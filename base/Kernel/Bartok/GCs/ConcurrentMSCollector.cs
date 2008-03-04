/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

#define OVERLAPPING_PHASES

namespace System.GCs {

    using Microsoft.Bartok.Runtime;
    using System.Runtime.CompilerServices;
    using System.Threading;

#if SINGULARITY
    using Microsoft.Singularity;
#if SINGULARITY_PROCESS
    using Microsoft.Singularity.V1.Services; // Used for timing, only
#elif SINGULARITY_KERNEL
    using Microsoft.Singularity.Scheduling;
    using Microsoft.Singularity.X86;
#endif
#endif

    /// <summary>
    /// This class implements a semi-concurrent version of MarkSweep.
    ///
    /// The goal for this collector is to perform as much of the processing
    /// as possible during mutator time, and also have a fixed upper bound
    /// on pause times.
    ///
    /// Additionally, the collector will be required in the future to
    /// communicate with the scheduler to prevent real time applications
    /// from competing with the GC.
    ///
    ///
    /// BUGBUG: NEED TO REPLICATE THE ALLOCATION COLOR LOGIC TO THE INLINED
    ///         ALLOCATION SEQUENCE. [T-DANIF 12/3/2004]
    /// </summary>

    [NoCCtor]
    [RequiredByBartok]
    internal class ConcurrentMSCollector: BaseCollector
    {

        internal enum MarkingPhase {
            Dummy,              // Not used!
            Idle,               // No marking is occurring
            Requested,          // No marking, but will be soon
            ComputingRoots,     // Marking the roots
            Tracing,            // Tracing gray objects
        }

        internal enum ReclamationPhase {
            Dummy,              // Not used!
            Idle,               // No reclamation is in progress
            Reclaiming,         // Reclamation is in progress
        }

        internal static MarkingPhase CurrentMarkingPhase {
            get { return (MarkingPhase) currentMarkingPhase; }
            set { currentMarkingPhase = (int) value; }
        }

        internal static int currentMarkingPhase;

        internal static ReclamationPhase CurrentReclamationPhase;

#if SINGULARITY
        private static TimeSpan infiniteWait;
#else
        private static int infiniteWait;
#endif

        /// <summary>
        /// Current mark state. This is flipped between collections.
        /// </summary>
        internal static UIntPtr markedColor;
        internal static UIntPtr unmarkedColor;
        internal static UIntPtr reclamationColor;

        private const uint markOne = 1U;
        private const uint markTwo = 2U;
        private const uint markThree = 3U;

        /// <summary>
        /// Amount of memory to allocate between collections.
        /// BUGBUG: This heuristic is not very good for CMS.
        /// </summary>
        private static UIntPtr collectionTrigger;

        internal static ConcurrentMSCollector instance;

        // Pointer visitors used for marking, etc
        private static MarkReferenceVisitor markReferenceVisitor;
        private static UpdateReferenceVisitor updateReferenceVisitor;
        private static StackMarkReferenceVisitor stackMarkReferenceVisitor;

        // Object visitors used for sweeping, etc
        private static SweepVisitor sweepVisitor;

        // Which threads are in what marking stage?
        private static UIntPtr[] threadColor;

        // Threads waiting for a particular collection to finish
        private static int[] waitingThreads;
        private static int firstWaitingThread;
        private const int waitingThreadsNullValue = -1;
        private const int waitingThreadsUnusedValue = -2;

        private const uint noColor = 0xffffffff;

        // The threads that performs the collection work
        private static Thread markThread;
        private static Thread workerThread1;
        private static Thread workerThread2;

        private static AutoResetEvent sweepPhaseMutex;

        /// <summary>
        /// Does this collector compute a root set with threads running?
        /// </summary>
        internal override bool IsOnTheFlyCollector {
            get {
                return true;
            }
        }

        /// <summary>
        /// Initialise the collector and allow allocations to commence.
        /// </summary>
        [PreInitRefCounts]
        public static void Initialize() {
            GC.Initialize();
            SegregatedFreeList.Initialize();
            unmarkedColor = (UIntPtr) markOne;
            markedColor = (UIntPtr) markTwo;
            reclamationColor = (UIntPtr) markThree;
            // instance = new ConcurrentMSCollector();
            ConcurrentMSCollector.instance = (ConcurrentMSCollector)
                BootstrapMemory.Allocate(typeof(ConcurrentMSCollector));
            // markReferenceVisitor = new MarkReferenceVisitor();
            markReferenceVisitor = (MarkReferenceVisitor)
                BootstrapMemory.Allocate(typeof(MarkReferenceVisitor));
            // updateReferenceVisitor = new UpdateReferenceVisitor();
            updateReferenceVisitor = (UpdateReferenceVisitor)
                BootstrapMemory.Allocate(typeof(UpdateReferenceVisitor));
            // stackMarkReferenceVisitor = new StackMarkReferenceVisitor();
            stackMarkReferenceVisitor = (StackMarkReferenceVisitor)
                BootstrapMemory.Allocate(typeof(StackMarkReferenceVisitor));
            // sweepVisitor = new SweepVisitor();
            sweepVisitor = (SweepVisitor)
                BootstrapMemory.Allocate(typeof(SweepVisitor));
            // threadColor = new UIntPtr[Thread.maxThreads];
            threadColor = (UIntPtr[])
                BootstrapMemory.Allocate(typeof(UIntPtr[]), Thread.maxThreads);
            for (int i = 0; i < threadColor.Length; i++) {
                threadColor[i] = (UIntPtr) noColor;
            }
            collectionTrigger = (UIntPtr) (1 << 24);
            CurrentMarkingPhase = MarkingPhase.Idle;
            CurrentReclamationPhase = ReclamationPhase.Idle;
#if SINGULARITY
            infiniteWait = TimeSpan.Infinite;
#else
            infiniteWait = Timeout.Infinite;
#endif
        }

        /// <summary>
        /// Perform a collection. Depending on the current phase of collection
        /// this method will either:
        ///
        ///     1. Start a new collection and schedule the mark thread
        ///     2. Notice that a collection is underway and exit
        ///     3. Clean up after a collection
        ///
        /// BUGBUG: The interaction between the collector needs work!
        /// </summary>
        internal override void Collect(int currentThreadIndex,
                                       int generation)
        {
            if (Transitions.HasGCRequest(currentThreadIndex)) {
                // We are GC safe, so we may do this
                Transitions.TakeDormantControlNoGC(currentThreadIndex);
                if (Transitions.TakeGCControl(currentThreadIndex)) {
                    MutatorHandshake(currentThreadIndex);
                    Transitions.ReleaseGCControl(currentThreadIndex);
                    Thread.SignalGCEvent(markThread.threadIndex);
                }
                Transitions.TakeMutatorControlNoGC(currentThreadIndex);
            } else {
                if (generation >= 0) {
                    AddThreadToWaitList(currentThreadIndex);
                }
                AddCollectionRequest();
                if (generation >= 0) {
                    Thread currentThread =
                        Thread.threadTable[currentThreadIndex];
                    while (ThreadIsWaiting(currentThreadIndex)) {
                        currentThread.WaitForEvent(infiniteWait);
                    }
                }
            }
        }

        internal override void CheckForNeededGCWork(Thread currentThread) {
            if (Transitions.HasGCRequest(currentThread.threadIndex)) {
                GC.InvokeCollection(currentThread);
            }
        }

        private static void CollectorHandshake(Thread collectorThread)
        {
            Transitions.MakeGCRequests(collectorThread.threadIndex);
            // Handshake with all the (other) threads.
            for(int i=0; i < Thread.threadTable.Length; i++) {
#if SINGULARITY_KERNEL
                if (Scheduler.IsIdleThread(i)) {
                    continue;
                }
#endif
                // Is there an unscanned thread here?
                while (Transitions.HasGCRequest(i)) {
                    if (Transitions.TakeGCControl(i)) {
                        MutatorHandshake(i);
                        Transitions.ReleaseGCControl(i);
                        Thread.SignalGCEvent(i);
                    } else if (Thread.threadTable[i] == null) {
                        // The thread must have terminated but the
                        // state hasn't yet changed to DormantState.
                        break;
                    } else {
                        Thread.WaitForGCEvent(collectorThread.threadIndex);
                    }
                }
            }

            if (CurrentMarkingPhase == MarkingPhase.ComputingRoots &&
                !WriteBarrierCMS.InSnoopingPhase) {
                MultiUseWord.CollectFromPreviousCollections(false);
            }
        }

        private static void MutatorHandshake(int threadIndex)
        {
            if (CurrentMarkingPhase == MarkingPhase.ComputingRoots &&
                !WriteBarrierCMS.InSnoopingPhase) {
                Thread thread = Thread.threadTable[threadIndex];
                if (thread != null) {
                    ScanThreadRoots(thread);
                    MultiUseWord.CollectFromThread(thread);
                }
            }
        }

        private static void ScanThreadRoots(Thread thread) {
            long start = CollectorStatistics.PerformanceCounter;
            CollectorStatistics.Event(GCEvent.StackScanStart,
                                      thread.threadIndex);
            CallStack.ScanStack(thread, stackMarkReferenceVisitor,
                                stackMarkReferenceVisitor);
            threadColor[thread.threadIndex] = markedColor;
            // Report the pause
            long pause =
                CollectorStatistics.PerformanceCounter - start;
            CollectorStatistics.Event(GCEvent.StackScanComplete,
                                      pause);
        }

        private static bool terminateCollectorThreads;

        private static void TraceThreadNotification() {
            terminateCollectorThreads = true;
            GC.Collect();
        }

        /// <summary>
        /// This method is run by the collector threads.
        /// </summary>
        private static void CollectionLoop() {
            Thread currentThread = Thread.CurrentThread;
#if SINGULARITY_PROCESS
            currentThread.MakeServiceThread(new Thread.StopServiceNotice(TraceThreadNotification));
#endif
            while (!terminateCollectorThreads) {
                // Wait to be told to start working.
                while (!TakeChargeOfTraceRequest()) {
                    currentThread.WaitForEvent(infiniteWait);
                }
#if SINGULARITY
#if DEBUG
                DebugStub.WriteLine("~~~~~ Start Concurrent Marking  [data={0:x8}, pid={1:x3}]",
                                    __arglist(SegregatedFreeList.TotalBytes,
                                              PageTable.processTag >> 16));
#endif
                int startTicks = Environment.TickCount;
#endif
                markThread = currentThread;
                advanceMarkColors();
                // Construct the root set.
                CollectorStatistics.Event(GCEvent.ComputeRootSet);
                StartRootMarkingPhase();
                WriteBarrierCMS.EnterSnoopingPhase();
                // Start the process of recycling pages
                SegregatedFreeList.RecycleGlobalPagesPhase1();
                // One handshake to ensure that everyone starts snooping
                CollectorHandshake(currentThread);
                // Complete the process of recycling pages
                SegregatedFreeList.RecycleGlobalPagesPhase2();
                // Another handshake to ensure that all updates started by
                // other threads prior to their handshake are done and
                // snooping will affect all new updates.
                CollectorHandshake(currentThread);
                WriteBarrierCMS.LeaveSnoopingPhase();
                ScanThreadRoots(currentThread);
                // A third handshake to get the threads to process their
                // own roots.
                CollectorHandshake(currentThread);
                Finalizer.PrepareCollectFinalizers();
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
                CollectorStatistics.Event(GCEvent.RootSetComputed);
                int waitingThreadList = StealWaitingThreadsList();
                // We are now in the concurrent tracing phase.
                ResetCollectorRequests();
                StartTracingPhase();
                CollectorStatistics.Event(GCEvent.TraceStart,
                                          ReservedMemory);
                // Trace live objects from the root set.
                markReferenceVisitor.ProcessScheduledObjects();
                CollectorStatistics.Event(GCEvent.TraceSpecial);
                // Mark weak references that do not track resurrection as dead.
                WeakReference.Process(updateReferenceVisitor, true, true);
                // Resurrect any finalization candidates.
                Finalizer.ResurrectCandidates(updateReferenceVisitor,
                                              markReferenceVisitor, true);
                // Complete closure from finalized objects.
                markReferenceVisitor.ProcessScheduledObjects();
                // Mark appropriate weak references as dead
                WeakReference.Process(updateReferenceVisitor, true, false);
                MultiUseWord.VisitWeakRefs(updateReferenceVisitor,
                                           false /* Don't use shadows */);
#if !VC
                TryAllManager.VisitWeakRefs(updateReferenceVisitor);
#endif
                MultiUseWord.PostGCHook();
                // Reset thread queues.  They should all be empty.
                markReferenceVisitor.Cleanup();
#if SINGULARITY
#if DEBUG
                int middleTicks = Environment.TickCount;
                DebugStub.WriteLine("~~~~~ Finish Concurrent Marking [data={0:x8}, pid={1:x3} ms={2:d6}]",
                                    __arglist(SegregatedFreeList.TotalBytes,
                                              PageTable.processTag >> 16,
                                              middleTicks - startTicks));
#endif
#endif
                    
                markThread = nextWorkerThread(currentThread);
                sweepPhaseMutex.WaitOne();
                try {
                    reclamationColor = unmarkedColor;
                    FinishTracingPhase();
                    SatisfyCollectorRequest(); // May start another trace phase
                    // Sweep garbage objects
                    StartReclamationPhase();
                    CollectorStatistics.Event(GCEvent.SweepStart,
                                              ReservedMemory);
                    Sweep();
                    // Clean up after the collection
                    CollectorStatistics.Event(GCEvent.SweepSpecial,
                                              ReservedMemory);
                    Finalizer.ReleaseCollectFinalizers();
                    CollectorStatistics.Event(GCEvent.SweepPreCommit,
                                              ReservedMemory);
                    // Commit accounting changes
                    CommitSweep();
                    CollectorStatistics.Event(GCEvent.CollectionComplete,
                                              ReservedMemory);
                    // TODO: Determine a new collection trigger?
                    SignalWaitingThreads(waitingThreadList);
                    FinishReclamationPhase();
                } finally {
                    sweepPhaseMutex.Set();
                }
#if SINGULARITY
#if DEBUG
                int endTicks = Environment.TickCount;
                DebugStub.WriteLine("~~~~~ Finish Concurrent Reclamation [data={0:x8}, pid={1:x3} ms={2:d6}]",
                                    __arglist(ReservedMemory,
                                              PageTable.processTag >> 16,
                                              endTicks - middleTicks));
#endif
#endif
            }
        }

        /// <summary>
        /// Allocate memory for a new object, potentially triggering a
        /// collection.
        /// </summary>
        [Inline]
        internal override UIntPtr AllocateObjectMemory(UIntPtr numBytes,
                                                       uint alignment,
                                                       Thread currentThread)
        {
            UIntPtr resultAddr =
                SegregatedFreeList.AllocateFast(currentThread,
                                                numBytes, alignment);
            if (resultAddr == UIntPtr.Zero) {
                resultAddr = AllocateObjectMemorySlow(numBytes, alignment,
                                                      currentThread);
            }
            return resultAddr;
        }

        [NoInline]
        private UIntPtr AllocateObjectMemorySlow(UIntPtr numBytes,
                                                 uint alignment,
                                                 Thread currentThread)
        {
            if (GC.newBytesSinceGC > collectionTrigger) {
                if (CurrentMarkingPhase == MarkingPhase.Idle ||
                    Transitions.HasGCRequest(currentThread.threadIndex)) {
                    GC.InvokeCollection(currentThread);
                } else if (GC.newBytesSinceGC > (collectionTrigger<<1)) {
                    // Slow down the allocating thread a bit
                    Thread.Yield();
                }
            }
            return SegregatedFreeList.AllocateSlow(currentThread,
                                                   numBytes, alignment);
        }

        [Inline]
        protected override void CreateObject(Object obj, VTable vtable,
                                             Thread currentThread)
        {
            // We expect the color to be assigned before the vtable field
            // is initialized.  This ensures that every real object has a
            // valid color.
            UIntPtr markBits = threadColor[currentThread.threadIndex];
            ThreadHeaderQueue.SetGcMark(obj, markBits);
            // The vtable field must be initialized before the object is
            // inserted into a list of objects to be scanned.
            base.CreateObject(obj, vtable, currentThread);
            // If necessary, mark the object for future scanning
            if (CurrentMarkingPhase == MarkingPhase.ComputingRoots &&
                Transitions.HasGCRequest(currentThread.threadIndex)) {
                ThreadHeaderQueue.PushUnsafe(currentThread, obj, markBits);
            }
        }

        /// <summary>
        /// Return the generation for an object. We only have one
        /// generation so we always return generation zero.
        /// </summary>
        internal override int GetGeneration(Object obj) {
            Verifier.genericObjectVisitor.Visit(obj);
            return MinGeneration;
        }

        /// <summary>
        /// The maximum generation. For MarkSweep this is generation zero.
        /// </summary>
        internal override int MaxGeneration {
            get { return (int)PageType.Owner0; }
        }

        /// <summary>
        /// The minimum generation. For MarkSweep this is generation zero.
        /// </summary>
        internal override int MinGeneration {
            get { return (int)PageType.Owner0; }
        }

        /// <summary>
        /// This returns the total amount of memory that is allocated within
        /// the collected heap.
        /// </summary>
        internal override long TotalMemory {
            get {
                return ReservedMemory;
            }
        }

        private static long ReservedMemory {
            get {
                return (long)SegregatedFreeList.TotalBytes;
            }
        }

        internal override void EnableHeap() {
            // waitingThreads = new int[Thread.maxThreads]
            waitingThreads = new int[Thread.maxThreads];
            for (int i = 0; i < waitingThreads.Length; i++) {
                waitingThreads[i] = waitingThreadsUnusedValue;
            }
            firstWaitingThread = -1;
            // Construct the collector thread(s)
#if SINGULARITY_KERNEL
            workerThread1 =
                Thread.CreateThread(Process.kernelProcess,
                                    new ThreadStart(CollectionLoop));
            workerThread2 =
                Thread.CreateThread(Process.kernelProcess,
                                    new ThreadStart(CollectionLoop));
#else
            workerThread1 = new Thread(new ThreadStart(CollectionLoop));
            workerThread2 = new Thread(new ThreadStart(CollectionLoop));
#endif
            workerThread1.Start();
            workerThread2.Start();
            markThread = workerThread1;
            sweepPhaseMutex = new AutoResetEvent(true);
        }

        /// <summary>
        /// Destroy the heap. Nothing to do here.
        /// </summary>
        internal override void DestructHeap() {
            // Do nothing
            while (CurrentMarkingPhase != MarkingPhase.Idle &&
                   CurrentReclamationPhase != ReclamationPhase.Idle) {
                Thread.Yield();
            }
        }

        /// <summary>
        /// Verify the heap.
        /// </summary>
        internal override void VerifyHeap(bool beforeCollection) {
            SegregatedFreeList.VisitAllObjects(VerifyVisitor.visitor);
            Verifier.segregatedFreeListVerifier.VerifyHeap();
        }

        private static char ToHexDigit(int number, int position) {
            int digit = (number >> (position * 4)) & 0xf;
            return (char) (digit + ((digit <= 9) ? '0' : ('A' - 10)));
        }
        private static int flag; // = 0;
        private static void DebugTrace(String text, int threadIndex) {
            while (Interlocked.CompareExchange(ref flag, 1, 0) != 0) { }
            VTable.DebugPrint(text+" "+
                              ToHexDigit(threadIndex, 2)+
                              ToHexDigit(threadIndex, 1)+
                              ToHexDigit(threadIndex, 0)+
                              "\n");
            Interlocked.Exchange(ref flag, 0);
        }

        /// Routines to keep track of requests for collection work

        private static int collectorStack; // 0:idle, 1:work, 2+:work+pending

        private static void AddCollectionRequest() {
            int stackHeight = Interlocked.Increment(ref collectorStack);
            VTable.Assert(stackHeight > 0);
            if (stackHeight == 1) {
                MakeTraceRequest();
            }
        }

        private static void ResetCollectorRequests() {
            Interlocked.Exchange(ref collectorStack, 1);
        }

        private static void SatisfyCollectorRequest() {
            int stackHeight = Interlocked.Decrement(ref collectorStack);
            VTable.Assert(stackHeight >= 0);
            if (stackHeight > 0 ) {
                MakeTraceRequest();
            }
        }

        /// Routines to control the commencement of the tracing phase

        private static void MakeTraceRequest() {
            CurrentMarkingPhase = MarkingPhase.Requested;
            markThread.SignalEvent();
        }

        private static bool TakeChargeOfTraceRequest() {
            return
                (Interlocked.CompareExchange(ref currentMarkingPhase,
                                             (int) MarkingPhase.ComputingRoots,
                                             (int) MarkingPhase.Requested) ==
                 (int) MarkingPhase.Requested);
        }

        /// Routines to keep track of threads that must be notified when
        /// a collection has been completed.

        private static void AddThreadToWaitList(int threadIndex) {
            int listHead = firstWaitingThread;
            waitingThreads[threadIndex] = listHead;
            while (Interlocked.CompareExchange(ref firstWaitingThread,
                                               threadIndex, listHead) !=
                   listHead) {
                listHead = firstWaitingThread;
                waitingThreads[threadIndex] = listHead;
            }
        }

        private static bool ThreadIsWaiting(int threadIndex) {
            return (waitingThreads[threadIndex] != waitingThreadsUnusedValue);
        }

        private static int StealWaitingThreadsList() {
            return Interlocked.Exchange(ref firstWaitingThread,
                                        waitingThreadsNullValue);
        }

        private static void SignalWaitingThreads(int listHead) {
            while (listHead != waitingThreadsNullValue) {
                int threadIndex = listHead;
                listHead = waitingThreads[threadIndex];
                waitingThreads[threadIndex] = waitingThreadsUnusedValue;
                Thread.threadTable[threadIndex].SignalEvent();
            }
        }

        // Routines to keep track of what phases the collector threads are in.

        private static void StartRootMarkingPhase() {
            CurrentMarkingPhase = MarkingPhase.ComputingRoots;
        }

        private static void StartTracingPhase() {
            CurrentMarkingPhase = MarkingPhase.Tracing;
        }

        private static void FinishTracingPhase() {
            CurrentMarkingPhase = MarkingPhase.Idle;
        }

        private static void StartReclamationPhase() {
            CurrentReclamationPhase = ReclamationPhase.Reclaiming;
        }

        private static void FinishReclamationPhase() {
            CurrentReclamationPhase = ReclamationPhase.Idle;
        }

        // Routines to manage marking colors

        private static void advanceMarkColors()
        {
            unmarkedColor = markedColor;
            markedColor = nextColor(markedColor);
        }

        private static UIntPtr nextColor(UIntPtr originalColor)
        {
            switch ((uint) originalColor) {
              case markOne: return (UIntPtr) markTwo;
              case markTwo: return (UIntPtr) markThree;
              case markThree: return (UIntPtr) markOne;
              default: throw new Exception("advanceColor failure!");
            }
        }

        private static Thread nextWorkerThread(Thread currentThread) {
#if OVERLAPPING_PHASES
            return ((currentThread == workerThread1) ?
                    workerThread2 :
                    workerThread1);
#else
            return currentThread;
#endif
        }

        /// <summary>
        /// Walk the allocation structures and reclaim any free cells.
        /// </summary>
        private static void Sweep() {
            SegregatedFreeList.VisitAllObjects(sweepVisitor);
        }

        /// <summary>
        /// Update alloc heap to account for data just freed.
        /// </summary>
        private static void CommitSweep() {
            SegregatedFreeList.CommitFreedData();
        }

        /// <summary>
        /// Routines for updating pointers to new locations of marked objects
        /// No objects are resurrected using this mechanism.
        /// As this is mark sweep the value does not need to be updated.
        /// </summary>
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
                                  PageTable.IsStackPage(pageType),
                                  "update.visit invalid page");
                    return;
                }
                VTable.Assert(PageTable.IsMyPage(page));
                Object obj = Magic.fromAddress(addr);
                if (ThreadHeaderQueue.GcMark(obj) == unmarkedColor) {
                    // The object was not live
                    *loc = UIntPtr.Zero;
                }
                VTable.Assert(obj.vtable != null, "update.visit null vtable");
            }
        }

        /// <summary>
        /// Select the generation to collect (always generation 0)
        /// </summary>
        internal override int CollectionGeneration(int genRequest) {
            return MinGeneration;
        }


        /// <summary>
        /// This visitor is the core of the tracing functionality.
        /// It builds up a buffer of references (the root set), and
        /// then at a later point the tracing thread processes these
        /// buffers.
        /// </summary>
        private class MarkReferenceVisitor : NonNullReferenceVisitor
        {

            /// <summary>
            /// Visit an object reference.
            /// </summary>
            internal unsafe override void Visit(UIntPtr *loc) {
                UIntPtr addr = *loc;
                if (addr == UIntPtr.Zero) return;
                VisitValue(addr);
            }


            public void VisitValue(UIntPtr addr) {
                // Ignore pointers out of our memory area
                if (PageTable.IsForeignAddr(addr)) {
                    return;
                }
                UIntPtr page = PageTable.Page(addr);
                PageType pageType = PageTable.Type(page);
                if (!PageTable.IsGcPage(pageType)) {
                    VTable.Assert((PageTable.IsNonGcPage(pageType) &&
                                   PageTable.IsMyPage(page)) ||
                                  PageTable.IsStackPage(pageType),
                                  "value.visit invalid page");
                    return;
                }
                VTable.Assert(PageTable.IsMyPage(page));
                Object obj = Magic.fromAddress(addr);
                VTable.Assert(obj.vtable != null,
                              "value.visit null vtable");
                WriteBarrierCMS.MarkObject(obj);
            }

            /// <summary>
            /// Process all marked objects from queues stored in
            /// thread objects.
            /// </summary>
            public void ProcessScheduledObjects() {
                Thread currentThread = Thread.CurrentThread;
                StealWork();
                do {
                    while (!ThreadHeaderQueue.IsEmpty(currentThread)) {
                        // Pop the next value
                        Object obj =
                            ThreadHeaderQueue.Pop(currentThread, markedColor);
                        // Visit Fields
                        this.VisitReferenceFields(obj);
                    }
                } while (StealWork());
            }

            /// <summary>
            /// Look through other threads and see if any have some values on
            /// their queues that we can steal.
            /// </summary>
            private bool StealWork() {
                Thread[] threadTable = Thread.threadTable;
                Thread me = Thread.CurrentThread;
                bool foundWork = false;
                // Attempt to steal work from live threads
                for (int i = 0; i < threadTable.Length; i++) {
                    Thread t = threadTable[i];
                    if (t != null && t != me &&
                        ThreadHeaderQueue.Steal(me, t, markedColor)) {
                        foundWork = true;
                    }
                }
                return foundWork;
            }

            /// <summary>
            /// Clean up after processing all queues. This involves calling
            /// reset on each thread's queue.
            /// </summary>
            internal void Cleanup() {
                Thread[] threadTable = Thread.threadTable;
                for (int i = 0; i < threadTable.Length; i++) {
                    Thread t = threadTable[i];
                    if (t != null) {
                        if (!ThreadHeaderQueue.IsEmpty(t)) {
                            // The queues may contain new objects allocated
                            // during the root-set computation phase but added
                            // to the queue after the scanning has completed.
                            // This is benign and we can clear the queues.
                            do {
                                Object head =
                                    ThreadHeaderQueue.Pop(t, markedColor);
                                VTable.Assert(ThreadHeaderQueue.GcMark(head) ==
                                              markedColor);
                            } while (!ThreadHeaderQueue.IsEmpty(t));
                        }
                        ThreadHeaderQueue.Reset(t);
                    }
                }
            }

        }

        /// <summary>
        /// This class maps an interior pointer back to the containing object
        /// pointer and then passes it on to the object marking visitor.
        /// </summary>
        private class StackMarkReferenceVisitor : NonNullReferenceVisitor
        {

            /// <summary>
            /// Visit an interior pointer stored in loc.
            /// </summary>
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
                                  PageTable.IsStackPage(pageType) ||
                                  PageTable.IsSharedPage(pageType),
                                  "interior.visit invalid page");
                    return;
                }
                UIntPtr realPtr = SegregatedFreeList.Find(addr);
                VTable.Assert(Magic.fromAddress(realPtr).vtable != null,
                              "interior.visit null vtable");
                markReferenceVisitor.VisitValue(realPtr);
            }

        }


        /// <summary>
        /// This class is used to visit every small object, determine if it
        /// is marked and free it if not.
        /// </summary>
        private class SweepVisitor : SegregatedFreeList.ObjectVisitor
        {

            private SegregatedFreeList.TempList tempList;

            internal override void VisitSmall(Object obj, UIntPtr memAddr)
            {
                if (ThreadHeaderQueue.GcMark(obj) == reclamationColor) {
                    // Not marked.
                    tempList.Add(memAddr);
                }
            }

            internal override void VisitSmallPageEnd() {
                SegregatedFreeList.FreeSmallList(ref tempList);
            }

            internal override UIntPtr VisitLarge(Object obj)
            {
                UIntPtr objectSize = ObjectLayout.Sizeof(obj);
                if (ThreadHeaderQueue.GcMark(obj) == reclamationColor) {
                    // Not marked.
                    SegregatedFreeList.FreeLarge(obj);
                }
                return objectSize;
            }

        }

        /// <summary>
        /// Find the object address for a given interior pointer.
        /// </summary>
        internal override UIntPtr FindObjectAddr(UIntPtr interiorPtr) {
            return SegregatedFreeList.Find(interiorPtr);
        }

        /// <summary>
        /// Visit all objects in the heap with a specified visitor.
        /// </summary>
        internal override
        void VisitObjects(ObjectLayout.ObjectVisitor objectVisitor,
                          UIntPtr lowAddr, UIntPtr highAddr)
        {
            VTable.Assert(PageTable.PageAligned(lowAddr),
                          "low not page aligned");
            VTable.Assert(PageTable.PageAligned(highAddr),
                          "high not page aligned");
            UIntPtr lowPage = PageTable.Page(lowAddr);
            UIntPtr highPage = PageTable.Page(highAddr);
            SegregatedFreeList.VisitObjects(lowPage, highPage, objectVisitor);
        }

        /// <summary>
        /// A new thread has been created, set any allocator/collector state.
        /// </summary>
        internal override void NewThreadNotification(Thread newThread,
                                                     bool initial)
        {
            base.NewThreadNotification(newThread, initial);
            threadColor[newThread.threadIndex] = markedColor;
            ThreadHeaderQueue.Reset(newThread);
            SegregatedFreeList.NewThreadNotification(newThread, initial);
            if (CurrentMarkingPhase == MarkingPhase.ComputingRoots) {
                Transitions.MakeGCRequest(newThread.threadIndex);
            }
        }

        internal override void DeadThreadNotification(Thread deadThread)
        {
            MultiUseWord.CollectFromThread(deadThread);
            SegregatedFreeList.DeadThreadNotification(deadThread);
            ThreadHeaderQueue.DeadThreadNotification(deadThread, markedColor);
            threadColor[deadThread.threadIndex] = (UIntPtr) noColor;
            base.DeadThreadNotification(deadThread);
        }

        internal override void ThreadStartNotification(int currentThreadIndex)
        {
            base.ThreadStartNotification(currentThreadIndex);
            threadColor[currentThreadIndex] = markedColor;
            if (CurrentMarkingPhase == MarkingPhase.ComputingRoots) {
                Transitions.MakeGCRequest(currentThreadIndex);
            }
        }

        internal override void ThreadDormantGCNotification(int threadIndex) {
            // We could scan our own stack, but instead we try to get
            // some work done while the Trace thread scans our stack.
            Thread.SignalGCEvent(markThread.threadIndex);
        }

        /// <summary>
        /// This class is used to verify that there are no dangling pointers.
        /// </summary>
        private class VerifyVisitor : SegregatedFreeList.ObjectVisitor
        {

            internal static VerifyVisitor visitor = new VerifyVisitor();

            internal override void VisitSmall(Object obj, UIntPtr memAddr) {
                if (ThreadHeaderQueue.GcMark(obj) == markedColor) {
                    VerifyMarkVisitor.visitor.VisitReferenceFields(obj);
                } else {
                    VTable.Assert(ThreadHeaderQueue.GcMark(obj) ==
                                  unmarkedColor);
                }
            }

            internal override UIntPtr VisitLarge(Object obj) {
                UIntPtr size;
                if (ThreadHeaderQueue.GcMark(obj) == markedColor) {
                    // The object has the mark color, so it should only
                    // reference other objects with the mark color.
                    size = VerifyMarkVisitor.visitor.VisitReferenceFields(obj);
                } else {
                    VTable.Assert(ThreadHeaderQueue.GcMark(obj) ==
                                  unmarkedColor);
                    size = ObjectLayout.Sizeof(obj);
                }
                return size;
            }

        }

        /// <summary>
        /// This class is used to check that all the pointers within a marked
        /// object point into other marked objects.
        /// </summary>
        private class VerifyMarkVisitor : NonNullReferenceVisitor
        {

            internal static VerifyMarkVisitor visitor
                = new VerifyMarkVisitor();

            internal unsafe override void Visit(UIntPtr *loc) {
                UIntPtr addr = *loc;
                UIntPtr page = PageTable.Page(addr);
                if (PageTable.IsGcPage(page)) {
                    Object obj = Magic.fromAddress(addr);

                    VTable.Assert(ThreadHeaderQueue.GcMark(obj) == markedColor,
                                  "dangling pointer!");
                }
            }

        }

        /// <summary>
        /// This method loops through all non-null threads and asserts that
        /// no thread has any work on its marking queue.
        /// </summary>
        private void VerifyEmptyQueues() {
            Thread[] threadTable = Thread.threadTable;
            for (int i = 0; i < threadTable.Length; i++) {
                Thread t = threadTable[i];
                if (t != null) {
                    VTable.Assert(ThreadHeaderQueue.IsEmpty(t),
                                  "Non-empty Queue!");
                }
            }
        }

        /// <summary>
        /// This method walks through all objects in the heap to ensure
        /// that no objects have values in their queue header field
        /// </summary>
        private void VerifyQueueHeaders() {
            SegregatedFreeList.VisitAllObjects(VerifyHeaderVisitor.visitor);
        }

        /// <summary>
        /// This visitor trivially asserts that the objects queue header
        /// field is zero.
        /// </summary>
        private class VerifyHeaderVisitor : SegregatedFreeList.ObjectVisitor
        {

            internal static VerifyHeaderVisitor visitor
                = new VerifyHeaderVisitor();

            /// <summary>
            /// Visit small objects, checking queue header.
            /// </summary>
            internal unsafe override void VisitSmall(Object obj,
                                                     UIntPtr memAddr)
            {
                VTable.Deny(ThreadHeaderQueue.IsInQueue(obj),
                            "Object in ThreadHeaderQueue");
            }

            internal override UIntPtr VisitLarge(Object obj) {
                VTable.Deny(ThreadHeaderQueue.IsInQueue(obj),
                            "Object in ThreadHeaderQueue");
                return ObjectLayout.Sizeof(obj);
            }

        }

    }

}
