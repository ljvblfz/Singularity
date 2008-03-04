////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Scheduler.cs
//
//  Note:
//

// #define DEBUG_SCHEDULER

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Singularity;
using Microsoft.Singularity.Hal;
using Microsoft.Singularity.Io;

namespace Microsoft.Singularity.Scheduling
{
    public class Scheduler
    {
        public static readonly TimeSpan MaxPeriod = new TimeSpan(10000000);

        private static bool yieldFlag;
        public static bool YieldFlag
        {
            get { return yieldFlag; }
            set { yieldFlag = value; }
        }
        private static bool timerInterruptedFlag = true;

        public static bool TimerInterruptedFlag
        {
            get { return timerInterruptedFlag; }
            set { timerInterruptedFlag = value; }
        }

        //////////////////////////////////////////////////////////////////////
        /// <summary>
        /// Return the Task object that the calling thread is currently working on behalf of.
        /// </summary>
        public static Task CurrentTask()
        {
            return Thread.CurrentThread.CurrentTask();
        }

        /// <summary>
        /// Return the Activity object that the calling thread is currently working on behalf of.
        /// </summary>
        public static Activity CurrentActivity()
        {
            return Thread.CurrentThread.Activity; // XXX TBD
        }

        /// <summary>
        /// Information logging used for debugging schedulers.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        [CLSCompliant(false)]
        public static void LogWakeThread(Thread thread)
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogSchedulerLate()
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogContextSwitch()
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogReservationId(ISchedulerTask task)
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        [CLSCompliant(false)]
        public static void LogBeginConstraint(Thread foo, bool ok, ulong start, ulong stop)
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogEndConstraint(TimeSpan diff)
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogTimeJump()
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogSleepAdd()
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogDeleteActivity()
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogEndConstraint()
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogResolveConstraint()
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogReschedule()
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogReservedActivity(int proxyCount)
        {
        }

        [System.Diagnostics.Conditional("DEBUG_SCHEDULER")]
        public static void LogRecurringCpuReservation()
        {
        }

        //
        //////////////////////////////////////////////////////////////////////////////

        [CLSCompliant(false)]
        public static void OnTimerInterrupt(IHalTimer timer)
        {
            // For now: We don't invoke the scheduler here, as the base
            // interrupt function will switch to the scheduler
            // thread context.  (Q: Should we do that here instead?)
            // Instead, for now just set the interrupted flag.
            // Also -- in the future instead we may be calling
            // scheduler.NextThread() and setting to that context.
            //DebugStub.Print("Timer Interrupt: {0} #\n", __arglist(kCurrentTime));
            Scheduler.TimerInterruptedFlag = true;
        }

        [CLSCompliant(false)]
        public static Thread GetResumedThread()
        {
            if (!CpuResource.Provider().ShouldReschedule() &&
                !IoSystem.DeferredSignalsPending()) {
                //Kernel.Waypoint(302);
                return Thread.CurrentThread;
            }
            else {
                if (Thread.CurrentThread != Thread.schedulingThread) {
                    //Kernel.Waypoint(303);
                    return Thread.schedulingThread;
                }
                else {
                    //Kernel.Waypoint(304);
                    return Thread.CurrentThread;
                }
            }
        }

        [CLSCompliant(false)]
        public bool GetNextThread(out Thread next)
        {
            //Kernel.Waypoint(3);

            // DrainDeferredSignals returns true if any signals were queued.
            IoSystem.DrainDeferredSignals();
            //Kernel.Waypoint(4);

            // XXX If there is an interrupt right here that adds
            // a deferred signals to the list it won't get
            // signalled until after the next thread runs.
            //
            // A potential fix is to loop here and make sure the flag is
            // clear before scheduling the next thread.

            //DebugStub.Print("Scheduler.GetNextThread()\n");
            //return (item != null) ? item.Thread : null;
            Debug.Assert(Thread.CurrentThread != null);
            Thread foo;
            //Kernel.Waypoint(5);
            bool halted = CpuResource.Provider().NextThread(out foo);
            next = (Thread)foo;
            yieldFlag = false;
            //DebugStub.Print("Exiting Scheduler.GetNextThread({0:x8},{1}\n",
            // __arglist(next.threadIndex, halted));
            //Kernel.Waypoint(6);
            return halted;
        }

        static bool notExiting = true;
        public static void StopSystem()
        {
            notExiting = false;
        }

        //TODO: Replace with a Processor Halt!
        void Halt()
        {
            // DebugStub.Print("Doing Processor hlt\n");
#if false
            //[SOSP-2005] Disable hlt so we can always use the cycle counter!
            Tracing.Log(Tracing.Debug, "Halting processor with hlt.");
            Processor.HaltUntilInterrupt();
#endif

            // Check for a debug break.
            if (DebugStub.PollForBreak()) {
                DebugStub.Print("Debugger breakin.\n");
                DebugStub.Break();
            }
        }

        [CLSCompliant(false)]
        public void Start(Processor rootProcessor)
        {
            // WARNING: Because the scheduler thread cannot go to sleep,
            // we must avoid any operation (such as memory allocation)
            // that might require the current thread to block.
#if DEBUG_SCHEDULER || !DEBUG_SCHEDULER
            Tracing.Log(Tracing.Audit, "Scheduler Started.");
#endif
            Thread.schedulingThread = Thread.CurrentThread;

            IHalTimer timer = rootProcessor.Timer;
            timer.SetNextInterrupt(timer.MaxInterruptInterval);

            Thread next;
#if DEBUG_SCHEDULER || !DEBUG_SCHEDULER
            Tracing.Log(Tracing.Audit, "Scheduler Loop Started.");
#endif

            while (notExiting) {
                bool halted = GetNextThread(out next);
                Debug.Assert(halted || next != null);
                if (!halted) {
#if DEBUG_SCHEDULER
                    Tracing.Log(Tracing.Audit, "Scheduling thread {0:x3}",
                                (UIntPtr)unchecked((uint)next.threadIndex));
#endif
                    next.Schedule();
                    if (next != null && next.IsStopping()) {
#if DEBUG_SCHEDULER
                        Tracing.Log(Tracing.Audit, "Cleaning up after thread {0:x3}",
                                    (UIntPtr)unchecked((uint)next.threadIndex));
#endif
                        next.Cleanup();
                    }
                }
                else {
                    if (notExiting) {
                        Kernel.Waypoint(99);
                        Halt();
                    }
                }
            }

#if DEBUG_SCHEDULER || !DEBUG_SCHEDULER
            Tracing.Log(Tracing.Audit, "Scheduler loop terminated.");
#endif
        }
    }
}
