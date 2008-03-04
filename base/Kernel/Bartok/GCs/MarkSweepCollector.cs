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

    using Microsoft.Bartok.Runtime;
    using System.Runtime.CompilerServices;
    using System.Threading;

#if SINGULARITY
    using Microsoft.Singularity;
#if SINGULARITY_PROCESS
    using Microsoft.Singularity.V1.Services;
    using Microsoft.Singularity.V1.Threads;
#else
    using Microsoft.Singularity.Memory;
#endif
#endif

    [NoCCtor]
    [RequiredByBartok]
    internal class MarkSweepCollector: StopTheWorldCollector
    {
        private static UIntPtr collectionTrigger;

        internal static MarkSweepCollector instance;

        // Visitor instances used for marking objects
        private static MarkReferenceVisitor markReferenceVisitor;
        private static UpdateReferenceVisitor updateReferenceVisitor;
        private static ThreadMarkReferenceVisitor threadMarkReferenceVisitor;
        private static SweepVisitor sweepVisitor;

        private static GCProfiler gcProfiler;
        private static ProfileRootsDelegate profileRoots;
        private static ProfileObjectsDelegate profileObjects;
        private static bool heapDamaged;

        private MarkSweepCollector() {
        }

        public static new void Initialize() {
            GC.Initialize();
            StopTheWorldCollector.Initialize();
            SegregatedFreeList.Initialize();
            // instance = new MarkSweepCollector();
            MarkSweepCollector.instance = (MarkSweepCollector)
                BootstrapMemory.Allocate(typeof(MarkSweepCollector));
            // markReferenceVisitor = new MarkReferenceVisitor();
            markReferenceVisitor = (MarkReferenceVisitor)
                BootstrapMemory.Allocate(typeof(MarkReferenceVisitor));
            // updateReferenceVisitor = new UpdateReferenceVisitor();
            updateReferenceVisitor = (UpdateReferenceVisitor)
                BootstrapMemory.Allocate(typeof(UpdateReferenceVisitor));
            // threadMarkReferenceVisitor = new ThreadMarkReferenceVisitor();
            threadMarkReferenceVisitor = (ThreadMarkReferenceVisitor)
                BootstrapMemory.Allocate(typeof(ThreadMarkReferenceVisitor));
            // sweepVisitor = new SweepVisitor();
            sweepVisitor = (SweepVisitor)
                BootstrapMemory.Allocate(typeof(SweepVisitor));
            collectionTrigger = (UIntPtr) (1 << 24);
        }

        // GCInterface methods
        internal override bool IsOnTheFlyCollector {
            get {
                return false;
            }
        }

        internal override void SetProfiler(GCProfiler profiler)
        {
            if (gcProfiler != null) {
                throw new InvalidOperationException("Only one GCProfiler can be active in a process");
            }
            profileRoots = new ProfileRootsDelegate(ProfileScanRoots);
            profileObjects = new ProfileObjectsDelegate(ProfileScanObjects);
            gcProfiler = profiler;
        }

        internal override void CollectStopped(int currentThreadIndex,
                                              int generation)
        {
#if SINGULARITY
            UIntPtr preGcTotalBytes = SegregatedFreeList.TotalBytes;
#if DEBUG
            DebugStub.WriteLine("~~~~~ Start MarkSweep Cleanup  [data={0:x8}, pid={1:x3}]",
                                __arglist(SegregatedFreeList.TotalBytes,
                                          PageTable.processTag >> 16));
#endif
#if SINGULARITY_KERNEL
    #if THREAD_TIME_ACCOUNTING
            TimeSpan ticks = Thread.CurrentThread.ExecutionTime;
            TimeSpan ticks2 = SystemClock.KernelUpTime;
    #else
            TimeSpan ticks = SystemClock.KernelUpTime;
    #endif
#elif SINGULARITY_PROCESS
    #if THREAD_TIME_ACCOUNTING
            TimeSpan ticks = ProcessService.GetThreadTime();
            TimeSpan ticks2 = ProcessService.GetUpTime();
    #else
            TimeSpan ticks = ProcessService.GetUpTime();
    #endif
#endif
#endif
            if (GC.IsProfiling) {
                gcProfiler.NotifyPreGC(MinGeneration);
                // non-generational collector, so pretend Gen0
                // Calls like ResurrectCandidates below can cause
                // allocations and thus, potentially, profiler
                // notifications.  However, at that time the heap is
                // damaged in the sense that VPtrs have bits OR-ed in
                // for object marking.  We do not want to accept
                // profiling during this window.
                //
                // There is no synchronization issue with setting this
                // flag because it will only be consulted by the
                // thread that sets and resets it.
                heapDamaged = true;
            }
            // 1) Mark the live objects
            CollectorStatistics.Event(GCEvent.TraceStart);
#if !VC
            TryAllManager.PreGCHookTryAll();
#endif
            MultiUseWord.PreGCHook(false /* don't use shadows */);
            Finalizer.PrepareCollectFinalizers();
            int countThreads =
                CallStack.ScanStacks(threadMarkReferenceVisitor,
                                     threadMarkReferenceVisitor);
            Thread.VisitBootstrapData(markReferenceVisitor);
#if SINGULARITY_KERNEL
            Kernel.VisitSpecialData(markReferenceVisitor);
#endif
            MultiUseWord.VisitStrongRefs(markReferenceVisitor,
                                         false /* Don't use shadows */);
#if !VC
            TryAllManager.VisitStrongRefs(markReferenceVisitor);
#endif
            StaticData.ScanStaticData(markReferenceVisitor);
            CollectorStatistics.Event(GCEvent.TraceSpecial);
            WeakReference.Process(updateReferenceVisitor, true, true);
            Finalizer.ResurrectCandidates(updateReferenceVisitor,
                                          markReferenceVisitor, true);
            markReferenceVisitor.Cleanup();
            UnmanagedPageList.ReleaseStandbyPages();
            // 2) Sweep the garbage objects
            CollectorStatistics.Event(GCEvent.SweepStart, TotalMemory);
            WeakReference.Process(updateReferenceVisitor, true, false);
            MultiUseWord.VisitWeakRefs(updateReferenceVisitor,
                                       false /* Don't use shadows */);
#if !VC
            TryAllManager.VisitWeakRefs(updateReferenceVisitor);
#endif
            SegregatedFreeList.VisitAllObjects(sweepVisitor);
            SegregatedFreeList.RecycleGlobalPages();
            SegregatedFreeList.CommitFreedData();
            CollectorStatistics.Event(GCEvent.SweepSpecial);
            MultiUseWord.PostGCHook();
            if (GC.IsProfiling) {
                heapDamaged = false;
                // Allocations may occur inside the PostGCHook.  Hopefully a
                // sufficiently limited quantity that we don't recursively
                // trigger a GC.
                gcProfiler.NotifyPostGC(profileRoots, profileObjects);
            }
            Finalizer.ReleaseCollectFinalizers();
#if !VC
            TryAllManager.PostGCHookTryAll();
#endif
            CollectorStatistics.Event(GCEvent.CollectionComplete,
                                      TotalMemory);
            // 3) Determine a new collection trigger
            UIntPtr testTrigger = (UIntPtr) this.TotalMemory >> 2;
            UIntPtr minTrigger = (UIntPtr) (1 << 24);
            UIntPtr maxTrigger = (UIntPtr) (1 << 26);
            collectionTrigger =
                (testTrigger > minTrigger) ?
                (testTrigger < maxTrigger ?
                 testTrigger : maxTrigger) : minTrigger;
#if SINGULARITY
#if SINGULARITY_KERNEL
    #if THREAD_TIME_ACCOUNTING
            int procId = Thread.CurrentProcess.ProcessId;
            ticks = Thread.CurrentThread.ExecutionTime - ticks;
            ticks2 = SystemClock.KernelUpTime - ticks2;
    #else
            ticks = SystemClock.KernelUpTime - ticks;
    #endif
            //Thread.CurrentProcess.SetGcPerformanceCounters(ticks, (long) SegregatedFreeList.TotalBytes);
#elif SINGULARITY_PROCESS
    #if THREAD_TIME_ACCOUNTING
            ushort procId = ProcessService.GetCurrentProcessId();
            ticks = ProcessService.GetThreadTime()  - ticks;
            ticks2 = ProcessService.GetUpTime() - ticks2;
    #else
            ticks = ProcessService.GetUpTime() - ticks;
    #endif
            //ProcessService.SetGcPerformanceCounters(ticks, (long) SegregatedFreeList.TotalBytes);
#endif
#if DEBUG
#if THREAD_TIME_ACCOUNTING
            DebugStub.WriteLine("~~~~~ Finish MarkSweep Cleanup [data={0:x8}, diff={7:x8} pid={1:x3}, ms(Thread)={2:d6}, ms(System)={3:d6}, thds={4}, procId={5}, tid={6}]",
                                __arglist(SegregatedFreeList.TotalBytes,
                                          PageTable.processTag >> 16,
                                          ticks.Milliseconds,
                                          ticks2.Milliseconds,
                                          countThreads,
                                          procId,
                                          Thread.GetCurrentThreadIndex(),
                                          preGcTotalBytes - SegregatedFreeList.TotalBytes
                                          ));
#else
            DebugStub.WriteLine("~~~~~ Finish MarkSweep Cleanup [data={0:x8}, pid={1:x3}, ms={2:d6}, thds={3}]",
                __arglist(SegregatedFreeList.TotalBytes,
                PageTable.processTag >> 16,
                ticks.Milliseconds,
                countThreads));
#endif
#endif
#endif
        }

        internal override int CollectionGeneration(int genRequest) {
            return MinGeneration;
        }

        // A profiler can request a scan of all Roots, passing in a
        // visitor for callback.
        private void ProfileScanRoots(NonNullReferenceVisitor visitor)
        {
            CallStack.ScanStacks(visitor, visitor);
            Thread.VisitBootstrapData(visitor);
#if SINGULARITY_KERNEL
            Kernel.VisitSpecialData(visitor);
#endif
            MultiUseWord.VisitStrongRefs(visitor,
                                         false /* Don't use shadows */);
            StaticData.ScanStaticData(visitor);
        }

        // A profiler can request a scan of all Objects in the heap,
        // passing in a visitor for callback.
        private
        void ProfileScanObjects(SegregatedFreeList.ObjectVisitor visitor)
        {
            SegregatedFreeList.VisitAllObjects(visitor);
        }

        internal override void ProfileAllocation(Object obj)
        {
            if (GC.IsProfiling && !heapDamaged) {
                UIntPtr size = ObjectLayout.Sizeof(obj);
                gcProfiler.NotifyAllocation(Magic.addressOf(obj),
                                            obj.GetType(), size);
            }
        }

        [Inline]
        internal override UIntPtr AllocateObjectMemory(UIntPtr numBytes,
                                                       uint alignment,
                                                       Thread currentThread)
        {
            UIntPtr resultAddr =
                SegregatedFreeList.AllocateFast(currentThread,
                                                numBytes, alignment);
            if (resultAddr == UIntPtr.Zero) {
                resultAddr = this.AllocateObjectMemorySlow(numBytes, alignment,
                                                           currentThread);
            }
            return resultAddr;
        }

        [NoInline]
        private UIntPtr AllocateObjectMemorySlow(UIntPtr numBytes,
                                                 uint alignment,
                                                 Thread currentThread)
        {
            if (GC.newBytesSinceGC > collectionTrigger &&
                !GC.allocationInhibitGC) {
                //REVIEW: This actually happens after the trigger...
                GC.InvokeCollection(currentThread);
            }
            return SegregatedFreeList.AllocateSlow(currentThread,
                                                   numBytes, alignment);
        }

        internal override int GetGeneration(Object obj) {
            return MinGeneration;
        }

        internal override int MaxGeneration {
            get { return (int)PageType.Owner0; }
        }

        internal override int MinGeneration {
            get { return (int)PageType.Owner0; }
        }

        internal override long TotalMemory {
            get {
                return (long)SegregatedFreeList.TotalBytes;
#if false
                UIntPtr pageCount = UIntPtr.Zero;
                for (UIntPtr i=UIntPtr.Zero; i<PageTable.pageTableCount; i++) {
                    if (PageTable.IsGcPage(i) && PageTable.IsMyPage(i)) {
                        pageCount++;
                    }
                }
                return (long) PageTable.RegionSize(pageCount);
#endif
            }
        }

        internal override void EnableHeap() {
            // Do nothing
        }

        internal override void DestructHeap() {
            if (GC.IsProfiling) {
                gcProfiler.NotifyShutdown();
            }
        }

        internal override void VerifyHeap(bool beforeCollection) {
            Verifier.segregatedFreeListVerifier.VerifyHeap();
        }

        internal override UIntPtr FindObjectAddr(UIntPtr interiorPtr) {
            return SegregatedFreeList.Find(interiorPtr);
        }

        internal override
        void VisitObjects(ObjectLayout.ObjectVisitor objectVisitor,
                          UIntPtr lowAddr, UIntPtr highAddr)
        {
            VTable.Assert(PageTable.PageAligned(lowAddr));
            VTable.Assert(PageTable.PageAligned(highAddr));
            UIntPtr lowPage = PageTable.Page(lowAddr);
            UIntPtr highPage = PageTable.Page(highAddr);
            SegregatedFreeList.VisitObjects(lowPage, highPage, objectVisitor);
        }

        internal override void NewThreadNotification(Thread newThread,
                                                     bool initial)
        {
            base.NewThreadNotification(newThread, initial);
            SegregatedFreeList.NewThreadNotification(newThread, initial);
        }

        internal override void DeadThreadNotification(Thread deadThread)
        {
            SegregatedFreeList.DeadThreadNotification(deadThread);
            base.DeadThreadNotification(deadThread);
        }

        // Routines for updating pointers to new locations of marked objects
        // No objects are resurrected using this mechanism
        // As this is mark sweep the value does not need to be updated.
        private class UpdateReferenceVisitor: NonNullReferenceVisitor
        {

            internal unsafe override void Visit(UIntPtr *loc) {
                UIntPtr addr = *loc;
                // Ignore pointers out of our memory area
                if (PageTable.IsForeignAddr(addr)) {
                    return;
                }
                UIntPtr page = PageTable.Page(addr);
                PageType pageType = PageTable.Type(page);
                if (!PageTable.IsGcPage(pageType)) {
                    VTable.Assert((PageTable.IsNonGcPage(pageType) &&
                                   PageTable.IsMyPage(page)) ||
                                  PageTable.IsStackPage(pageType));
                    return;
                }
                VTable.Assert(PageTable.IsMyPage(page));
                Object obj = Magic.fromAddress(addr);
                VTable.Assert(obj.vtable != null, "Null VTable");
                if (obj.GcMark() == UIntPtr.Zero) {
                    // The object was not live
                    *loc = UIntPtr.Zero;
                }
            }
        }

        private class MarkReferenceVisitor : NonNullReferenceVisitor
        {

            private static UIntPtrStack workList;
            private static bool fMarkInProgress;

            internal unsafe override void Visit(UIntPtr *loc) {
                UIntPtr addr = *loc;
                // Ignore pointers out of our memory area
                if (PageTable.IsForeignAddr(addr)) {
                    return;
                }
                UIntPtr page = PageTable.Page(addr);
                PageType pageType = PageTable.Type(page);
                if (!PageTable.IsGcPage(pageType)) {
                    VTable.Assert((PageTable.IsNonGcPage(pageType) &&
                                   PageTable.IsMyPage(page)) ||
                                  PageTable.IsStackPage(pageType));
                    return;
                }
                VTable.Assert(PageTable.IsMyPage(page));
                Object obj = Magic.fromAddress(addr);
                VTable.Assert(obj.vtable != null);
                if (obj.GcMark((UIntPtr)1)) {
                    // We changed the color of the object, so we
                    // have to mark the objects reachable from the fields
                    MarkVisit(obj);
                }
            }

            internal void MarkVisit(Object obj) {
                if (fMarkInProgress) {
                    workList.Write(Magic.addressOf(obj));
                } else {
                    fMarkInProgress = true;
                    this.VisitReferenceFields(obj);
                    this.ProcessScheduledObjects();
                    fMarkInProgress = false;
                }
            }

            private void ProcessScheduledObjects() {
                while (!workList.IsEmpty) {
                    UIntPtr addr = workList.Read();
                    Object obj = Magic.fromAddress(addr);
                    this.VisitReferenceFields(obj);
                }
            }

            internal void Cleanup() {
                VTable.Assert(workList.IsEmpty);
                workList.Cleanup(true);
            }

            [Inline]
            internal override UIntPtr VisitReferenceFields(Object obj)
            {
                UIntPtr vtableAddr = Magic.addressOf(obj.vtable)&~(UIntPtr)3U;
                VTable vtable = Magic.toVTable(Magic.fromAddress(vtableAddr));
                return this.VisitReferenceFields(Magic.addressOf(obj),
                                                 vtable);
            }

        }

        private class ThreadMarkReferenceVisitor : NonNullReferenceVisitor
        {

            internal unsafe override void Visit(UIntPtr *loc) {
                UIntPtr addr = *loc;
                // Ignore pointers out of our memory area
                if (PageTable.IsForeignAddr(addr)) {
                    return;
                }
                UIntPtr page = PageTable.Page(addr);
                PageType pageType = PageTable.Type(page);
                if (!PageTable.IsGcPage(pageType)) {
                    VTable.Assert((PageTable.IsNonGcPage(pageType) &&
                                   PageTable.IsMyPage(page))||
                                  PageTable.IsStackPage(pageType) ||
                                  PageTable.IsSharedPage(pageType));
                    return;
                }
                UIntPtr objectAddr = SegregatedFreeList.Find(addr);
                markReferenceVisitor.Visit(&objectAddr);
            }

        }

        private class SweepVisitor : SegregatedFreeList.ObjectVisitor
        {

            private SegregatedFreeList.TempList tempList;

            internal override void VisitSmall(Object obj, UIntPtr memAddr)
            {
                if (!obj.GcMark(UIntPtr.Zero)) {
                    // We did not change the color of the object back
                    // to unmarked, so we are responsible for freeing it.
                    tempList.Add(memAddr);
                }
            }

            internal override void VisitSmallPageEnd() {
                SegregatedFreeList.FreeSmallList(ref tempList);
            }

            internal override UIntPtr VisitLarge(Object obj)
            {
                UIntPtr objectSize =
                    ObjectLayout.ObjectSize(Magic.addressOf(obj),
                                            obj.GcUnmarkedVTable);
                if (!obj.GcMark(UIntPtr.Zero)) {
                    // We did not change the color of the object back
                    // to unmarked, so we are responsible for freeing it.
                    SegregatedFreeList.FreeLarge(obj);
                }
                // REVIEW: Should we return a real size here?
                return objectSize;
            }

        }

        private class VerifyVisitor : ObjectLayout.ObjectVisitor {

            internal static VerifyVisitor visitor = new VerifyVisitor();

            internal override UIntPtr Visit(Object obj) {
                UIntPtr size;
                if (obj.GcMark() != UIntPtr.Zero) {
                    // The object has the mark color, so it should only
                    // reference other objects with the mark color.
                    size = VerifyMarkVisitor.visitor.VisitReferenceFields(obj);
                } else {
                    size = ObjectLayout.Sizeof(obj);
                }
                return size;
            }

        }

        private class VerifyMarkVisitor : NonNullReferenceVisitor {

            internal static VerifyMarkVisitor visitor =new VerifyMarkVisitor();

            internal unsafe override void Visit(UIntPtr *loc) {
                UIntPtr addr = *loc;
                // Ignore pointers out of our memory area
                if (PageTable.IsForeignAddr(addr)) {
                    return;
                }
                UIntPtr page = PageTable.Page(addr);
                PageType pageType = PageTable.Type(page);
                if (PageTable.IsGcPage(pageType)) {
                    Object obj = Magic.fromAddress(addr);
                    VTable.Assert(obj.GcMark() != UIntPtr.Zero);
                    VTable.Assert(PageTable.IsMyPage(page));
                }
            }

        }

    }

}
