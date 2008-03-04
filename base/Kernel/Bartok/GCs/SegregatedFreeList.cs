/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace System.GCs {

    using Microsoft.Bartok.Options;
    using Microsoft.Bartok.Runtime;

    using System.Runtime.CompilerServices;
    using System.Threading;

#if SINGULARITY
    using Microsoft.Singularity;
#endif

    /// <summary>
    /// This class supports Allocate/Free operations using memory obtained
    /// from the PageManager.
    ///
    /// Objects that are small enough that multiple instances of them can fit
    /// on a page are allocated from pages of identically sized memory cells.
    ///
    /// Large objects are allocated on separate pages.
    ///
    /// This class is designed to be thread safe on both allocation and free
    /// operations. At this stage it is required that RecycleGlobalPages is
    /// called periodically to reconsider previously full pages for inclusion
    /// in allocation lists.
    ///
    /// The free operation is currently more expensive than required due to
    /// space accounting.
    ///
    /// This class also keeps up to date memory accounting information. Note
    /// that modifications to this data is not synchronized so it is possible
    /// that the counts drift from actual figures over time.
    /// </summary>
    internal unsafe struct SegregatedFreeList /* : Allocator */
    {

        #region Mixins

        [MixinConditional("SegregatedFreeList")]
        [MixinConditional("AllThreadMixins")]
        [Mixin(typeof(Thread))]
        private class SegregatedFreeListThread : Object {
            // Thread-specific segregated free-list allocator
            [RequiredByBartok]
            internal SegregatedFreeList segregatedFreeList;
        }

        [Inline]
        private static SegregatedFreeListThread MixinThread(Thread t)
        {
            return (SegregatedFreeListThread) (Object) t;
        }

        #endregion

        #region Global (Safe from the context of any thread)

        #region Constants
        /// <summary>
        /// Small objects.
        /// </summary>
        internal const PageType SMALL_OBJ_PAGE = PageType.Owner2;

        /// <summary>
        /// This is a page that is being set up.
        /// </summary>
        internal const PageType INIT_PAGE = PageType.Owner4;

        /// <summary>
        /// Large objects.
        /// </summary>
        internal const PageType LARGE_OBJ_PAGE = PageType.Owner3;

        /// <summary>
        /// BUGBUG: Duplicated constant.
        /// </summary>
        internal const int LOG_PAGE_SIZE = 12;

        /// <summary>
        /// The size of each block (which contains a single object size)
        /// </summary>
        internal const int BLOCK_SIZE = (1 << LOG_PAGE_SIZE) - PageHeader.SIZEOF;

        /// <summary>
        /// The threshold where objects become large objects.
        /// </summary>
        internal const int LARGE_OBJECT_THRESHOLD = (BLOCK_SIZE >> 1) + 1;

        /// <summary>
        /// The largest possible size class.
        /// </summary>
        internal const int SIZE_CLASSES = 33 + ((LARGE_OBJECT_THRESHOLD-1) >> 7);
        #endregion

        #region Size Class Mappings
        /// <summary>
        /// Returns the size class index for an object of the specified size.
        ///
        /// BUGBUG: We hope that from an inlined allocation site this is
        /// resolved as a constant. That is why this was changed to remove
        /// indirection through an index lookup table.
        /// </summary>
        [Inline]
        private static int GetSizeClass(UIntPtr bytes) {
            // Exact request rounds down to lower size class.
            bytes = bytes - new UIntPtr(1);
            // Sizes 0 -> 64 are in classes 0 -> 15
            // Sizes 72 -> 128 are in classes 16 -> 23
            // Sizes 140 -> 256 are in classes 24 -> 31
            // Sizes 256 -> 512 are in classes 32 -> 35
            // We skip a power of 2 because large objects are rare.
            //Sizes 512 and up are in classes 36 -> 41
            if (bytes <  64) return  0 + ((int)bytes >> 2);
            if (bytes < 128) return  8 + ((int)bytes >> 3);
            if (bytes < 256) return 16 + ((int)bytes >> 4);
            if (bytes < 512) return 28 + ((int)bytes >> 6);
            if (bytes < LARGE_OBJECT_THRESHOLD) return 32 + ((int)bytes >> 7);
            throw new Exception("GetSizeClass called on large object size");
        }

        /// <summary>
        /// Returns the cell size for a given size class.
        /// </summary>
        private static UIntPtr GetCellSize(int sizeClass) {
            VTable.Assert(sizeClass < SIZE_CLASSES,
                          "Attempt cellSize for invalid sizeClass");

            uint sc = (uint)sizeClass + 1;

            uint bytes;
            if      (sc <= 16) bytes = (sc -  0) << 2;
            else if (sc <= 24) bytes = (sc -  8) << 3;
            else if (sc <= 32) bytes = (sc - 16) << 4;
            else if (sc <= 36) bytes = (sc - 28) << 6;
            else               bytes = (sc - 32) << 7;

            return new UIntPtr(bytes);
        }
        #endregion

        #region Memory Accounting
        /// <summary>
        /// A count of the bytes allocated for small objects.
        /// </summary>
        internal static UIntPtr SmallBytes;

        /// <summary>
        /// A count of the bytes for objects the process of being freed.
        /// </summary>
        internal static UIntPtr SmallFreedBytes;

        /// <summary>
        /// A count of the small pages in the process of being freed.
        /// </summary>
        internal static UIntPtr SmallFreedPages;

        /// <summary>
        /// A count of the large pages in the process of being freed.
        /// </summary>
        internal static UIntPtr LargeFreedPages;

        /// <summary>
        /// A count of the bytes allocated for large objects.
        /// </summary>
        internal static UIntPtr LargeBytes {
            get {
                return PageTable.RegionSize(LargePages);
            }
        }

        /// <summary>
        /// The number of pages reserved for large objects.
        /// </summary>
        internal static UIntPtr LargePages;

        /// <summary>
        /// The number of pages reserved for small objects.
        /// </summary>
        internal static UIntPtr SmallPages;

        /// <summary>
        /// This is the total size of data (including object headers)
        /// </summary>
        internal static UIntPtr TotalBytes {
            get {
                return SmallBytes + LargeBytes;
            }
        }

        /// <summary>
        /// The number of pages managed by the alloc heap, including space
        /// occupied by empty cells.
        /// </summary>
        internal static UIntPtr ReservedBytes {
            get {
                return PageTable.RegionSize(SmallPages) + LargeBytes;
            }
        }

        /// <summary>
        /// Increment the counter of the number of small bytes allocated.
        /// </summary>
        [Inline]
        private static void AddSmallBytes(UIntPtr newBytes) {
            SmallBytes += newBytes;
            GC.newBytesSinceGC += newBytes;
        }

        /// <summary>
        /// Increment the counter of the number of large bytes allocated.
        /// </summary>
        [Inline]
        private static void AddLargePages(UIntPtr pageCount) {
            LargePages += pageCount;
            GC.newBytesSinceGC += PageTable.RegionSize(pageCount);
        }

        /// <summary>
        /// Decrement the counter of the number of small bytes allocated.
        /// </summary>
        [Inline]
        private static void SubSmallBytes(UIntPtr newBytes) {
            SmallFreedBytes += newBytes;
        }

        /// <summary>
        /// Decrement the counter of the number of large pages allocated.
        /// </summary>
        [Inline]
        private static void SubLargePages(UIntPtr pageCount) {
            LargeFreedPages += pageCount;
        }

        /// <summary>
        /// Increment the counter of the number of small pages allocated.
        /// </summary>
        [Inline]
        private static void AddSmallPage() {
            SmallPages += new UIntPtr(1);
            AddSmallBytes(PageTable.PageSize);
        }

        /// <summary>
        /// Decrement the counter of the number of small pages allocated.
        /// </summary>
        [Inline]
        private static void SubSmallPage(PageHeader *page) {
            SmallFreedPages += new UIntPtr(1);
            SubSmallBytes(PageTable.PageSize -
                          (page->cellSize * (UIntPtr) page->freeCount));
        }

        /// <summary>
        /// Subtract and zero the freed data counts.
        /// </summary>
        internal static void CommitFreedData() {
            SmallPages -= SmallFreedPages;
            SmallFreedPages = new UIntPtr(0);
            LargePages -= LargeFreedPages;
            LargeFreedPages = new UIntPtr(0);
            SmallBytes -= SmallFreedBytes;
            SmallFreedBytes = new UIntPtr(0);
        }
        #endregion

        /// <summary>
        /// When notified of the creation of a new thread we initialize the
        /// alloc heap in that thread.
        /// </summary>
        internal unsafe static void NewThreadNotification(Thread newThread,
                                                          bool initial)
        {
            SegregatedFreeListThread mixinThread = MixinThread(newThread);
            if (initial) {
                // Initialise the initial thread.
                mixinThread.segregatedFreeList.localPages = (PageHeader*[])
                    BootstrapMemory.Allocate(typeof(PageHeader*[]),
                                             SIZE_CLASSES);
                mixinThread.segregatedFreeList.freeList = (UIntPtr[])
                    BootstrapMemory.Allocate(typeof(UIntPtr[]), SIZE_CLASSES);
            } else {
                // We have to create the thread-specific array of pages.
                mixinThread.segregatedFreeList.freeList =
                    new UIntPtr[SIZE_CLASSES];
                mixinThread.segregatedFreeList.localPages =
                    new PageHeader*[SIZE_CLASSES];
            }
        }

        /// <summary>
        /// A thread has finished, so we release any local pages.
        /// </summary>
        internal static void DeadThreadNotification(Thread deadThread)
        {
            SegregatedFreeListThread mixinThread = MixinThread(deadThread);
            for(int i=0; i < SIZE_CLASSES; i++) {
                if (mixinThread.segregatedFreeList.localPages[i] != null) {
                    mixinThread.segregatedFreeList.ReleaseLocalPage(i);
                }
            }
        }

        /// <summary>
        /// Allocate a large object. Large objects don't share pages with
        /// any other objects. Get memory for the object directly from the
        /// PageManager.
        /// </summary>
        [ManualRefCounts]
        private static unsafe UIntPtr AllocateLarge(uint alignment,
                                                    UIntPtr bytes,
                                                    Thread currentThread)
        {
            UIntPtr pageCount = PageTable.PageCount(PageTable.PagePad(bytes));
            bool fCleanPages = true;
            UIntPtr page = PageManager.EnsurePages(currentThread,
                                                   pageCount, INIT_PAGE,
                                                   ref fCleanPages);

            UIntPtr regionSize = PageTable.RegionSize(pageCount);
            AddLargePages(pageCount);

            int unusedBytes = (int) (regionSize - bytes);
            int unusedCacheLines = unusedBytes >> 5;
            int offset;
            if (unusedCacheLines != 0) {
                offset = (largeOffset % unusedCacheLines) << 5;
                largeOffset =
                    (largeOffset + 1) & ((int)PageTable.PageSize - 1);
            } else {
                offset = 0;
            }
            UIntPtr pageAddr = PageTable.PageAddr(page);
            UIntPtr startAddr = pageAddr + offset + PreHeader.Size;
            UIntPtr resultAddr =
                Allocator.AlignedObjectPtr(startAddr, alignment);
            short shortOff = (short) (resultAddr - pageAddr);
            VTable.Assert(shortOff > 0, "offset not positive");
            PageTable.SetExtra(page, shortOff);
            // Ready to be visited
            PageTable.SetType(page, pageCount, LARGE_OBJ_PAGE);
            return resultAddr;
        }

        /// <summary>
        /// Free the specified object. For large objects the page becomes
        /// immediately available. For small objects it may require a call
        /// to RecycleGlobalPages.
        /// </summary>
        [ManualRefCounts]
        internal static void Free(Object obj) {
            UIntPtr objectStart = Magic.addressOf(obj) - PreHeader.Size;
            UIntPtr page = PageTable.Page(objectStart);
            PageType pageType = PageTable.Type(page);
            // Free the object
            if (pageType == SMALL_OBJ_PAGE) {
                FreeSmall(obj);
            } else {
                VTable.Assert(pageType == LARGE_OBJ_PAGE,
                    "Found GC Page not small or large");
                FreeLarge(obj);
            }
        }

        /// <summary>
        /// Free a small object.
        /// </summary>
        [ManualRefCounts]
        internal static void FreeSmall(Object obj) {
            UIntPtr objectAddr = Magic.addressOf(obj);
            // Put the object memory cell on the freelist for the page
            UIntPtr pageAddr = PageTable.PageAlign(objectAddr);
            PageHeader *pageHeader = (PageHeader*) pageAddr;
            UIntPtr cellStart;
            if (obj.vtable.baseAlignment > UIntPtr.Size) {
                cellStart = FindSmallCell(objectAddr);
                VTable.Assert(cellStart > (UIntPtr) pageHeader &&
                              cellStart <= objectAddr &&
                              cellStart+pageHeader->cellSize >= objectAddr,
                              "find small cell found invalid start");
            } else {
                cellStart = objectAddr - PreHeader.Size;
            }
            Util.MemClear(cellStart, pageHeader->cellSize);
            SubSmallBytes(pageHeader->cellSize);
            UIntPtr oldFreeList;
            cellStart = (cellStart + PreHeader.Size);
            do {
                oldFreeList = pageHeader->freeList;
                *(UIntPtr *)cellStart = oldFreeList;
            } while (Interlocked.CompareExchange(ref pageHeader->freeList,
                cellStart, oldFreeList) != oldFreeList);
            Interlocked.Increment(ref pageHeader->freeCount);
        }

        internal unsafe struct TempList {

            private UIntPtr next;

            internal void Add(UIntPtr memAddr) {
                *(UIntPtr *) memAddr = this.next;
                this.next = memAddr;
            }

            internal UIntPtr GetList() {
                UIntPtr result = this.next;
                this.next = UIntPtr.Zero;
                return result;
            }

        }

        [ManualRefCounts]
        internal static void FreeSmallList(ref TempList tempList)
        {
            UIntPtr cell = tempList.GetList();
            if (cell != UIntPtr.Zero) {
                PageHeader *pageHeader = (PageHeader *)
                    PageTable.PageAlign(cell);
                UIntPtr newChain = UIntPtr.Zero;
                UIntPtr newChainTail = cell + PreHeader.Size;
                UIntPtr cellSize = pageHeader->cellSize;
                int count = 0;
                do {
                    count++;
                    UIntPtr next = *(UIntPtr *) cell;
                    Util.MemClear(cell, cellSize);
                    UIntPtr cellPtrAddr = cell + PreHeader.Size;
                    *(UIntPtr *) cellPtrAddr = newChain;
                    newChain = cellPtrAddr;
                    cell = next;
                } while (cell != UIntPtr.Zero);
                SubSmallBytes((UIntPtr) count * cellSize);
                UIntPtr oldFreeList;
                do {
                    oldFreeList = pageHeader->freeList;
                    *(UIntPtr*)newChainTail = oldFreeList;
                } while (Interlocked.CompareExchange(ref pageHeader->freeList,
                                                     newChain, oldFreeList) !=
                         oldFreeList);
                int oldFreeCount, newFreeCount;
                do {
                    oldFreeCount = pageHeader->freeCount;
                    newFreeCount = oldFreeCount + count;
                } while (Interlocked.CompareExchange(ref pageHeader->freeCount,
                                                     newFreeCount,
                                                     oldFreeCount) !=
                         oldFreeCount);
            }
        }

        /// <summary>
        /// Free a large object.
        /// </summary>
        [ManualRefCounts]
        internal static void FreeLarge(Object obj) {
            UIntPtr objectStart = Magic.addressOf(obj) - PreHeader.Size;
            UIntPtr firstPage = PageTable.Page(objectStart);
            // Release the page(s) that the object resides on
            UIntPtr pageAddr = PageTable.PageAlign(objectStart);
            UIntPtr objectSize = ObjectLayout.Sizeof(obj);
            UIntPtr limitPage =
                PageTable.Page(PageTable.PagePad(objectStart + objectSize));
            UIntPtr pageCount = limitPage - firstPage;
            PageTable.SetType(firstPage, pageCount, INIT_PAGE);
            PageTable.SetExtra(PageTable.Page(pageAddr), 0);
            PageManager.ReleaseUnusedPages(firstPage, pageCount, false);
            SubLargePages(pageCount);
        }

        /// <summary>
        /// Given a possibly interior pointer to an object, return the
        /// real address of the object.
        /// </summary>
        internal static unsafe UIntPtr Find(UIntPtr addr) {
            UIntPtr page = PageTable.Page(addr);
            PageType pageType = PageTable.Type(page);
            VTable.Assert(PageTable.IsGcPage(pageType),
                          "Attempt find on non-GC page");
            if (pageType == SMALL_OBJ_PAGE) {
                return FindSmall(addr);
            } else {
                return FindLarge(addr);
            }
        }

        /// <summary>
        /// Find a small object (after determining it is on a small page)
        /// </summary>
        private static UIntPtr FindSmall(UIntPtr cellAddr) {
            UIntPtr objectAddr = FindSmallCell(cellAddr) + PreHeader.Size;
            objectAddr = Allocator.SkipAlignment(objectAddr);
            return objectAddr;
        }

        /// <summary>
        /// Find the cell for a given object address
        /// </summary>
        private static unsafe UIntPtr FindSmallCell(UIntPtr addr) {
            UIntPtr pageAddr = PageTable.PageAlign(addr - PreHeader.Size);
            PageHeader *pageHeader = (PageHeader *) pageAddr;
            UIntPtr firstAddr = pageAddr + PageHeader.SIZEOF;
            return
                addr - ((int)(addr - firstAddr) % (int)pageHeader->cellSize);
        }

        /// <summary>
        /// Find a large object (after determining it is on a large page)
        /// </summary>
        private static UIntPtr FindLarge(UIntPtr addr) {
            UIntPtr page = PageTable.Page(addr - PreHeader.Size);
            short brickData = PageTable.Extra(page);
            while (brickData == 0) {
                page--;
                brickData = PageTable.Extra(page);
                VTable.Assert(PageTable.Type(page) == LARGE_OBJ_PAGE,
                              "page type invalid");
            }
            return (PageTable.PageAddr(page) + brickData);
        }

        internal abstract class ObjectVisitor : ObjectLayout.ObjectVisitor {

            internal virtual void VisitSmall(Object obj, UIntPtr memAddr) {
                this.Visit(obj);
            }

            internal virtual void VisitSmallPageEnd() { }

            internal virtual UIntPtr VisitLarge(Object obj) {
                return this.Visit(obj);
            }

            internal override UIntPtr Visit(Object obj) {
                VTable.NotReached("Someone forgot an override method in a "+
                                  "subclass of SegregatedFreeList.ObjectVisitor");
                return UIntPtr.Zero;
            }

        }

        // Wraps an SegregatedFreeList.ObjectVisitor around a
        // ObjectLayout.ObjectVisitor.
        // Both large and small objects are visited by the same
        // ObjectLayout.ObjectVisitor.
        internal class ObjectVisitorWrapper : ObjectVisitor {

            private ObjectLayout.ObjectVisitor visitor;

            internal ObjectVisitorWrapper(ObjectLayout.ObjectVisitor visitor) {
                this.visitor = visitor;
            }

            internal override void VisitSmall(Object obj, UIntPtr memAddr) {
                this.visitor.Visit(obj);
            }

            internal override UIntPtr VisitLarge(Object obj) {
                return this.visitor.Visit(obj);
            }

        }

        /// <summary>
        /// Visit each object in the heap across all pages.
        /// </summary>
        [ManualRefCounts]
        internal static void VisitAllObjects(ObjectVisitor visitor)
        {
            VisitObjects(UIntPtr.Zero, PageTable.pageTableCount, visitor);
        }

        /// <summary>
        /// Visit each object in the heap across a range of pages.
        ///
        /// This can be run concurrent to allocations, but not frees.
        /// </summary>
        [ManualRefCounts]
        internal static void VisitObjects(UIntPtr lowPage,
                                          UIntPtr highPage,
                                          ObjectVisitor visitor)
        {
            for (UIntPtr i = lowPage; i < highPage; i++) {
                PageType pageType = PageTable.MyType(i);
                if (pageType == INIT_PAGE) {
                    // Not yet ready for allocation so we can't visit it...
                } else if (pageType == SMALL_OBJ_PAGE) {
                    VisitSmallObjects(i, visitor);
                } else if (pageType == LARGE_OBJ_PAGE) {
                    UIntPtr j = i + 1;
                    while (j < highPage &&
                           PageTable.MyType(j) == LARGE_OBJ_PAGE) {
                        j++;
                    }
                    UIntPtr largeObjectPageCount =
                        VisitLargeObject(i, visitor);
                    i += largeObjectPageCount - 1;
                    VTable.Assert(i <= j);
                }
            }
        }

        internal static void VisitObjects(UIntPtr lowPage,
                                          UIntPtr highPage,
                                          ObjectLayout.ObjectVisitor visitor)
        {
            ObjectVisitor myObjectVisitor = visitor as ObjectVisitor;
            VTable.Assert(myObjectVisitor != null,
                          "SegregatedFreeList requires specialized ObjectVisitor");
            VisitObjects(lowPage, highPage, myObjectVisitor);
        }

        /// <summary>
        /// Visit small objects in a single page.
        /// </summary>
        [ManualRefCounts]
        private static unsafe void VisitSmallObjects(UIntPtr page,
                                                     ObjectVisitor visitor)
        {
            VTable.Assert(PageTable.Type(page) == SMALL_OBJ_PAGE,
                          "Visiting small objects on invalid page");
            UIntPtr pageAddr = PageTable.PageAddr(page);
            PageHeader *pageHeader = (PageHeader *) pageAddr;
            UIntPtr cellSize = pageHeader->cellSize;
            VTable.Assert(cellSize != UIntPtr.Zero,
                          "zero cellSize visiting small");
            UIntPtr lowAddr = pageAddr + PageHeader.SIZEOF;
            UIntPtr highAddr = PageTable.PagePad(lowAddr);
            while (lowAddr <= highAddr - cellSize) {
                UIntPtr objectAddr = lowAddr + PreHeader.Size;
                objectAddr = Allocator.SkipAlignment(objectAddr);
                Object maybeObject = Magic.fromAddress(objectAddr);
                UIntPtr vtablePtr = *maybeObject.VTableFieldAddr;
                if (vtablePtr != 0) {
                    // In Singularity, it is not always allowed
                    // to compute PageTable.Page(UIntPtr.Zero);
                    UIntPtr vtablePage = PageTable.Page(vtablePtr);
                    if (vtablePage != page) {
                        // We have found a slot containing an object
                        visitor.VisitSmall(maybeObject, lowAddr);
                    }
                }
                lowAddr += cellSize;
            }
            visitor.VisitSmallPageEnd();
        }

        /// <summary>
        /// Visit a large object on the specified page.
        /// </summary>
        [ManualRefCounts]
        private static UIntPtr VisitLargeObject(UIntPtr page,
                                                ObjectVisitor visitor)
        {
            VTable.Assert(PageTable.Type(page) == LARGE_OBJ_PAGE,
                          "Visiting large object on invalid page");
            // Find the object
            UIntPtr pageAddr = PageTable.PageAddr(page);
            short brickData = PageTable.Extra(page);
            if (brickData == 0) {
                // Possibly in the process of being allocated.
                return new UIntPtr(1);
            }
            UIntPtr objectAddr = pageAddr + brickData;
            Object obj = Magic.fromAddress(objectAddr);
            if (obj.vtable == null) {
                // Memory has been allocated, but object is not initialized
                return new UIntPtr(1);
            }
            // Visit the object
            UIntPtr objectSize = visitor.VisitLarge(obj);
            // Return the page count
            UIntPtr objectEnd = objectAddr + objectSize - PreHeader.Size;
            UIntPtr limitPage = PageTable.Page(PageTable.PagePad(objectEnd));
            return limitPage - page;
        }

        /// <summary>
        /// This is the the free list of pages to allocate from.
        /// </summary>
        private static UIntPtr[] globalFreePages;

        /// <summary>
        /// This is the list of pages released by threads. These pages must
        /// be periodically processes to release them back for allocation if
        /// necessary.
        /// </summary>
        private static UIntPtr[] globalPages;

        // Used by RecycleGlobalPages to avoid the ABA problem of
        // lock-free data structures.
        private static UIntPtr[] stolenGlobalFreePages;
        private static UIntPtr[] stolenGlobalFullPages;

        /// <summary>
        /// Initialize the alloc heap by setting up the heads for all the
        /// linked lists.
        /// </summary>
        [PreInitRefCounts]
        internal static unsafe void Initialize() {
            // Global array of allocated pages
            globalPages = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]), SIZE_CLASSES);
            // Global array of pages with free elements
            globalFreePages = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]), SIZE_CLASSES);
            // Temporary list holders used by RecycleGlobalPages
            stolenGlobalFullPages = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]), SIZE_CLASSES);
            stolenGlobalFreePages = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]), SIZE_CLASSES);
        }

        /// <summary>
        /// Take all global pages that have had elements freed and put them in
        /// the allocation queues.
        /// </summary>
        internal static void RecycleGlobalPages() {
            RecycleGlobalPagesPhase1();
            RecycleGlobalPagesPhase2();
        }

        internal static void RecycleGlobalPagesPhase1() {
            for (int i=0; i < SIZE_CLASSES; i++) {
                // Steal chains
                VTable.Assert(stolenGlobalFullPages[i] == UIntPtr.Zero);
                VTable.Assert(stolenGlobalFreePages[i] == UIntPtr.Zero);
                stolenGlobalFullPages[i] =
                    AtomicPopChain(ref globalPages[i]);
                stolenGlobalFreePages[i] =
                    AtomicPopChain(ref globalFreePages[i]);
            }
        }

        internal static void RecycleGlobalPagesPhase2() {
            for (int i=0; i < SIZE_CLASSES; i++) {
                UIntPtr globalFull = stolenGlobalFullPages[i];
                stolenGlobalFullPages[i] = UIntPtr.Zero;
                UIntPtr globalFree = stolenGlobalFreePages[i];
                stolenGlobalFreePages[i] = UIntPtr.Zero;

                // Start with free pages (they can not become full)
                UIntPtr current = globalFree;

                // New free and full chains
                PageHeader *freeHead = null, freeTail = null;
                PageHeader *fullHead = null, fullTail = null;

                // Determine starting point
                if (current == UIntPtr.Zero) {
                    current = globalFull;
                    globalFull = UIntPtr.Zero;

                    if (current == UIntPtr.Zero) {
                        continue;
                    }
                }

                // Number of cells of this size class in a block
                int cells = BLOCK_SIZE / (int) GetCellSize(i);
                VTable.Assert(cells > 0 && cells < (BLOCK_SIZE >> 1),
                              "invalid cell count");

                // Iterate through list
                while (current != UIntPtr.Zero) {
                    PageHeader *page = (PageHeader*) current;
                    current = page->nextPage;

                    if (page->freeCount == cells) {
                        // Completely Free Page
                        SubSmallPage(page);
                        UIntPtr pageNum = PageTable.Page(new UIntPtr(page));
                        PageTable.SetType(pageNum, INIT_PAGE);
                        PageTable.SetExtra(pageNum, 0);
                        PageManager.ReleaseUnusedPages(pageNum, new UIntPtr(1),
                                                       false);
                    } else if (page->freeCount > 0) {
                        // Partially Free Page
                        AddPageToList(page, ref freeHead, ref freeTail);
                    } else {
                        // Completely Full Page
                        AddPageToList(page, ref fullHead, ref fullTail);
                    }

                    if (current == UIntPtr.Zero) {
                        // Finished the free list, onto the full list.
                        current = globalFull;
                        globalFull = UIntPtr.Zero;
                    }
                }

                // Reinsert values onto the free and full chains
                if (fullHead != null) {
                    AtomicPushChain(ref globalPages[i],
                                    fullHead, fullTail);
                }

                if (freeHead != null) {
                    AtomicPushChain(ref globalFreePages[i],
                                    freeHead, freeTail);
                }
            }
        }

        /// <summary>
        /// Add a page onto a local linked list (possibly the first page)
        /// </summary>
        private static void AddPageToList(PageHeader *page,
                                          ref PageHeader *head,
                                          ref PageHeader *tail) {
            if (head == null) {
                page->nextPage = UIntPtr.Zero;
                head = page;
                tail = page;
                return;
            }
            tail->nextPage = new UIntPtr(page);
            tail = page;
        }

        /// <summary>
        /// Atomically push a value onto the linked list.
        /// </summary>
        private static void AtomicPush(ref UIntPtr head, PageHeader *page) {
            AtomicPushChain(ref head, page, page);
        }

        /// <summary>
        /// Atomically remove a value from the linked list. Returns null if the
        /// list is empty.
        /// </summary>
        private static UIntPtr AtomicPop(ref UIntPtr head) {
            UIntPtr oldHead;
            PageHeader *oldPage;
            UIntPtr newHead;
            do {
                oldHead = head;
                if (oldHead == UIntPtr.Zero) {
                    // Empty list.
                    return UIntPtr.Zero;
                }
                oldPage = (PageHeader*) oldHead;
                newHead = oldPage->nextPage;
            } while(oldHead != Interlocked.CompareExchange
                (ref head, newHead, oldHead));

            oldPage->nextPage = UIntPtr.Zero;
            return oldHead;
        }

        /// <summary>
        /// Steal an entire list.
        /// </summary>
        private static UIntPtr AtomicPopChain(ref UIntPtr head) {
            return Interlocked.Exchange(ref head, UIntPtr.Zero);
        }

        /// <summary>
        /// Push a whole chain onto a list.
        /// </summary>
        private static void AtomicPushChain(ref UIntPtr head,
                                            PageHeader *chainHead,
                                            PageHeader *chainTail) {
            UIntPtr oldHead;
            UIntPtr newHead = new UIntPtr(chainHead);
            do {
                oldHead = head;
                chainTail->nextPage = oldHead;
            } while (oldHead != Interlocked.CompareExchange
                (ref head, newHead, oldHead));
        }

        /// <summary>
        /// This struct represents the header data stored in each
        /// small object page.
        ///
        /// BUGBUG: Not space efficient.
        /// </summary>
        private struct PageHeader {
            internal const int SIZEOF = 16;

            /// <summary>
            /// The next page in the linked list.
            /// </summary>
            internal UIntPtr nextPage;

            /// <summary>
            /// The head of the free list for this page. This is not
            /// used when a page is assigned to a thread.
            /// </summary>
            internal UIntPtr freeList;

            /// <summary>
            /// The cell size for objects in this page.
            /// </summary>
            internal UIntPtr cellSize;

            /// <summary>
            /// The number of cells that have been freed. This is used
            /// for accounting purposes.
            /// </summary>
            internal int freeCount;
        }
        #endregion

        #region Local  (Safe from the context of owner thread)
        /// <summary>
        /// This is a thread's local free list for each size class.
        /// </summary>
        [RequiredByBartok]
        private UIntPtr[] freeList;

        /// <summary>
        /// This is a thread's local set of pages for each size class.
        /// </summary>
        private PageHeader*[] localPages;

        [ManualRefCounts]
        internal static UIntPtr Allocate(Thread thread,
                                         UIntPtr bytes, uint alignment)
        {
            SegregatedFreeListThread mixinThread = MixinThread(thread);
            return mixinThread.segregatedFreeList.Allocate(bytes, alignment,
                                                           thread);
        }

        [ManualRefCounts]
        internal UIntPtr Allocate(UIntPtr bytes, uint alignment, Thread thread)
        {
            UIntPtr resultAddr = this.AllocateFast(bytes, alignment);
            if (resultAddr == UIntPtr.Zero) {
                resultAddr = this.AllocateSlow(bytes, alignment, thread);
            }
            return resultAddr;
        }

        [Inline]
        [ManualRefCounts]
        internal static UIntPtr AllocateFast(Thread thread,
                                             UIntPtr bytes,
                                             uint alignment)
        {
            SegregatedFreeListThread mixinThread = MixinThread(thread);
            return mixinThread.segregatedFreeList.AllocateFast(bytes,
                                                               alignment);
        }

        [RequiredByBartok]
        [Inline]
        [DisableBoundsChecks]
        public static unsafe Object CompilerAllocateMarkSweep
        (VTable vtable, Thread currentThread, UIntPtr bytes, uint alignment)
        {
            VTable.Assert((alignment == 4)
                          || ((alignment == 8) && (UIntPtr.Size == 8))
                          || ((alignment == 8) && (PreHeader.Size == 4)),
                          "Unsupported object layout");
            VTable.Assert(UIntPtr.Size == PreHeader.Size,
                          "Unsupported preheader size");
            VTable.Assert
                (Util.IsAligned((uint) PreHeader.Size + (uint)PostHeader.Size,
                                alignment),
                 "Unsupported header sizes");
            VTable.Assert(bytes < LARGE_OBJECT_THRESHOLD,
                          "CompilerAllocate called for large object");

            // Room to ensure alignment
            bool alignRequired = (alignment > UIntPtr.Size);
            if (alignRequired) {
                bytes = bytes + alignment - UIntPtr.Size;
            }
            int sizeClass = GetSizeClass(bytes);
            SegregatedFreeListThread mixinThread = MixinThread(currentThread);
            UIntPtr region = mixinThread.segregatedFreeList.freeList[sizeClass];
            if (region != UIntPtr.Zero) {
                mixinThread.segregatedFreeList.freeList[sizeClass] =
                    *(UIntPtr *)region;

                // Zero out the free list data structure.  However, if the
                // vtable is at offset zero, then we're going to overwrite it
                // anyway.  (and the optimizer does not see this through the
                // unmanaged/managed conversion)
                if (Magic.OffsetOfVTable != UIntPtr.Zero) {
                    *(UIntPtr *)region = UIntPtr.Zero;
                }

                UIntPtr objAddr = region;

                if((alignment == 8) && (UIntPtr.Size == 4)) {
                    // Since 'objAddr' will be the actual object reference, we
                    // want to misalign it here so that the object payload will
                    // be aligned.  (We know that PostHeader.Size & 8 == 4
                    // because the PreHeader is 4 and the sum of the PreHeader
                    // and PostHeader sizes is a multiple of alignment (8))

                    // Store alignment token at objAddr.  This will be where an
                    // alignment token should go if it is required...
                    Allocator.WriteAlignment(objAddr);
                    // ... (align if necessary) ...
                    objAddr = Util.Align(objAddr, (UIntPtr) alignment);
                    // ... or where the object header will be if alignment was
                    // not necessary.  This code zeroes the object header
                    // regardless and avoids a branch in this fast path.
                    *(UIntPtr *)objAddr = UIntPtr.Zero;
                    // Finally misalign 'objAddr'
                    objAddr += PreHeader.Size;
                }

                Object obj = Magic.fromAddress(objAddr);
                obj.vtable = vtable;
                return obj;
            }

            return GC.AllocateObjectNoInline(vtable, currentThread);
        }

        [Inline]
        [ManualRefCounts]
        private UIntPtr AllocateFast(UIntPtr bytes, uint alignment)
        {
            // Room to ensure alignment
            bool alignRequired = (alignment > UIntPtr.Size);
            if (alignRequired) {
                bytes = bytes + alignment - UIntPtr.Size;
            }
            // Is this a large object?
            if (!(bytes < LARGE_OBJECT_THRESHOLD)) {
                return UIntPtr.Zero;
            }
            int sizeClass = GetSizeClass(bytes);
            UIntPtr region = AllocateSmallFast(sizeClass);
            if (region == UIntPtr.Zero) {
                return UIntPtr.Zero;
            } else {
                UIntPtr resultAddr =
                    Allocator.AlignedObjectPtr(region, alignment);
                return resultAddr;
            }
        }

        [Inline]
        [ManualRefCounts]
        internal static UIntPtr AllocateSlow(Thread thread,
                                             UIntPtr bytes, uint alignment)
        {
            SegregatedFreeListThread mixinThread = MixinThread(thread);
            return mixinThread.segregatedFreeList.AllocateSlow(bytes,
                                                               alignment,
                                                               thread);
        }

        [NoInline]
        [ManualRefCounts]
        private UIntPtr AllocateSlow(UIntPtr bytes, uint alignment,
                                      Thread currentThread)
        {
            // Room to ensure alignment
            bool alignRequired = (alignment > UIntPtr.Size);

            if (alignRequired) {
                bytes = bytes + alignment - UIntPtr.Size;
            }

            // Is this a large object?
            if (!(bytes < LARGE_OBJECT_THRESHOLD)) {
                return AllocateLarge(alignment, bytes, currentThread);
            }

            int sizeClass = GetSizeClass(bytes);
            UIntPtr region = AllocateSmall(sizeClass, currentThread);
            UIntPtr resultAddr =
                Allocator.AlignedObjectPtr(region, alignment);
            return resultAddr;
        }

        /// <summary>
        /// Used to attempt to spread large objects across pages to avoid
        /// higher cache conflicts on low page addresses.
        /// </summary>
        private static int largeOffset;

        /// <summary>
        /// Allocate an object of a specified size class from the
        /// thread's local block.
        /// </summary>
        [Inline]
        [ManualRefCounts]
        private UIntPtr AllocateSmall(int sizeClass, Thread currentThread) {
            UIntPtr region = freeList[sizeClass];
            if (region != UIntPtr.Zero) {
                freeList[sizeClass] = *(UIntPtr*)region;
                *(UIntPtr*)region = UIntPtr.Zero;
                return region;
            } else {
                return AllocateSmallSlow(sizeClass, currentThread);
            }
        }

        [Inline]
        private UIntPtr AllocateSmallFast(int sizeClass) {
            UIntPtr region = freeList[sizeClass];
            if (region != UIntPtr.Zero) {
                freeList[sizeClass] = *(UIntPtr*)region;
                *(UIntPtr*)region = UIntPtr.Zero;
                return region;
            } else {
                return UIntPtr.Zero;
            }
        }

        /// <summary>
        /// Get a new page and allocate into it.
        /// </summary>
        /// <param name="sizeClass"></param>
        /// <returns></returns>
        [ManualRefCounts]
        private UIntPtr AllocateSmallSlow(int sizeClass, Thread currentThread)
        {
            // Return our old page
            if (localPages[sizeClass] != null) {
                ReleaseLocalPage(sizeClass);
            }

            // Get a new one.
            localPages[sizeClass] = GetLocalPage(sizeClass, currentThread);
            VTable.Assert(localPages[sizeClass] != null, "no local page");

            // Read (and then zero) the free list.
            freeList[sizeClass] = Interlocked.Exchange
                (ref localPages[sizeClass]->freeList, UIntPtr.Zero);

            VTable.Assert(freeList[sizeClass] != UIntPtr.Zero,
                          "GetLocalPage returned empty page");

            // Allocate off the free list
            return AllocateSmallFast(sizeClass);
        }

        /// <summary>
        /// Release a local allocation page into the pool of consumed pages.
        /// </summary>
        [ManualRefCounts]
        private void ReleaseLocalPage(int sizeClass) {
            PageHeader *page = localPages[sizeClass];

            // Prepare the page to be released
            if (freeList[sizeClass] != UIntPtr.Zero) {
                // We are releasing the page 'early'.
                UIntPtr pageFreeList = freeList[sizeClass];
                freeList[sizeClass] = UIntPtr.Zero;

                // Save our local free list as the page's free list
                UIntPtr oldFreeList = Interlocked.CompareExchange
                    (ref page->freeList, pageFreeList, UIntPtr.Zero);

                // Count the reclaimed cells.
                int freeCount = 0;
                UIntPtr next = pageFreeList;
                while (next != UIntPtr.Zero) {
                    next = *(UIntPtr*)next;
                    freeCount++;
                }

                SubSmallBytes(new UIntPtr((uint)freeCount *
                    (uint)page->cellSize));

                if (oldFreeList != UIntPtr.Zero) {
                    // Page already had a free list, follow it to the end.
                    while(*(UIntPtr*)oldFreeList != UIntPtr.Zero) {
                        oldFreeList = *(UIntPtr*)oldFreeList;
                    }

                    // And stitch the free lists together
                    *(UIntPtr*)oldFreeList = pageFreeList;
                }

                // Set the reclaimed cells.
                int old;
                do {
                    old = page->freeCount;
                } while (old != Interlocked.CompareExchange
                    (ref page->freeCount, old + freeCount, old));
            }

            // Atomically insert the page in the globalPages queue.
            AtomicPush(ref globalPages[sizeClass], page);
        }

        /// <summary>
        /// Either reuse an existing or make a new page to allocate into.
        /// </summary>
        [ManualRefCounts]
        private PageHeader * GetLocalPage(int sizeClass, Thread currentThread)
        {
            GC.CheckForNeededGCWork(currentThread);
            UIntPtr page = AtomicPop(ref globalFreePages[sizeClass]);
            if (page != UIntPtr.Zero) {
                // We got the page
                PageHeader *pageHeader = (PageHeader*) page;

                VTable.Assert(pageHeader->freeCount > 0, "empty FreePage");

                AddSmallBytes(new UIntPtr((uint)pageHeader->freeCount *
                    (uint)pageHeader->cellSize));

                pageHeader->freeCount = 0;

                return pageHeader;
            }

            // Create a new local page.
            return NewLocalPage(sizeClass, currentThread);
        }

        /// <summary>
        /// Create a new page to allocate into
        /// </summary>
        [ManualRefCounts]
        private PageHeader * NewLocalPage(int sizeClass, Thread currentThread)
        {
            VTable.Assert(sizeClass > 0, "non-positive sizeClass");
            bool fCleanPages = true;
            UIntPtr page = PageManager.EnsurePages(currentThread,
                                                   (UIntPtr) 1, INIT_PAGE,
                                                   ref fCleanPages);
            AddSmallPage();
            UIntPtr pageAddr = PageTable.PageAddr(page);
            PageTable.SetExtra(page, (short) sizeClass);
            PageHeader *pageHeader = (PageHeader *) pageAddr;

            // Set up the free list of free slots
            UIntPtr stride = GetCellSize(sizeClass);
            VTable.Assert(stride != UIntPtr.Zero, "Zero Stride");
            pageHeader->cellSize = stride;
            UIntPtr cursor =
                pageAddr + PageHeader.SIZEOF + PreHeader.Size;

            pageHeader->freeList = cursor;
            UIntPtr limit =
                PageTable.PageAddr(page+1) - stride + PreHeader.Size;
            UIntPtr nextAddr = cursor + stride;
            while (nextAddr <= limit) {
                *(UIntPtr*)cursor = nextAddr;
                cursor = nextAddr;
                nextAddr = cursor + stride;
            }

            PageTable.SetType(page, SMALL_OBJ_PAGE);
            return pageHeader;
        }

        #endregion

    }

}
