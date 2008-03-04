////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   MinScheduler.cs
//
//  Note:   Minimal round-robin style without priorities scheduler.
//
//          MinScheduler favors thread that have recently become unblocked
//          and tries to avoid reading the clock or reseting the timer as
//          much as possible.
//
//          The minimal scheduler maintains two queues of threads that can
//          be scheduled.  The unblockedThreads queue contains threads which
//          have become unblocked during this scheduling quantum; mostly,
//          these are threads that were unblocked by the running thread.
//          The runnableThreads queue contains all other threads that are
//          currently runnable.  If the current thread blocks, MinScheduler
//          will schedule threads from the unblockedThread queue, without
//          reseting the timer.  When the timer finally fires, MinScheduler
//          moves all unblockedThreads to the end of the runnableThreads
//          queue and schedules the next runnableThread.
//

//#define DEBUG_DISPATCH
//#define DEBUG_TIMER

using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

using Microsoft.Bartok.Options;

using Microsoft.Singularity;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Scheduling;
using Microsoft.Singularity.V1.Threads;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// Summary description for MinScheduler.
    /// </summary>
    [NoCCtor]
    [CLSCompliant(false)]
    [Mixin(typeof(Scheduler))]
    public class MinScheduler : Scheduler
    {
        // List of recently runnable, but unscheduled threads.
        private static ThreadQueue unblockedThreads;

        // List of runnable, but unscheduled threads.
        private static ThreadQueue runnableThreads;

        // List of blocked threads, sorted by wait time.
        private static ThreadQueue blockedThreads;

        // List of frozen threads (unsorted)
        private static ThreadQueue frozenThreads;

        // set to something large to debug!
        private static TimeSpan minSlice;
        private static TimeSpan idleSlice;

        //////////////////////////////////////////////////////////////////////
        //
        [MixinExtend("Initialize")]
        public static void Initialize(Process idleProcess) {
            Scheduler.Initialize();

            unblockedThreads = new ThreadQueue();
            runnableThreads = new ThreadQueue();
            blockedThreads = new ThreadQueue();
            frozenThreads = new ThreadQueue();

            minSlice = TimeSpan.FromMilliseconds(10);
            // If busy, don't run for more than 10ms on same task.
            idleSlice = TimeSpan.FromDays(30);
            // If idle, wake up once a month whether we need to or not.

            // Create the idle threads and put them on the idleThreads loop.
            for (int i = 0; i < Processor.processorTable.Length; i++) {
                Thread idle = Thread.CreateIdleThread(Processor.processorTable[i]);
                Processor.processorTable[i].IdleThread = idle;

                ThreadHandle handle;
                idleProcess.AddThread(idle,out handle);

                DispatchLock();
                idleThreads.EnqueueTail(idle.schedulerEntry);
                DispatchRelease();
            }

            bool iflag = Processor.DisableInterrupts();
            Processor.CurrentProcessor.SetNextTimerInterrupt(
                SchedulerTime.MinValue + TimeSpan.FromMilliseconds(5)
                );
            Processor.RestoreInterrupts(iflag);
        }

        [MixinExtend("Finalize")]
        new public static void Finalize()
        {
            Scheduler.Finalize();
        }

        /////////////////////////////////////////// Scheduling Event Handlers.
        //
        //  Each of these should be replaced by the scheduler mixin.
        //

        [MixinOverride]
        new public static void OnThreadStateInitialize(Thread thread, bool constructorCalled)
        {
            // Only initialize thread-local state!  No locks held and interrupts on.
        }

        [MixinOverride]
        new public static void OnThreadStart(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();
            DebugStub.Assert(thread.freezeCount == 0);
            EnqueueThreadToRun(unblockedThreads, thread);
        }

        [MixinOverride]
        new public static Thread OnThreadBlocked(Thread thread, SchedulerTime stop)
        {
            Scheduler.AssertDispatchLockHeld();

            // First, put the thread on the blocked queue.
            EnqueueBlockedThread(thread, stop);

            // Now, find a runnable thread
            return NextRunnableThread();
        }

        [MixinOverride]
        [NoHeapAllocation]
        new public static void OnThreadUnblocked(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();

#if DEBUG_DISPATCH
            DebugStub.WriteLine("OnThreadUnblocked: thread={0:x8} [before]",
                                __arglist(Kernel.AddressOf(thread)));
            //#if DEBUG_DISPATCH
            //DumpBlockedThreads();
#endif // DEBUG_DISPATCH

            ThreadEntry entry = thread.schedulerEntry;
            DebugStub.Assert(entry.queue == blockedThreads
                             || entry.queue == frozenThreads);
            entry.RemoveFromQueue();
            EnqueueThreadToRun(unblockedThreads, thread);

#if DEBUG_DISPATCH
            DebugStub.WriteLine("OnThreadUnblocked: after={0:x8} [before]",
                                __arglist(Kernel.AddressOf(thread)));
            DumpBlockedThreads();
#endif // DEBUG_DISPATCH
        }

        [MixinOverride]
        [NoHeapAllocation]
        new public static Thread OnThreadYield(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();

            Thread target = NextRunnableThread();
            if (target == null) {
                return thread;
            }
            else {
                EnqueueThreadToRun(runnableThreads, thread);
                return target;
            }
        }

        [MixinOverride]
        new public static Thread OnThreadStop(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();

            return NextRunnableThread();
        }

        [MixinOverride]
        new public static void OnThreadFreezeIncrement(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();
            thread.freezeCount++;
            if (thread.ActiveProcessor == null) {
                ThreadEntry entry = thread.schedulerEntry;
                entry.RemoveFromQueue();
                frozenThreads.EnqueueTail(entry);
            }
            // (If thread.ActiveProcessor != null, thread will move
            // to frozenThreads when it is descheduled.)
        }

        [MixinOverride]
        new public static void OnThreadFreezeDecrement(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();
            DebugStub.Assert(thread.freezeCount > 0);
            thread.freezeCount--;
            if (thread.freezeCount == 0) {
                ThreadEntry entry = thread.schedulerEntry;
                frozenThreads.Remove(entry);
                EnqueueThread(thread);
            }
        }

        [MixinOverride]
        [CLSCompliant(false)]
        [NoHeapAllocation]
        new public static Thread OnTimerInterrupt(Thread thread, SchedulerTime now)
        {
            Scheduler.AssertDispatchLockHeld();

            ThreadEntry entry;

            // Move the recently unblocked threads to the runnable queue.
            while ((entry = unblockedThreads.DequeueHead()) != null) {
                EnqueueThreadToRun(runnableThreads, entry.Thread);
            }

            // Quantum is up, move current thread back to the runnable list.
            if (thread != null) {
                EnqueueThreadToRun(runnableThreads, thread);
            }

#if DEBUG_DISPATCH
            DebugStub.WriteLine("OnTimerInterrupt : thread={0:x8} cpu={1} [before]",
                                __arglist(Kernel.AddressOf(thread),
                                          Processor.GetCurrentProcessorId()));
            //#if DEBUG_DISPATCH
            //DumpBlockedThreads();
#endif // DEBUG_DISPATCH

            // Now, unblock any threads whose timers have elapsed.
            while ((entry = blockedThreads.Head) != null &&
                   entry.Thread.BlockedUntil <= now) {

#if DEBUG_DISPATCH
                DebugStub.WriteLine("OnTimerInterrupt : thread={0:x8} cpu={1} [will remove]",
                                    __arglist(Kernel.AddressOf(entry.Thread),
                                              Processor.GetCurrentProcessorId()));
#endif // DEBUG_DISPATCH

                blockedThreads.Remove(entry);
                entry.Thread.WaitFail();
                EnqueueThreadToRun(runnableThreads, entry.Thread);
            }

#if DEBUG_DISPATCH
            DebugStub.WriteLine("OnTimerInterrupt : thread={0:x8} cpu={1} [after]",
                                __arglist(Kernel.AddressOf(thread),
                                          Processor.GetCurrentProcessorId()));
            DumpBlockedThreads();
#endif // DEBUG_DISPATCH

            DebugStub.Assert(minSlice.Ticks != 0); // Initialization failure
            DebugStub.Assert(idleSlice.Ticks != 0); // Initialization failure

            // Choose a default timeout depending on whether their are
            // runnable threads or not.
            SchedulerTime stop;
            if (runnableThreads.IsEmpty()) {
                stop = new SchedulerTime(now.Ticks + idleSlice.Ticks);
            }
            else {
                stop = new SchedulerTime(now.Ticks + minSlice.Ticks);
            }

            // Then adjust the timeout so that we wake up sooner if
            // there is a thread waiting on a timer.
            if (entry != null && entry.Thread.BlockedUntil < stop) {
                stop = entry.Thread.BlockedUntil;
            }
            Processor.CurrentProcessor.SetNextTimerInterrupt(stop);

#if DEBUG_TIMER
            DebugStub.WriteLine("Next Timer: {0}ms",
                                __arglist((stop.Ticks - now.Ticks) / TimeSpan.TicksPerMillisecond));
#endif // DEBUG_TIMER

            VTable.Assert(unblockedThreads.Head == null);
            return runnableThreads.DequeueHeadThread();
        }

        [MixinOverride]
        [NoHeapAllocation]
        new public static Thread OnIoInterrupt(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();

            if (thread != null) {
                EnqueueThreadToRun(unblockedThreads, thread);
            }
            return unblockedThreads.DequeueHeadThread();
        }

        [MixinOverride]
        [NoHeapAllocation]
        new public static void OnProcessorShutdown(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();

            if (thread != null) {
                EnqueueThreadToRun(unblockedThreads, thread);
            }
        }

        /////////////////////////////////// Helper Methods
        //
        [Inline]
        [NoHeapAllocation]
        private static Thread NextRunnableThread() {
            Thread target = unblockedThreads.DequeueHeadThread();
            if (target == null) {
                target = runnableThreads.DequeueHeadThread();
            }
            return target;
        }

        private static void EnqueueThread(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();
            if (thread.blocked) {
                EnqueueBlockedThread(thread, thread.BlockedUntil);
            }
            else {
                EnqueueThreadToRun(runnableThreads, thread);
            }
        }

        // Enqueue ready-to-run thread in queue
        // (where queue==runnableThreads or queue==unblockedThreads).
        // Frozen threads are rerouted to the frozen queue.
        [Inline]
        [NoHeapAllocation]
        private static void EnqueueThreadToRun(ThreadQueue queue,
            Thread thread)
        {
            if (thread.freezeCount == 0) {
                queue.EnqueueTail(thread.schedulerEntry);
            }
            else {
                frozenThreads.EnqueueTail(thread.schedulerEntry);
            }
        }

        [Inline]
        private static void EnqueueBlockedThread(Thread thread, SchedulerTime stop)
        {
            Scheduler.AssertDispatchLockHeld();
            // Put the thread on the blocked queue.
            bool updateStop = ((blockedThreads.Head == null) ||
                               (blockedThreads.Head.Thread.BlockedUntil > stop));

            if (updateStop && stop <= SchedulerTime.Now) {
                // Optimization: don't bother setting a timer for a thread
                // whose blocking has already timed out.  Wake it immediately.
                thread.WaitFail();
                EnqueueThreadToRun(runnableThreads, thread);
                return;
            }

            if (stop == SchedulerTime.MaxValue) {
                blockedThreads.EnqueueTail(thread.schedulerEntry);
#if DEBUG_DISPATCH
                DebugStub.Print("OnThreadBlocked  : thread={0:x8} cpu={1} EnqueueTail\n",
                                __arglist(Kernel.AddressOf(thread),
                                      Processor.GetCurrentProcessorId()));
#endif // DEBUG_DISPATCH
            }
            else {
                ThreadEntry entry = blockedThreads.Head;
#if DEBUG_DISPATCH
                DebugStub.Print("OnThreadBlocked  : thread={0:x8} cpu={1} stop={2}\n",
                                __arglist(Kernel.AddressOf(thread),
                                          Processor.GetCurrentProcessorId(),
                                          stop.Ticks));
#endif // DEBUG_DISPATCH
                while (entry != null && entry.Thread.BlockedUntil <= stop) {
                    // Loop until we find the first thread with a later stop.
                    entry = entry.Next;
#if DEBUG_DISPATCH
                    DebugStub.Print("OnThreadBlocked  : BlockedUntil={0}\n",
                                    __arglist(entry.Thread.BlockedUntil.Ticks));
#endif // DEBUG_DISPATCH
                }

                blockedThreads.InsertBefore(entry, thread.schedulerEntry);
            }

            VTable.Assert(blockedThreads.IsEnqueued(thread.schedulerEntry));


            if (updateStop) {
#if DEBUG_DISPATCH
                DebugStub.Print("OnThreadBlocked  : SetNextTimerInterrupt({0})\n",
                                __arglist(stop.Ticks));
#endif // DEBUG_DISPATCH
                Processor.CurrentProcessor.SetNextTimerInterrupt(stop);
            }
            else {
#if DEBUG_DISPATCH
                DebugStub.Print("OnThreadBlocked  : No timer update needed\n");
#endif // DEBUG_DISPATCH
            }

            DumpBlockedThreads();
        }

        /////////////////////////////////// Diagnostics and Debugging Support.
        //
        [Conditional("DEBUG_DISPATCH")]
        public static void DumpBlockedThreads()
        {
            // First, put the thread on the blocked queue.
            ThreadEntry entry = blockedThreads.Head;
            for (; entry != null; entry = entry.Next) {
                DebugStub.Print("      thread={0:x8} stop= {1}\n",
                                __arglist(
                                    Kernel.AddressOf(entry.Thread),
                                    entry.Thread.BlockedUntil.Ticks));
            }
        }

        //////////////////////////////////////////////////////////////////////
        //
    }
}
