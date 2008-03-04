/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

//#define DONT_RECORD_OBJALLOC_IN_OFFSETTABLE

namespace System.GCs {

    using Microsoft.Bartok.Runtime;
    using System.Runtime.CompilerServices;
    using System.Threading;

    [NoCCtor]
    internal abstract class GenerationalCollector: StopTheWorldCollector
    {

        [TrustedNonNull]
        internal static RememberedSet installedRemSet;

        // PageType.Owner0 is not 0 now. One side effect is that the following arrays
        // have unused entries.

        internal static short[]     gcCountTable;
        internal static short[]     gcFrequencyTable;

        internal static UIntPtr[]   gcPromotedTable;
        internal static UIntPtr[]   gcPromotedLimitTable;

        internal static int[]       fromSpacePageCounts;

        private  static UIntPtr     previousPageCount;
        internal static UIntPtr     nurserySize;
        internal static UIntPtr     pretenuredSinceLastFullGC;
        internal const  PageType    nurseryGeneration     = PageType.Owner0;
        internal const  PageType    largeObjectGeneration = PageType.Owner1;
        internal static PageType    defaultGeneration;

        internal static PageType MIN_GENERATION {
            get { return PageType.Owner0; }
        }
        internal static PageType MAX_GENERATION {
            get { return PageType.Owner1; }
        }

        // These two arrays contain the range of pages each generation is within
        internal static UIntPtr[] MinGenPage;
        internal static UIntPtr[] MaxGenPage;

        [PreInitRefCounts]
        internal static new void Initialize() {
            if (gcCountTable != null) {
                return;
            }
            GC.Initialize();
            StopTheWorldCollector.Initialize();
            defaultGeneration = MAX_GENERATION;
            uint tableSize = (uint) MAX_GENERATION+1;
            nurserySize    = (UIntPtr) (1 << 24);
            // gcCountTable = new short[MAX_GENERATION+1];
            gcCountTable = (short[])
                BootstrapMemory.Allocate(typeof(short[]), tableSize);
            // gcFrequencyTable = new short[MAX_GENERATION+1];
            gcFrequencyTable = (short[])
                BootstrapMemory.Allocate(typeof(short[]), tableSize);
            // gcPromotedTable = new UIntPtr[MAX_GENERATION+1];
            gcPromotedTable = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]), tableSize);
            // gcPromotedLimitTable = new UIntPtr[MAX_GENERATION+1];
            gcPromotedLimitTable = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]), tableSize);
            previousPageCount = UIntPtr.Zero;
            pretenuredSinceLastFullGC = UIntPtr.Zero;
            // fromSpacePageCounts = new int[MAX_GENERATION+1];
            fromSpacePageCounts = (int[])
                BootstrapMemory.Allocate(typeof(int[]),
                                         (uint)MAX_GENERATION+1);

            MinGenPage = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]),
                                         (uint)MAX_GENERATION+1);
            MaxGenPage = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]),
                                         (uint)MAX_GENERATION+1);


            // Initialization of tables
            // gcFrequencyTable and gcPromotedLimitTable may be modified by
            // VTable.ParseArgs
            gcFrequencyTable[(int)MIN_GENERATION] = Int16.MaxValue;
            gcPromotedLimitTable[(int)MIN_GENERATION] = (UIntPtr)1 << 25; // 32MB
            // Install the remembered set
            InstallRemSet();
        }

        private static void InstallRemSet() {
            switch (GC.remsetType) {
              case RemSetType.SSB: {
                  SequentialStoreBuffer.Initialize();
                  installedRemSet = SequentialStoreBuffer.instance;
                  break;
              }
              case RemSetType.Cards: {
                  CardTable.Initialize();
                  installedRemSet = CardTable.instance;
                  break;
              }
              default: {
                  VTable.NotReached("Unsupported remembered set: "+
                                    GC.remsetType);
                  break;
              }
            }
        }

        private void TruncateAllocationAreas(int generation) {
            this.TruncateNurseryAllocationAreas();
            if (generation != (int) nurseryGeneration) {
                this.TruncateOlderAllocationAreas(generation);
            }
        }

        private void TruncateNurseryAllocationAreas() {
            int limit = Thread.threadTable.Length;
            for (int i = 0; i < limit; i++) {
                Thread t = Thread.threadTable[i];
                if (t != null) {
                    BumpAllocator.Truncate(t);
                }
            }
        }

        internal abstract void TruncateOlderAllocationAreas(int generation);

        internal override void CollectStopped(int currentThreadIndex,
                                              int generation)
        {
            VTable.Assert(currentThreadIndex == collectorThreadIndex);
            int maxGeneration = (int) MAX_GENERATION;
            if (generation < 0) {
                if (generation == -1) {
                    generation = MinGeneration;
                    while (generation < maxGeneration &&
                           gcCountTable[generation] >=
                           gcFrequencyTable[generation]) {
                        generation++;
                    }
                    while (generation < maxGeneration &&
                           gcPromotedTable[generation] >=
                           gcPromotedLimitTable[generation]) {
                        generation++;
                    }
                } else {
                    generation = maxGeneration;
                }
            } else {
                generation = Math.Max(generation, MinGeneration);
                generation = Math.Min(generation, maxGeneration);
            }
            generation = this.CollectionGeneration(generation);
            for (int i = MinGeneration; i < generation; i++) {
                gcCountTable[i] = 0;
            }
            gcCountTable[generation]++;
            this.TruncateAllocationAreas(generation);
            UIntPtr heapPageCount = MarkZombiePages(generation);
            //VTable.DebugPrint("[GC gen {0}]\n", __arglist(generation));
            this.CollectGeneration(generation, heapPageCount);
            ReclaimZombiePages(heapPageCount, generation);
            for (int i = MinGeneration; i < generation; i++) {
                gcPromotedTable[i] = UIntPtr.Zero;
            }
        }

        internal abstract void CollectGeneration(int generation,
                                                 UIntPtr sourcePageCount);

        [Inline]
        internal override UIntPtr AllocateObjectMemory(UIntPtr numBytes,
                                                       uint alignment,
                                                       Thread currentThread)
        {
            UIntPtr resultAddr =
                BumpAllocator.AllocateFast(currentThread, numBytes, alignment);
            if (resultAddr == UIntPtr.Zero) {
                resultAddr = AllocateObjectMemorySlow(numBytes, alignment,
                                                      currentThread);
            }

            VTable.Assert(resultAddr != UIntPtr.Zero);
            return resultAddr;
        }

        [NoInline]
        private UIntPtr AllocateObjectMemorySlow(UIntPtr numBytes,
                                                 uint alignment,
                                                 Thread currentThread)
        {
            //Trace.Log(Trace.Area.Allocate,
            //          "AllocateObjectMemorySlow numBytes={0}, alignment={1}, currentThread={2}", __arglist(numBytes, alignment, currentThread));
            GC.CheckForNeededGCWork(currentThread);
            VTable.Assert(CurrentPhase != STWPhase.SingleThreaded ||
                          currentThread.threadIndex==collectorThreadIndex);
            if (GenerationalCollector.IsLargeObjectSize(numBytes)) {
                return AllocateBig(numBytes, alignment, currentThread);
            }
            return BumpAllocator.Allocate(currentThread, numBytes, alignment);
        }

        internal override int GetGeneration(Object obj) {
            UIntPtr addr = Magic.addressOf(obj);
            UIntPtr page = PageTable.Page(addr);
            return (int) PageTable.Type(page);
        }

        internal override int MaxGeneration {
            get { return (int)MAX_GENERATION; }
        }

        internal override int MinGeneration {
            get { return (int)MIN_GENERATION; }
        }

        internal override long TotalMemory {
            get {
                UIntPtr pageCount = UIntPtr.Zero;
                for (UIntPtr i=UIntPtr.Zero; i<PageTable.pageTableCount; i++) {
                    if (PageTable.IsMyGcPage(i)) {
                        pageCount++;
                    }
                }
                return (long) PageTable.RegionSize(pageCount);
            }
        }

        internal override void EnableHeap() {
            // Do nothing
        }

        internal override void DestructHeap() {
            // Do nothing
        }

        internal override void VerifyHeap(bool beforeCollection) {
            this.TruncateAllocationAreas((int)MAX_GENERATION);
            Verifier.bumpAllocatorVerifier.VerifyHeap();
        }

        internal override UIntPtr FindObjectAddr(UIntPtr interiorPtr) {
            return InteriorPtrTable.Find(interiorPtr);
        }

        internal override
        void VisitObjects(ObjectLayout.ObjectVisitor objectVisitor,
                          UIntPtr lowAddr, UIntPtr highAddr)
        {
            UIntPtr oldAddr = UIntPtr.Zero;
            UIntPtr objectAddr = lowAddr + PreHeader.Size;
            objectAddr = BumpAllocator.SkipNonObjectData(objectAddr, highAddr);
            while (objectAddr < highAddr) {
                if (PageTable.Page(objectAddr) != PageTable.Page(oldAddr)) {
                    InteriorPtrTable.VerifyFirst(oldAddr, objectAddr);
                }
                oldAddr = objectAddr;
                Object obj = Magic.fromAddress(objectAddr);
                UIntPtr objectSize = objectVisitor.Visit(obj);
                objectAddr += objectSize;
                objectAddr =
                    BumpAllocator.SkipNonObjectData(objectAddr, highAddr);
            }
            VTable.Assert(objectAddr - PreHeader.Size <= highAddr);
        }

        internal override void NewThreadNotification(Thread newThread,
                                                     bool initial)
        {
            base.NewThreadNotification(newThread, initial);
            BumpAllocator.NewThreadNotification(newThread, nurseryGeneration);
        }

        internal override void DeadThreadNotification(Thread deadThread)
        {
            installedRemSet.Clean(deadThread);
            base.DeadThreadNotification(deadThread);
        }

        //=======================================
        // Routines to manipulate from-space tags

        [Inline]
        internal unsafe static void MakeZombiePage(UIntPtr page,
                                                   PageType destGeneration)
        {
            VTable.Assert(destGeneration >= nurseryGeneration &&
                          destGeneration <= MAX_GENERATION);
            PageType pageType = PageTable.LiveToZombie(destGeneration);
            PageTable.SetType(page, pageType);
        }

        [Inline]
        internal unsafe static void MakeZombiePages(UIntPtr startPage,
                                                    UIntPtr pageCount,
                                                    PageType destGeneration)
        {
            VTable.Assert(destGeneration >= nurseryGeneration &&
                          destGeneration <= MAX_GENERATION);
            PageType pageType = PageTable.LiveToZombie(destGeneration);
            PageTable.SetType(startPage, pageCount, pageType);
        }

        [Inline]
        internal static bool IsMyZombiePage(UIntPtr page) {
            return (PageTable.IsMyPage(page) &&
                    PageTable.IsZombiePage(PageTable.Type(page)));
        }

        internal static UIntPtr PretenureSoftGCTrigger {
            [Inline]
            get { return (UIntPtr) (1 << 26); }
        }

        internal static UIntPtr PretenureHardGCTrigger {
            [Inline]
            get { return (UIntPtr) (1 << 27); }
        }

        [Inline]
        internal static bool IsLargeObjectSize(UIntPtr size) {
            UIntPtr largeObjectSize =
                (UIntPtr) (1 << Constants.LargeObjectBits);
            return size >= largeObjectSize;
        }

        internal override int CollectionGeneration(int genRequest) {
            if (pretenuredSinceLastFullGC > PretenureSoftGCTrigger) {
                genRequest = GC.MaxGeneration;
            }

            if (genRequest == GC.MaxGeneration) {
                pretenuredSinceLastFullGC = UIntPtr.Zero;
            }

            return genRequest;
        }

        internal static UIntPtr MarkZombiePages(int generation) {
            for (int i = (int)MIN_GENERATION; i <= generation; i++) {
                fromSpacePageCounts[i] = 0;
            }
            // Count the number of pages belonging to each generation.
            // Mark pages in the target generations as zombies.
            UIntPtr heapPageCount = UIntPtr.Zero;

            // determine the range of pages to loop through.
            // Our target generations are "generation" and all younger ones.
            // Therefore to ensure we get the right range, we must union the
            // ranges of all the target generations
            UIntPtr minPage = MinGenPage[generation];
            UIntPtr maxPage = MaxGenPage[generation];

            for (int i = 0; i < generation; i++) {
                if (MinGenPage[i] < minPage)
                {
                    minPage = MinGenPage[i];
                }
                if (MaxGenPage[i] > maxPage)
                {
                    maxPage = MaxGenPage[i];
                }
            }

            // This is used to determine the new range for the target generations
            UIntPtr newMinPage = PageTable.pageTableCount;
            UIntPtr newMaxPage = (UIntPtr)0;

            for (UIntPtr i = minPage; i <= maxPage; i++) {
                if (PageTable.IsMyGcPage(i)) {
                    heapPageCount++;
                    PageType pageType = PageTable.Type(i);
                    int pageGen = (int) pageType;
                    if (pageGen <= generation) {
                        fromSpacePageCounts[pageGen]++;
                        if (pageGen < (int)MAX_GENERATION) {
                            MakeZombiePage(i, (PageType)(pageGen+1));
                        } else {
                            MakeZombiePage(i, MAX_GENERATION);
                        }

                        if (i < newMinPage)
                        {
                            newMinPage = i;
                        }

                        // for loop is always increasing, so MaxNurseryPage will
                        // never be greater than i
                        newMaxPage = i;
                    }
                }
            }

            // update this generation with the new range.
            // This info is used by ReclaimZombiePages

            MinGenPage[generation] = newMinPage;
            MaxGenPage[generation] = newMaxPage;

            return heapPageCount;
        }

        internal static void ReclaimZombiePages(UIntPtr heapPageCount,
                                                int generation) {
            UIntPtr reservePages;
            if (heapPageCount < previousPageCount) {
                reservePages = (previousPageCount + heapPageCount) / 2;
            } else {
                reservePages = heapPageCount;
            }
            previousPageCount = heapPageCount;

            // MarkZombiePages updates the range for this generation, so we do
            // not need to take the union ranges of all target generations
            UIntPtr minZombiePage = MinGenPage[generation];
            UIntPtr maxZombiePage = MaxGenPage[generation];

            for (UIntPtr i = minZombiePage; i <= maxZombiePage; i++) {
                if (IsMyZombiePage(i)) {
                    UIntPtr startPage = i;
                    UIntPtr endPage = startPage;
                    do {
                        endPage++;
                    } while (IsMyZombiePage(endPage));
                    InteriorPtrTable.ClearFirst(startPage, endPage);
                    if (GC.remsetType == RemSetType.Cards) {
                       OffsetTable.ClearLast(PageTable.PageAddr(startPage), 
                                             PageTable.PageAddr(endPage)-1);
                    }
                    if (reservePages > UIntPtr.Zero) {
                        // Keep sufficient pages for the new nursery
                        UIntPtr pageCount = endPage - startPage;
                        if (pageCount > reservePages) {
                            // Zero out the memory for reuse
                            PageManager.ReleaseUnusedPages(startPage,
                                                           reservePages,
                                                           false);
                            startPage += reservePages;
                            PageManager.FreePageRange(startPage, endPage);
                            reservePages = UIntPtr.Zero;
                        } else {
                            // Zero out the memory for reuse
                            PageManager.ReleaseUnusedPages(startPage,
                                                           pageCount,
                                                           false);
                            reservePages = reservePages - pageCount;
                        }
                    } else {
                        PageManager.FreePageRange(startPage, endPage);
                    }
                    i = endPage - 1;
                }
            }
        }

        // Interface with the compiler!

        internal static unsafe UIntPtr AllocateBig(UIntPtr numBytes,
                                                   uint alignment,
                                                   Thread currentThread)
        {
            // Pretenure Trigger
            pretenuredSinceLastFullGC += numBytes;
            if (pretenuredSinceLastFullGC > PretenureHardGCTrigger) {
                GC.InvokeMajorCollection(currentThread);
            }

            // Potentially Join a collection
            GC.CheckForNeededGCWork(currentThread);
            int maxAlignmentOverhead = unchecked((int)alignment)-UIntPtr.Size;
            UIntPtr pageCount =
                PageTable.PageCount(numBytes + maxAlignmentOverhead);
            bool fCleanPages = true;
            UIntPtr page = PageManager.EnsurePages(currentThread, pageCount,
                                                   largeObjectGeneration,
                                                   ref fCleanPages);
            int unusedBytes =
                unchecked((int) (PageTable.RegionSize(pageCount) - numBytes));
            int unusedCacheLines =
                unchecked((int) (unusedBytes - maxAlignmentOverhead)) >> 5;
            int pageOffset = 0;
            if (unusedCacheLines != 0) {
                pageOffset = (bigOffset % unusedCacheLines) << 5;
                bigOffset++;
            }
            UIntPtr pageStart = PageTable.PageAddr(page);
            for (int i = 0; i < pageOffset; i += UIntPtr.Size) {
                Allocator.WriteAlignment(pageStart + i);
            }
            UIntPtr unalignedStartAddr = pageStart + pageOffset;
            UIntPtr startAddr =
                Allocator.AlignedAllocationPtr(unalignedStartAddr,
                                               pageStart + unusedBytes,
                                               alignment);
            pageOffset +=
                unchecked((int)(uint)(startAddr - unalignedStartAddr));
            if (pageOffset < unusedBytes) {
                BumpAllocator.WriteUnusedMarker(pageStart+pageOffset+numBytes);
            }
            UIntPtr resultAddr = startAddr + PreHeader.Size;
            InteriorPtrTable.SetFirst(resultAddr);
            VTable.Assert(PageTable.Page(resultAddr) <
                          PageTable.Page(startAddr+numBytes-1),
                          "Big object should cross pages");
            if (GC.remsetType == RemSetType.Cards) {
#if DONT_RECORD_OBJALLOC_IN_OFFSETTABLE
#else
                     OffsetTable.SetLast(resultAddr);   
#endif
            }      
            return resultAddr;
        }

        private static int bigOffset;

        // Utility functions

        internal override void CheckForNeededGCWork(Thread currentThread)
        {
            base.CheckForNeededGCWork(currentThread);
            if (CurrentPhase == STWPhase.Idle &&
                GC.newBytesSinceGC >= nurserySize &&
                !GC.allocationInhibitGC) {
                GC.InvokeCollection(currentThread);
            }
        }

    }

}
