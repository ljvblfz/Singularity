///////////////////////////////////////////////////////////////////////////////
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
#if DEBUG
#define SHOW_TWIDDLE
#else
//#define SHOW_TWIDDLE
#endif

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Singularity;
using Microsoft.Singularity.Hal;
using Microsoft.Singularity.Io;

namespace Microsoft.Singularity.Scheduling
{
    [NoCCtor]
    [CLSCompliant(false)]
    public class Scheduler
    {
        // Global
        [AccessedByRuntime("referenced from halidt.asm")]
        private static SpinLock dispatchLock;

        // List of per-processor idle threads.
        protected static ThreadQueue idleThreads;

        [NoHeapAllocation]
        public static void DispatchLock()
        {
            dispatchLock.Acquire();
        }

        [NoHeapAllocation]
        public static bool EnsureDispatchLock(int currentThreadIndex) {
            if (dispatchLock.IsHeldBy(currentThreadIndex)) {
                return false;
            } else {
                DispatchLock();
                return true;
            }
        }

        [NoHeapAllocation]
        public static void DispatchRelease()
        {
            dispatchLock.Release();
        }

        [Conditional("DEBUG")]
        [NoHeapAllocation]
        public static void AssertDispatchLockHeld()
        {
            VTable.Assert(Processor.InterruptsDisabled());
            VTable.Assert(dispatchLock.IsHeldBy(Thread.CurrentThread));
        }

        public static unsafe void Initialize()
        {
            // Scheduler visualization hack
            // we use unsafe access to minimize the scheduler overhead.
            screenMem = IoMemory.MapPhysicalMemory(0xb8000, 80*50*2, true, true);
            screenPos = 0;
            screenPtr = (ushort *)screenMem.VirtualAddress;

            // Create the idle threads queue
            idleThreads = new ThreadQueue();

            // Create the twiddle values.
            twiddles = new ushort[] {
                (ushort)(0xe000 | '|'),
                (ushort)(0xe000 | '/'),
                (ushort)(0xe000 | '-'),
                (ushort)(0xe000 | '\\')
            };
            tpos = 0;
        }

        public static void Finalize()
        {
        }

        ////////////////////////////////////////// Heads-up Scheduler Display.
        //
        private static IoMemory screenMem;
        private static int      screenPos;
        private static unsafe ushort * screenPtr;

        [Inline]
        [NoHeapAllocation]
        private static unsafe void DisplayThread(int id)
        {
#if SHOW_TWIDDLE
            screenPtr[screenPos] = (ushort)(0x2a00| ('@'+id));
            screenPos = (screenPos + 1) % 80;
#endif
        }

        private static ushort[] twiddles;

        private static int tpos;

        [Inline]
        [NoHeapAllocation]
        private static unsafe void Twiddle()
        {
#if SHOW_TWIDDLE
            screenPtr[screenPos] = twiddles[tpos];
            tpos = ++tpos & 3;
#endif
        }

        /////////////////////////////////////////////////// Logging Functions.
        //
        [NoHeapAllocation]
        public static void SelectingThread(Thread thread)
        {
            Scheduler.AssertDispatchLockHeld();
            Thread.CurrentThread.ActiveProcessor = null;

            if (thread != null) {
                int id = thread.GetThreadId();

                DisplayThread(id);
                thread.ActiveProcessor = Processor.CurrentProcessor;
#if DEBUG_DISPATCH
                DebugStub.Print("Thread {0:x8} Selecting {1:x8}\n",
                                __arglist(
                                    Kernel.AddressOf(Thread.CurrentThread),
                                    Kernel.AddressOf(thread)));
#endif // DEBUG_DISPATCH
                Tracing.Log(Tracing.Debug, "Selecting tid={0:x3}", (uint)id);

                VTable.Assert(!thread.schedulerEntry.Enqueued);
            }
            Twiddle();
        }

        [NoHeapAllocation]
        public static bool IsIdleThread(int threadIndex) {
            Thread thread = Thread.threadTable[threadIndex];
            return (thread != null &&
                    idleThreads.IsEnqueued(thread.schedulerEntry));
        }

        [Conditional("DEBUG_SCHEDULER")]
        [CLSCompliant(false)]
        public static void LogWakeThread(Thread thread)
        {
        }

        [Conditional("DEBUG_SCHEDULER")]
        public static void LogSchedulerLate()
        {
        }

        [Conditional("DEBUG_SCHEDULER")]
        public static void LogContextSwitch()
        {
        }

        [Conditional("DEBUG_SCHEDULER")]
        public static void LogTimeJump()
        {
        }

        [Conditional("DEBUG_SCHEDULER")]
        public static void LogSleepAdd()
        {
        }

        [Conditional("DEBUG_SCHEDULER")]
        public static void LogReschedule()
        {
        }

        /////////////////////////////////////////// Scheduling Event Handlers.
        //
        //  Each of these should be replaced by the scheduler mixin.
        //
        [CLSCompliant(false)]
        public static void OnThreadStateInitialize(Thread thread, bool constructorCalled)
        {
            // Only initialize thread-local state!  No locks held and interrupts on.
            DebugStub.Break();
        }

        [CLSCompliant(false)]
        public static void OnThreadStart(Thread thread)
        {
            AssertDispatchLockHeld();
            Debug.Assert(thread.freezeCount == 0);
            DebugStub.Break();
        }

        [CLSCompliant(false)]
        public static Thread OnThreadBlocked(Thread thread, SchedulerTime stop)
        {
            // Scheduler should return the target thread.
            AssertDispatchLockHeld();
            DebugStub.Break();
            return null;
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static void OnThreadUnblocked(Thread thread)
        {
            // The scheduler should add this thread to the runnable queue,
            // but should not perform a context switch yet.
            AssertDispatchLockHeld();
            DebugStub.Break();
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static Thread OnThreadYield(Thread thread)
        {
            AssertDispatchLockHeld();
            DebugStub.Break();
            return null;
        }

        [CLSCompliant(false)]
        public static Thread OnThreadStop(Thread thread)
        {
            AssertDispatchLockHeld();
            DebugStub.Break();
            return null;
        }

        [CLSCompliant(false)]
        public static void OnThreadFreezeIncrement(Thread thread)
        {
            // Increment freezeCount and unschedule the thread until
            // freezeCount==0.  (Used for Thread.Stop, Thread.Suspend,
            // and modifying a thread's garbage collection state)
            //
            // If the thread is already running on a processor,
            // the scheduler need not unschedule it immediately, but:
            //   - the thread must be unscheduled within a bounded time
            //     (e.g. a time slice)
            //   - once unscheduled, the thread must not be scheduled
            //     again until freezeCount==0.
            AssertDispatchLockHeld();
            DebugStub.Break();
        }

        [CLSCompliant(false)]
        public static void OnThreadFreezeDecrement(Thread thread)
        {
            // Decrement freezeCount.  If freezeCount reaches 0,
            // the thread may now be scheduled (if it is
            // not blocked).
            AssertDispatchLockHeld();
            DebugStub.Break();
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static Thread OnTimerInterrupt(Thread thread, SchedulerTime now)
        {
            AssertDispatchLockHeld();
            DebugStub.Break();
            return null;
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static Thread OnIoInterrupt(Thread thread)
        {
            AssertDispatchLockHeld();
            DebugStub.Break();
            return null;
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static void OnProcessorShutdown(Thread thread)
        {
            AssertDispatchLockHeld();
            DebugStub.Break();
        }
    }
}
