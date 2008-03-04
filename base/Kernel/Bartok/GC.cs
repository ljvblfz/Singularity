/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace System
{
    using Microsoft.Bartok.Runtime;
    using System.GCs;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Threading;

#if SINGULARITY
    using Microsoft.Singularity;
    using Microsoft.Singularity.X86;
#endif

    // The GC has only static members and doesn't require the serializable
    // keyword.
    [CCtorIsRunDuringStartup]
    [RequiredByBartok]
    [CLSCompliant(false)]
    public sealed class GC
    {

        // Bartok runtime "magic" function
        // It saves the callee-save registers in a transition record and
        // calls System.GC.CollectBody(thread, generation)
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.GCFRIEND)]
        [StackBound(128)]
        private static extern void CollectBodyTransition(Thread thread,
                                                         int generation);
        internal static int         gcTotalCount;
        internal static long        gcTotalTime;
        internal static long        maxPauseTime;
        internal static long        pauseCount;
        internal static long        gcTotalBytes;
        internal static ulong       bytesAllocated;
        internal static ulong       objectsAllocated;
#if SINGULARITY_KERNEL
        internal static uint        perfCounter = 6;
#else
        internal static uint        perfCounter = 5;
#endif

        [RequiredByBartok]
        [TrustedNonNull]
        internal static Collector   installedGC;

        private  static bool        isProfiling;

        private  static Object      dummyGlobal; // Used by KeepAlive

        [AccessedByRuntime("referenced in halasm.asm/brtasm.asm")]
        internal static bool        allocationInhibitGC = false;

        internal static UIntPtr     newBytesSinceGC;

        [Intrinsic]
        internal static GCType gcType;

        [Intrinsic]
        internal static WBType wbType;

        [Intrinsic]
        internal static RemSetType remsetType;

        [Intrinsic]
        internal static CopyScanType copyscanType;

#if !SINGULARITY
        [NoStackLinkCheck]
#endif
        [PreInitRefCounts]
        internal static void ConstructHeap() {
            PageTable.Initialize();

            MemoryManager.Initialize();
#if SINGULARITY
            UIntPtr heap_commit_size = new UIntPtr(1 << 16);
#else
            UIntPtr heap_commit_size = new UIntPtr(1 << 20);
#endif
            UIntPtr os_commit_size = MemoryManager.OperatingSystemCommitSize;
            VTable.Assert(os_commit_size > UIntPtr.Zero);
            VTable.Assert(heap_commit_size >= os_commit_size);
            UIntPtr bootstrapSize =
                UIntPtr.Size == 8
                ? (UIntPtr) 1 << 15
                : (UIntPtr) 1 << 14;
            if (bootstrapSize < os_commit_size) {
                bootstrapSize = os_commit_size;
            }
            BootstrapMemory.Initialize(bootstrapSize);
            StaticData.Initialize();
            PageManager.Initialize(os_commit_size, heap_commit_size);
        }

        // Called after the GC is up, but before multi-threading is enabled.
        internal static void FinishInitializeThread()
        {
            PageManager.FinishInitializeThread();
        }

        // NB: This is called from VTable.Initialize()
        [PreInitRefCounts]
        static GC() // Class Constructor (cctor)
        {
            switch(gcType) {
#if !SINGULARITY || ADAPTIVE_COPYING_COLLECTOR
              case GCType.AdaptiveCopyingCollector: {
                  AdaptiveCopyingCollector.Initialize();
                  GC.installedGC = AdaptiveCopyingCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY || MARK_SWEEP_COLLECTOR
              case GCType.MarkSweepCollector: {
                  MarkSweepCollector.Initialize();
                  GC.installedGC = MarkSweepCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY || TABLE_MARK_SWEEP_COLLECTOR
              case GCType.TableMarkSweepCollector: {
                  SimpleMarkSweepCollector.Initialize();
                  GC.installedGC = SimpleMarkSweepCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY || SEMISPACE_COLLECTOR
              case GCType.SemispaceCollector: {
                  SemispaceCollector.Initialize();
                  GC.installedGC = SemispaceCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY || SLIDING_COLLECTOR
              case GCType.SlidingCollector: {
                  SlidingCollector.Initialize();
                  GC.installedGC = SlidingCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY || CONCURRENT_MS_COLLECTOR
              case GCType.ConcurrentMSCollector: {
                  ConcurrentMSCollector.Initialize();
                  GC.installedGC = ConcurrentMSCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY || ATOMIC_RC_COLLECTOR
              case GCType.AtomicRCCollector: {
                  AtomicRCCollector.Initialize();
                  GC.installedGC = AtomicRCCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY
              case GCType.ReferenceCountingCollector: {
                  ReferenceCountingCollector.Initialize();
                  GC.installedGC = ReferenceCountingCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY
              case GCType.DeferredReferenceCountingCollector: {
                  DeferredReferenceCountingCollector.Initialize();
                  GC.installedGC = DeferredReferenceCountingCollector.instance;
                  break;
              }
#endif
#if !SINGULARITY
              case GCType.NullCollector: {
                  VTable.Assert(wbType == 0, "No need for a write barrier");
                  GC.Initialize();
                  GC.installedGC =
                      (NullCollector)
                      BootstrapMemory.Allocate(typeof(NullCollector));
                  break;
              }
#endif
              default: {
                  VTable.NotReached("Unknown GC type: "+gcType);
                  break;
              }
            }
            GC.installedGC.NewThreadNotification(Thread.initialThread, true);
            GC.installedGC.ThreadStartNotification(Thread.initialThread.threadIndex);
        }

        [PreInitRefCounts]
        internal static void Initialize()
        {
            Transitions.Initialize();
            WriteBarrier.Initialize();
        }

        private static void FinishedGCCycle()
        {
            gcTotalCount++;
            gcTotalBytes += (long) newBytesSinceGC;
            newBytesSinceGC = UIntPtr.Zero;
        }

#if !SINGULARITY
        private static DateTime LogMessage(String message)
        {
            DateTime currentTime = System.DateTime.Now;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            String hourString = currentTime.Hour.ToString();
            if (hourString.Length == 1) {
                sb.Append('0');
            }
            sb.Append(hourString);
            sb.Append(':');
            String minuteString = currentTime.Minute.ToString();
            if (minuteString.Length == 1) {
                sb.Append('0');
            }
            sb.Append(minuteString);
            sb.Append(':');
            String secondString = currentTime.Second.ToString();
            if (secondString.Length == 1) {
                sb.Append('0');
            }
            sb.Append(secondString);
            sb.Append('.');
            String milliString = currentTime.Millisecond.ToString();
            if (milliString.Length < 3) {
                sb.Append('0');
            }
            if (milliString.Length < 2) {
                sb.Append('0');
            }
            sb.Append(milliString);
            sb.Append(":  ");
            sb.Append(message);
            Console.Out.WriteLine(sb.ToString());
            return currentTime;
        }
#endif

        // This empty class allows us to easily spot the HeapCritialSection
        // mutex when debugging.
        private class HeapMonitor
        {
        }

        internal static void CheckForNeededGCWork(Thread currentThread) {
            installedGC.CheckForNeededGCWork(currentThread);
        }

#if SINGULARITY
        // This is a Singularity special not in the CLR
        public static void Verify()
        {
            DebugStub.WriteLine("Calling VerifyHeap()");
            bool oldGCVerify = VTable.enableGCVerify;
            VTable.enableGCVerify = true;
            Collect();
            VTable.enableGCVerify = oldGCVerify;
            DebugStub.WriteLine("Verification finished.");
        }

        public static void PerformanceCounters(out int collectorCount,
                                               out long collectorMillis,
                                               out long collectorBytes)
        {
            collectorCount  = gcTotalCount;
            collectorMillis = gcTotalTime;
            collectorBytes  = gcTotalBytes;
        }
#endif

        // Garbage Collect all generations.
        public static void Collect()
        {
            CollectBodyTransition(Thread.CurrentThread, MaxGeneration);
        }

        public static void Collect(int generation)
        {
            if (generation < 0) {
                throw new ArgumentOutOfRangeException(
                    "generation",
                    "Argument should be positive!");
            }
            CollectBodyTransition(Thread.CurrentThread, generation);
        }

        internal static void InvokeCollection(Thread currentThread)
        {
            CollectBodyTransition(currentThread, -1);
        }

        internal static void InvokeMajorCollection(Thread currentThread)
        {
            CollectBodyTransition(currentThread, -2);
        }

        // DO NOT REMOVE THE StackLinkCheck ATTRIBUTE FROM THIS
        // FUNCTION!
        //
        // It is called from native code System.GC.CollectBodyTransition
        // that only has an attribute for the amount of stack space that
        // the native code requires.
        [StackLinkCheck]
        [AccessedByRuntime("called from halforgc.asm/brtforgc.asm")]
        private static unsafe Thread CollectBody(Thread currentThread,
                                                 int generation)
        {
            int startTicks = 0;
            bool enableGCTiming = VTable.enableGCTiming;
            if (enableGCTiming) {
                VTable.enableGCTiming = false;
                pauseCount++;
                startTicks = Environment.TickCount;
                VTable.DebugPrint("[GC start: {0} bytes]\n",
                                  __arglist(installedGC.TotalMemory));
            }
            if (VTable.enableGCWatermarks) {
                MemoryAccounting.RecordHeapWatermarks();
            }

            int currentThreadIndex = currentThread.threadIndex;
            // Our stack is GC safe after going through CollectBodyTransition
            installedGC.Collect(currentThreadIndex, generation);
            FinishedGCCycle();

            if (VTable.enableGCWatermarks) {
                MemoryAccounting.RecordHeapWatermarks();
            }
            if (enableGCTiming) {
                int elapsedTicks = Environment.TickCount - startTicks;
                gcTotalTime += elapsedTicks;
                if (maxPauseTime < elapsedTicks) {
                    maxPauseTime = elapsedTicks;
                }
                VTable.DebugPrint("[GC end  : {0} bytes, {1} ms]\n",
                                  __arglist(installedGC.TotalMemory,
                                            elapsedTicks));
                VTable.enableGCTiming = true;
            }
            return Thread.threadTable[currentThreadIndex];
        }

        [RequiredByBartok]
        [AccessedByRuntime("called from brtasm.asm")]
        [Inline]
        [ManualRefCounts]
        internal static Object AllocateObject(VTable vtable)
        {
            return AllocateObject(vtable, Thread.CurrentThread);
        }

        [RequiredByBartok]
        [Inline]
        [ManualRefCounts]
        internal static Object AllocateObject(VTable vtable,
                                              Thread currentThread)
        {
            VTable.Deny(Transitions.UnderGCControl(currentThread.threadIndex));
            return installedGC.AllocateObject(vtable, currentThread);
        }

        [NoInline]
        [ManualRefCounts]
        internal static Object AllocateObjectNoInline(VTable vtable,
                                                      Thread currentThread)
        {
            return GC.AllocateObject(vtable, currentThread);
        }

        [RequiredByBartok]
        [Inline]
        [ManualRefCounts]
        internal static Array AllocateVector(VTable vtable, int numElements)
        {
            return AllocateVector(vtable, numElements, Thread.CurrentThread);
        }

        [Inline]
        [ManualRefCounts]
        internal static Array AllocateVector(VTable vtable,
                                             int numElements,
                                             Thread currentThread)
        {
            VTable.Deny(Transitions.UnderGCControl(currentThread.threadIndex));
            return installedGC.AllocateVector(vtable, numElements,
                                              currentThread);
        }

        [RequiredByBartok]
        [Inline]
        [ManualRefCounts]
        internal static Array AllocateArray(VTable vtable, int rank,
                                            int totalElements)
        {
            return AllocateArray(vtable, rank, totalElements,
                                 Thread.CurrentThread);
        }

        [Inline]
        [ManualRefCounts]
        internal static Array AllocateArray(VTable vtable, int rank,
                                            int totalElements,
                                            Thread currentThread)
        {
            VTable.Deny(Transitions.UnderGCControl(currentThread.threadIndex));
            return installedGC.AllocateArray(vtable, rank, totalElements,
                                             currentThread);
        }

        [RequiredByBartok]
        [Inline]
        [ManualRefCounts]
        internal static String AllocateString(int stringLength)
        {
            return AllocateString(stringLength, Thread.CurrentThread);
        }

        [Inline]
        [ManualRefCounts]
        internal static String AllocateString(int stringLength,
                                              Thread currentThread)
        {
            VTable.Deny(Transitions.UnderGCControl(currentThread.threadIndex));
            return installedGC.AllocateString(stringLength, currentThread);
        }

        public static int GetGeneration(Object obj)
        {
            return installedGC.GetGeneration(obj);
        }

        public static int MaxGeneration {
            get { return installedGC.MaxGeneration; }
        }

        [NoInline]
        public static void KeepAlive(Object obj)
        {
            dummyGlobal = obj;
            dummyGlobal = null;
        }

        public static void WaitForPendingFinalizers()
        {
            Finalizer.WaitForPending();
        }

        public static long GetTotalMemory(bool forceFullCollection)
        {
            long size = installedGC.TotalMemory;
            if (!forceFullCollection) {
                return size;
            }
            // If we force a full collection, we will run the finalizers on all
            // existing objects and do a collection until the value stabilizes.
            // The value is "stable" when either the value is within 5% of the
            // previous call to installedGC.TotalMemory, or if we have been sitting
            // here for more than x times (we don't want to loop forever here).
            for (int reps = 0; reps < 8; reps++) {
                WaitForPendingFinalizers();
                Collect();
                long newSize = installedGC.TotalMemory;
                long bound = size / 20;  // 5%
                long diff = newSize - size;
                size = newSize;
                if (diff >= -bound && diff <= bound) {
                    break;
                }
            }
            return size;
        }

        public static void SuppressFinalize(Object obj)
        {
            if (obj == null) {
                throw new ArgumentNullException("obj");
            }
            Finalizer.SuppressCandidate(obj);
        }

        internal static void nativeSuppressFinalize(Object obj) {
            Finalizer.SuppressCandidate(obj);
        }

        public static void ReRegisterForFinalize(Object obj)
        {
            if (obj == null) {
                throw new ArgumentNullException("obj");
            }
            Finalizer.RegisterCandidate(obj);
        }

        public static int GetGeneration(WeakReference wo)
        {
            Object obj = wo.Target;
            if (obj == null) {
                throw new ArgumentException("wo", "target already collected");
            }
            return GetGeneration(obj);
        }

        public static void SetProfiler(GCProfiler profiler)
        {
            installedGC.SetProfiler(profiler);
            isProfiling = true;
        }

        public static bool IsProfiling
        {
            get {
                return isProfiling;
            }
        }

        internal static void ProfileAllocation(Object obj)
        {
            if (isProfiling) {
                installedGC.ProfileAllocation(obj);
            }
        }

        private static void SetCleanupCache()
        {
            // REVIEW: will not ever clean up these caches (such as Assembly
            // strong names)
        }

        internal static void EnableHeap()
        {
            CollectorStatistics.Initialize();
            CollectorStatistics.Event(GCEvent.CreateHeap);
            GC.installedGC.EnableHeap();
            Finalizer.StartFinalizerThread();
        }

        // Called on VM shutdown.
        internal static void DestructHeap()
        {
            if (VTable.enableGCWatermarks) {
                MemoryAccounting.RecordHeapWatermarks();
                MemoryAccounting.ReportHeapWatermarks();
            }

            if (VTable.enableGCTiming) {
#if SINGULARITY
                DebugStub.WriteLine("Total GC Time (ms): {0}",
                                    __arglist(gcTotalTime));
#else
                Console.Error.WriteLine("Total GC Time (ms): " + gcTotalTime);
                Console.Error.WriteLine("Max. Pause Time (ms): " + maxPauseTime);
                if (pauseCount != 0) {
                    Console.Error.WriteLine("Avg. Pause Time (ms): " +
                                            gcTotalTime/pauseCount);
                } else {
                    Console.Error.WriteLine("Avg. Pause Time (ms): 0");
                }
#endif
            }
            if (VTable.enableGCProfiling) {
#if !SINGULARITY
                Console.Error.WriteLine("Objects allocated: "+
                                        objectsAllocated);
                Console.Error.WriteLine("Total bytes allocated (KB): "+
                                        (bytesAllocated >> 10));
#endif
            }

            if(installedGC != null) {
                installedGC.DestructHeap();
            }
            CollectorStatistics.Event(GCEvent.DestroyHeap);
            CollectorStatistics.Summary();
        }

        internal static void NewThreadNotification(Thread newThread,
                                                   bool initial)
        {
            GC.installedGC.NewThreadNotification(newThread, initial);
        }

        internal static void DeadThreadNotification(Thread deadThread)
        {
            GC.installedGC.DeadThreadNotification(deadThread);
        }

        internal static void ThreadStartNotification(int currentThreadIndex)
        {
            GC.installedGC.ThreadStartNotification(currentThreadIndex);
        }

        internal static void ThreadEndNotification(Thread dyingThread)
        {
            GC.installedGC.ThreadEndNotification(dyingThread);
        }

        internal static void ThreadDormantGCNotification(int threadIndex)
        {
            GC.installedGC.ThreadDormantGCNotification(threadIndex);
        }

    }

}
