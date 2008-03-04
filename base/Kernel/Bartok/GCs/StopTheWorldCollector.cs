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

    using System.Threading;
    using System.Runtime.CompilerServices;

#if SINGULARITY
#if SINGULARITY_KERNEL
  using Microsoft.Singularity;
  using Microsoft.Singularity.Scheduling;
#else
  using Microsoft.Singularity;
  using Microsoft.Singularity.V1.Services;
#endif

    [CLSCompliant(false)]
    public enum GarbageCollectorEvent : ushort
    {
        StartStopTheWorld = 1,
        EndStopTheWorld = 2,
        StartCollection = 3,
        EndCollection = 4,
    }
#endif

    [NoCCtor]
    internal abstract class StopTheWorldCollector : BaseCollector
    {

        internal static int collectorThreadIndex;

        /// <summary>
        /// Identifies the current collection phase
        /// </summary>
        internal enum STWPhase {
            Dummy,              // Not used!
            Idle,               // No collection is taking place
            Synchronizing,      // Attempting to stop the world
            SingleThreaded,     // Stop-the-world phase
        }

        /// <summary>
        /// The current state of the collector.
        /// </summary>
        internal static STWPhase CurrentPhase;

        [PreInitRefCounts]
        internal static void Initialize() {
            collectorThreadIndex = -1;
            CurrentPhase = STWPhase.Idle;
        }

        internal abstract void CollectStopped(int currentThreadIndex,
                                              int generation);

        // Implementation of GCInterface
        internal override bool IsOnTheFlyCollector {
            get {
                return false;
            }
        }

        internal override void Collect(int currentThreadIndex,
                                       int generation)
        {
            int foundIndex =
                Interlocked.CompareExchange(ref collectorThreadIndex,
                                            currentThreadIndex, -1);
            if (foundIndex < 0) {
                // We are the designated collector thread
                PerformCollection(generation);
            } else {
                Transitions.TakeDormantControlNoGC(currentThreadIndex);
                // The 'foundIndex' thread may have completed its
                // collection and another thread may have started
                // another collection after we read
                // 'collectorThreadIndex'.  The collector thread may
                // have decided to wait for us before we entered
                // DormantState, so we have to read
                // 'collectorThreadIndex' again.
                foundIndex = collectorThreadIndex;
                if (foundIndex >= 0) {
                    Thread.SignalGCEvent(foundIndex);
                }
                Transitions.TakeMutatorControlNoGC(currentThreadIndex);
            }
        }

        private void PerformCollection(int generation)
        {
#if SINGULARITY
            Tracing.Log(Tracing.Debug,"GC start");
#endif
            CollectorStatistics.Event(GCEvent.StopTheWorld);
            CurrentPhase = STWPhase.Synchronizing;
            StopTheWorld();
            CurrentPhase = STWPhase.SingleThreaded;
#if SINGULARITY
            long preGcMemoryUsage = GC.GetTotalMemory(false);
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
#endif  //singularity
#if SINGULARITY_KERNEL
            bool iflag = Processor.DisableInterrupts();
#endif
#if SINGULARITY
            ulong beg = Processor.GetCycleCount();
#endif
            // Preparation
            GC.allocationInhibitGC = true;
            // Verify the heap before GC
            if (VTable.enableGCVerify) {
                this.VerifyHeap(true);
            }
            // Invoke the chosen collector
#if SINGULARITY
            Monitoring.Log(Monitoring.Provider.GC,
                           (ushort)GarbageCollectorEvent.StartCollection);
#endif
            this.CollectStopped(collectorThreadIndex, generation);
#if SINGULARITY
            Monitoring.Log(Monitoring.Provider.GC,
                           (ushort)GarbageCollectorEvent.EndCollection);
#endif
            // Verify the heap after GC
            if (VTable.enableGCVerify) {
                this.VerifyHeap(false);
            }
            if (VTable.enableGCAccounting) {
                MemoryAccounting.Report(GC.gcType);
            }
            // Cleanup
            CollectorStatistics.Event(GCEvent.ResumeTheWorld);
            GC.allocationInhibitGC = false;
            CurrentPhase = STWPhase.Idle;
#if SINGULARITY
            long postGcMemoryUsage = GC.GetTotalMemory(false);
#endif
            ResumeTheWorld();
            collectorThreadIndex = -1;
#if SINGULARITY
            Tracing.Log(Tracing.Debug,"GC stop");
            long pagesCollected = preGcMemoryUsage - postGcMemoryUsage;
#if SINGULARITY_KERNEL
#if THREAD_TIME_ACCOUNTING
            int procId = Thread.CurrentProcess.ProcessId;
            ticks = Thread.CurrentThread.ExecutionTime - ticks;
            ticks2 = SystemClock.KernelUpTime - ticks2;
            Process.kernelProcess.SetGcPerformanceCounters(ticks, (long) pagesCollected);
#else
            ticks = SystemClock.KernelUpTime - ticks;
#endif
            Thread.CurrentProcess.SetGcPerformanceCounters(ticks, (long) pagesCollected);
#elif SINGULARITY_PROCESS
#if THREAD_TIME_ACCOUNTING
            ushort procId = ProcessService.GetCurrentProcessId();
            ticks = ProcessService.GetThreadTime()  - ticks;
            ticks2 = ProcessService.GetUpTime() - ticks2;
#else
            ticks = ProcessService.GetUpTime() - ticks;
#endif
            ProcessService.SetGcPerformanceCounters(ticks, (long) pagesCollected);
#endif

#if DEBUG
#if THREAD_TIME_ACCOUNTING
            DebugStub.WriteLine("~~~~~ StopTheWorld [collected pages={0:x8}, pid={1:x3}, ms(Thread)={2:d6}, ms(System)={3:d6}, procId={4}, tid={5}]",
                                __arglist(pagesCollected,
                                          PageTable.processTag >> 16,
                                          ticks.Milliseconds,
                                          ticks2.Milliseconds,
                                          procId,
                                          Thread.GetCurrentThreadIndex()
                                          ));
#endif
#endif
#endif

#if SINGULARITY
            DebugStub.AddToPerfCounter(GC.perfCounter, Processor.GetCycleCount() - beg);
#endif
#if SINGULARITY_KERNEL
#else
#if false
            DebugStub.WriteLine("GC{0} after {1,11}",
                                __arglist(
                                    ProcessService.GetCurrentProcessId(),
                                    (UIntPtr)GC.installedGC.TotalMemory));
            if (GC.installedGC.TotalMemory >= VTable.enableGCAccountingThreshold) {
                DebugStub.WriteLine("GC memory {0,16}", __arglist(GC.installedGC.TotalMemory));
            }
#endif
#endif

#if SINGULARITY_KERNEL
            Processor.RestoreInterrupts(iflag);
#endif
        }

        internal override void CheckForNeededGCWork(Thread currentThread) {
            while (CurrentPhase == STWPhase.Synchronizing &&
                   currentThread.threadIndex != collectorThreadIndex) {
                GC.InvokeCollection(currentThread);
            }
        }

        internal override void NewThreadNotification(Thread newThread,
                                                     bool initial)
        {
            base.NewThreadNotification(newThread, initial);
            if (CurrentPhase == STWPhase.Synchronizing) {
                Transitions.MakeGCRequest(newThread.threadIndex);
            }
        }

        internal override void DeadThreadNotification(Thread deadThread)
        {
            MultiUseWord.CollectFromThread(deadThread);
            base.DeadThreadNotification(deadThread);
        }

        internal override void ThreadDormantGCNotification(int threadIndex) {
            int ctid = collectorThreadIndex;
            if (ctid >= 0) {
                Thread.SignalGCEvent(ctid);
            }
        }

        private static void StopTheWorld() {
#if SINGULARITY
            //DebugStub.WriteLine("~~~~~ StopTheWorld()");
            Monitoring.Log(Monitoring.Provider.GC,
                           (ushort)GarbageCollectorEvent.StartStopTheWorld);
#if SINGULARITY_KERNEL
            TimeSpan ticks = SystemClock.KernelUpTime;
#elif SINGULARITY_PROCESS
            TimeSpan ticks = ProcessService.GetUpTime();
#endif
#endif
            VTable.Assert(Thread.GetCurrentThreadIndex() ==
                          collectorThreadIndex);
            Transitions.MakeGCRequests(collectorThreadIndex);
            // Force threads to take allocation slow path.
            for (int i = 0; i < Thread.threadTable.Length; i++) {
                Thread t = Thread.threadTable[i];
                if (t != null) {
                    BumpAllocator.Preempt(t);
                }
            }
            for(int i = 0; i < Thread.threadTable.Length; i++) {
                if (Thread.threadTable[i] == null) {
                    continue;
                }
                if (i == collectorThreadIndex) {
                    continue;
                }
                CollectorStatistics.Event(GCEvent.StopThread, i);
                while (!Transitions.TakeGCControl(i) &&
                       !Transitions.UnderGCControl(i) &&
                       Transitions.HasGCRequest(i) &&
                       Thread.threadTable[i] != null) {
                    Thread.WaitForGCEvent(collectorThreadIndex);
                }
            }
#if SINGULARITY
#if SINGULARITY_KERNEL
            ticks = SystemClock.KernelUpTime - ticks;
#elif SINGULARITY_PROCESS
            ticks = ProcessService.GetUpTime() - ticks;
#endif
                Monitoring.Log(Monitoring.Provider.GC,
                               (ushort)GarbageCollectorEvent.EndStopTheWorld);
#endif
        }

        private static void ResumeTheWorld() {
            VTable.Assert(Thread.GetCurrentThreadIndex() ==
                          collectorThreadIndex);
            for(int i = 0; i < Thread.threadTable.Length; i++) {
#if SINGULARITY_KERNEL
                if (Scheduler.IsIdleThread(i)) {
                    continue;
                }
#endif
                if (i == collectorThreadIndex) {
                    if (Transitions.HasGCRequest(i)) {
                        Transitions.ClearGCRequest(i);
                    }
                } else if (Transitions.UnderGCControl(i)) {
                    Transitions.ReleaseGCControl(i);
                } else if (Thread.threadTable[i] != null) {
                    Thread.SignalGCEvent(i);
                }
            }
        }

    }

}
