////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Thread.cs
//
//  Note:
//
//      The Thread class and the Scheduler interact through three mechanisms.
//
//      First, the synchronization operations acquire the Scheduler's dispatch
//      lock (via Scheduler.DispatchLock() and Scheduler.DispatchRelease()
//      to ensure that no two processors ever attempt to dispatch on the block
//      or release threads at exactly the same time.
//
//      Second, the Thread class notifies the Scheduler of important events
//      in the life of each thread.  These notifications are done via overrides
//      on the thread class.  The mixin overrides are:
//          Scheduler.OnThreadStateInitialize(): Thread has been created.
//          Scheduler.OnThreadStart():      Thread is ready to start.
//          Scheduler.OnThreadBlocked():    Thread just blocked on a handle.
//          Scheduler.OnThreadUnblocked():  Thread is now runnable.
//          Scheduler.OnThreadYield():      Thread yields processor.
//          Scheduler.OnThreadStop():       Thread is ready to stop.
//          Scheduler.OnThreadFreezeIncrement(): Freeze thread, incr count.
//          Scheduler.OnThreadFreezeDecrement(): Decrement count, if 0 then unfreeze
//
//      Third, the Scheduler calls Thread.Stopped() when it has finish with a
//      thread that is no longer runnable.
//

// #define DEBUG_SWITCH

namespace System.Threading
{
    using System.Threading;
    using System.Runtime.InteropServices;
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.GCs;
    using System.Collections;
    using System.Runtime.CompilerServices;
    using Microsoft.Singularity;
    using Microsoft.Singularity.Channels;
    using Microsoft.Singularity.Hal;
    using Microsoft.Singularity.Scheduling;
    using Microsoft.Singularity.Security;
    using Microsoft.Singularity.V1.Threads;
    using Microsoft.Singularity.X86;
    using Microsoft.Singularity.Memory;
    using Microsoft.Bartok.Runtime;

    [CLSCompliant(false)]
    public enum ThreadEvent : ushort
    {
        CreateIdle = 12,
        Create = 13,
        WaitAny = 30,
        WaitFail = 31,
        SwitchTo = 3,
        ThreadPackageInit = 10
    }

    //| <include path='docs/doc[@for="Thread"]/*' />
    [CCtorIsRunDuringStartup]
    [CLSCompliant(false)]
    [RequiredByBartok]
    public class Thread
    {
        // GC fields.
        [RequiredByBartok] // Thread-specific alloc heap
        internal SegregatedFreeList segregatedFreeList;
        [RequiredByBartok] // Thread-specific bump allocator
        internal BumpAllocator bumpAllocator;

        // MultiUseWord (object header) fields.
        internal UIntPtr externalMultiUseObjAllocListHead;
        internal UIntPtr externalMultiUseObjAllocListTail;

        // Scheduling fields.
        internal int processThreadIndex;
        internal int threadIndex;
        private ThreadStart threadStart;
        private ThreadState threadState; // acquire Process.processLock when changing unstarted->running

        private AutoResetEvent autoEvent;
        private bool gcEventBlocked; // Are we blocked on the gcEvent?
        private bool gcEventSignaled; // Has the gcEvent been signaled
        private ManualResetEvent joinEvent;
        internal Thread blockingCctorThread;
        internal ThreadLocalServiceRequest localServiceRequest;

        // Scheduler dispatching fields.
        [AccessedByRuntime("referenced from c++")]
        internal bool blocked;                          // Thread blocked on something.
        [AccessedByRuntime("referenced from c++")]
        private ThreadEntry[] blockedOn;                // WaitHandles blocked on (if any).
        [AccessedByRuntime("referenced from c++")]
        private int blockedOnCount;                     // Number of valid WaitHandles.
        [AccessedByRuntime("referenced from c++")]
        private SchedulerTime blockedUntil;             // Timeout of Wait.
        public SchedulerTime BlockedUntil
        {
            [NoHeapAllocation]
            get { return blockedUntil; }
        }
        private volatile int unblockedBy;               // WaitHandle signaled in unblock.
        internal int freezeCount;                       // >0 prevents thread from being scheduled.

        [AccessedByRuntime("referenced from c++")]
        public ThreadEntry schedulerEntry;              // Entry for some scheduler queue.
        public Processor ActiveProcessor;               // Processor running this thread (null if not running).
        public Processor AffinityProcessor;             // Soft affinity.
        public Processor LockedProcessor;               // non-null if thread dedicated.

        // Singularity specific fields
        [AccessedByRuntime("referenced from c++")]
        internal ThreadContext context;

        [AccessedByRuntime("referenced from c++")]
        internal Process process;
        internal ThreadHandle threadHandle;
        internal UIntPtr threadLocalValue;

        // Most recently thrown exception object that the thread
        // did not catch at all (i.e. that propagated to the bottom
        // of the stack without encountering an appropriate catch clause).
        internal Exception lastUncaughtException;

        // Monitor fields.
        // Remove these (& Monitor) as soon as stack is out of kernel.
        internal Thread nextThread; // Link for linked lists of threads

        private Object exceptionStateInfo;            // Exception info latched to the thread on a thread abort

        // Bartok specific fields
        [RequiredByBartok]
        internal TryAllManager tryAllManager;

        private static long totalArrayAllocations;
        private static long totalArrayBytesAllocated;
        private static long totalBytes;
        private static long totalStructBytesAllocated;

        internal static Thread[] threadTable;
        private static SpinLock threadTableLock;

        private static LocalDataStore localDataStore;

        internal static Thread initialThread;

        private static SpinLock threadStopLock;
        private static ProcessStopException processStopException;

#if PAGING
        // For use when we temporarily switch to a different domain
        private ProtectionDomain tempDomain;
#endif

        // This is used by the Bartok backend. When Bartok try to generate
        // callback stub for delegate, it checks to see if it is
        // ThreadProc, if it is not, Bartok adds leaveGCSafeState and
        // enterGCSafeState around the delegate call.
        [RequiredByBartok]
        private unsafe delegate uint ThreadProc(void *param);

        //////////////////////////////////////////////////////////////////////
        // This manager is responsible for storing the global data that is
        // shared amongst all the thread local stores.
        //
        static private LocalDataStoreMgr m_LocalDataStoreMgr;

        //////////////////////////////////////////////////////////////////////
        // Creates a new Thread object which will begin execution at
        // start.ThreadStart on a new thread when the Start method is called.
        //
        // Exceptions: ArgumentNullException if start == null.
        //
        //| <include path='docs/doc[@for="Thread.Thread"]/*' />
        internal const int maxThreads = 1024; // Must be power of 2 >= 64
        private static int threadIndexGenerator;

        private Thread(Process process)
        {
            this.processThreadIndex = -1;
            this.threadIndex = -1;
            this.threadState = ThreadState.Unstarted;
            this.SetKernelMode();
            this.process = process;

            context.threadIndex = unchecked((ushort)-1);
            context.processThreadIndex = unchecked((ushort)-1);
            context.processId = unchecked((ushort)-1);
            Transitions.InitializeStatusWord(ref context);

            // Allocate the kernel objects needed by the thread.
            autoEvent = new AutoResetEvent(false);
            joinEvent = new ManualResetEvent(false);
            localServiceRequest = new ThreadLocalServiceRequest();
            schedulerEntry = new ThreadEntry(this);
            this.GetWaitEntries(1); // Cache allows wait without allocation

            // Try to put the thread in the thread table.
            bool iflag = Processor.DisableInterrupts();
            try {
                threadTableLock.Acquire(CurrentThread);
                try {
                    for (int i = 0; i < threadTable.Length; i++) {
                        int index = (threadIndexGenerator + i) % threadTable.Length;
                        if (threadTable[index] == null) {
                            threadTable[index] = this;
                            this.threadIndex = index;
                            threadIndexGenerator = index + 1;
                            // NB: We call this once, subsequently the GC visitor calls it.
                            context.UpdateAfterGC(this);
                            break;
                        }
                    }
                }
                finally {
                    threadTableLock.Release(CurrentThread);
                }

                // Save the TID and PID into the thread context.
                context.threadIndex = unchecked((ushort)(threadIndex));
                Transitions.InitializeStatusWord(ref context);
                if (process != null)
                {
                    context.processId = unchecked((ushort)(process.ProcessId));
                }
            }
            finally {
                Processor.RestoreInterrupts(iflag);
            }

            // Allocate the thread's stack.
            context.stackBegin = 0;
            context.stackLimit = 0;
        }

        // Constructor for processor idle threads.
        protected Thread(bool idle)
            : this(Process.idleProcess)
        {
            UIntPtr stackSegment = Stacks.GetInitialStackSegment(ref context);
            DebugStub.Assert(stackSegment != UIntPtr.Zero);
#if PAGING
            context.InitializeIdle(threadIndex, stackSegment,
                                   unchecked((uint)Process.kernelProcess.Domain.AddressSpace.PdptPage.Value));
#else
            context.InitializeIdle(threadIndex, stackSegment, 0);
#endif
            Monitoring.Log(Monitoring.Provider.Thread,
                           (ushort)ThreadEvent.CreateIdle, 0,
                           (uint)threadIndex, 0, 0, 0, 0);
        }

        // Constructor for all other threads.
        protected Thread(Process process, ThreadStart start)
            : this(process)
        {
            if (start == null) {
                throw new ArgumentNullException("start");
            }

            this.threadStart = start;

#if PAGING
            context.abiStackHead = Stacks.GetStackSegment(0, ref context);
            context.abiStackBegin = context.stackBegin;
            context.abiStackLimit = context.stackLimit;
            context.stackBegin = 0;
            context.stackLimit = 0;
#endif
            UIntPtr stackSegment = Stacks.GetInitialStackSegment(ref context);
            DebugStub.Assert(stackSegment != UIntPtr.Zero);
#if PAGING
            context.Initialize(threadIndex, stackSegment,
                               unchecked((uint)(Process.Domain.AddressSpace.PdptPage.Value)));
#else
            context.Initialize(threadIndex, stackSegment, 0);
#endif
            Monitoring.Log(Monitoring.Provider.Thread,
                           (ushort)ThreadEvent.Create, 0,
                           (uint)threadIndex, 0, 0, 0, 0);
        }

        // To create a new idle thread.
        public static Thread CreateIdleThread(Processor processor)
        {
            // Allocate the thread.
            Thread idle = new Thread(true);

            if (idle.threadIndex < 0) {
                Tracing.Log(Tracing.Warning, "Thread table is full.");
                DebugStub.Break();
                return null;
            }

            //MemoryBarrier();?

            DebugStub.WriteLine("CreateIdleThread tid={0:x3}, proc={1:x3}",
                                __arglist(idle.threadIndex,
                                          processor.processorIndex));
            idle.LockedProcessor = processor;
            return idle;
        }

        // Entry point for kernel to create new process threads.
        public static Thread CreateThread(Process process,
                                          ThreadStart start)
        {
            if (process == null) {
                process = CurrentProcess;
            }

            // Allocate the thread.
            Thread thread = new Thread(process, start);

            if (thread.threadIndex < 0) {
                Tracing.Log(Tracing.Warning, "Thread table is full.");
                DebugStub.Break();
                return null;
            }

            ThreadHandle handle;

            process.AddThread(thread, out handle);

            //MemoryBarrier();?

            // Tell the scheduler to initialize the thread.
            Scheduler.OnThreadStateInitialize(thread, true);

            PerfCounters.IncrementThreadsCreated();

            return thread;
        }

        //////////////////////////////////////////////////////////////////////
        // Spawns off a new thread which will begin executing at the
        // ThreadStart delegate passed in the constructor. Once the thread is
        // dead, it cannot be restarted with another call to Start.
        //
        // Exceptions: ThreadStateException if the thread has already been started.
        //
        //| <include path='docs/doc[@for="Thread.Start"]/*' />
        public void Start()
        {
            process.StartThread(ref threadState);
            StartRunningThread();
        }

        // precondition: process.processLock held
        internal void SetMainThreadRunning()
        {
            VTable.Assert(threadState == ThreadState.Unstarted);
            process.StartMainThread(ref threadState);
        }

        internal void StartRunningThread()
        {
            VTable.Assert(threadState == ThreadState.Running);

            // Tell the GC that we have created the thread
            GC.NewThreadNotification(this, false);

            // Tell the scheduler to start the thread.
            bool iflag = Processor.DisableInterrupts();
            try {
                Scheduler.DispatchLock();
                try {
                    //Kernel.Waypoint(1);
                    Scheduler.OnThreadStart(this);
                }
                finally {
                    Scheduler.DispatchRelease();
                }
            }
            finally {
                //Kernel.Waypoint(114);
                Processor.RestoreInterrupts(iflag);
            }
        }


        // ThreadContext.InitializeIdle sets ThreadIdleStub as the first instruction to be
        // executed in a new thread context.
        [AccessedByRuntime("referenced from c++")]
        private static unsafe void ThreadIdleStub(int index)
        {
            for (;;) {
                // Check for a debug break.
                // Tell the scheduler to start the thread.
                bool iflag = Processor.DisableInterrupts();
                try {
                    Processor.NextSampleIsIdle();
                    if (DebugStub.PollForBreak()) {
                        DebugStub.Print("Debugger breakin.\n");
                        DebugStub.Break();
                    }
                }
                finally {
                    //Kernel.Waypoint(114);
                    Processor.RestoreInterrupts(iflag);
                }
                Processor.HaltUntilInterrupt();
            }
        }

        // ThreadContext.Initialize sets ThreadStub as the first instruction to be
        // executed in a new thread context.
        [AccessedByRuntime("referenced from c++")]
        private static unsafe void ThreadStub(int threadIndex)
        {
            Thread currentThread = threadTable[threadIndex];

#if PAGING
            // Give our Protection Domain a chance to set up
            // if we're first in here. Run this before anything
            // else!
            currentThread.process.Domain.InitHook();
#endif

            Transitions.ThreadStart();
            GC.ThreadStartNotification(threadIndex);
            Tracing.Log(Tracing.Trace, "ThreadStub() entered");
            ThreadStart startFun = currentThread.threadStart;

            Processor.InitFpu();

            try {
                startFun();
            }
            catch (ProcessStopException) {
                // Ok, exit thread without failure.
            }
            catch (Exception e) {
                // Not ok, fail.
                Tracing.Log(Tracing.Notice,
                            "Thread failed with exception {0}.{1}",
                            e.GetType().Namespace, e.GetType().Name);
                Tracing.Log(Tracing.Trace, "Exception message was {0}",
                            e.Message);
                DebugStub.Assert(e == null,
                                 "Thread {0} failed w/ exception {1}.{2}: {3}",
                                 __arglist(threadIndex,
                                           e.GetType().Namespace,
                                           e.GetType().Name,
                                           e.Message));
            }

            Tracing.Log(Tracing.Trace, "{0:x} ThreadStub() stopping",
                        Kernel.AddressOf(currentThread));

            currentThread.threadState = ThreadState.Stopping;
            currentThread.joinEvent.Set();

            // The scheduler takes care of exiting MutatorState, so we
            // don't need the following call.
            // Transitions.ThreadEnd(threadIndex);

            bool iflag = Processor.DisableInterrupts();
            try {
                Thread target = null;

                // Block the ServiceStopped method until we acquire DispatchLock
                // (but don't acquire DispatchLock yet, because
                // CurrentThreadStopped would try to double-acquire it).
                threadStopLock.Acquire();

                // Tell service thread to call currentThread.ServiceStopped()
                ThreadLocalServiceRequest.CurrentThreadStopped();

                Scheduler.DispatchLock();
                try {
                    threadStopLock.Release();
                    Kernel.Waypoint(1);

                    // We change to the Stopped state here so that
                    // Process.ServiceSuspend can stop waiting for us.
                    // (We can't wait for the service thread to
                    // set the Stopped state, because if the
                    // service thread is running Process.ServiceSuspend
                    // it could deadlock.)
                    currentThread.threadState = ThreadState.Stopped;
                    target = Scheduler.OnThreadStop(currentThread);
                    Scheduler.SelectingThread(target);
                }
                catch(Exception e) {
                    Scheduler.DispatchRelease();
                    throw e;
                }

                if (target == null) {
                    target = Processor.CurrentProcessor.IdleThread;
                }

                target.SwitchTo();
            }
            finally {
                //Kernel.Waypoint(114);
                Processor.RestoreInterrupts(iflag);
            }
            DebugStub.Break();
        }

        // ServiceStopped is called by the service thread when the thread is stopped.
        internal void ServiceStopped()
        {
            // Make sure ThreadStub has gotten a chance to exit before
            // we deallocate its stack:
            bool iflag = Processor.DisableInterrupts();
            try {
                threadStopLock.Acquire();
                Scheduler.DispatchLock();
                Scheduler.DispatchRelease();
                threadStopLock.Release();
            } finally {
                Processor.RestoreInterrupts(iflag);
            }

            VTable.Assert(threadState == ThreadState.Stopped);
            VTable.Assert(threadIndex > 0);
            VTable.Deny(Transitions.InMutatorState(threadIndex));

            GC.DeadThreadNotification(this);

#if !PAGING
            while (context.stackBegin != 0) {
                Stacks.ReturnStackSegment(ref context);
            }
#else
            int count = 0;
            while (context.stackBegin != 0) {
                // HACK: if the thread stops abruptly, the stack
                // may contain the abi segment
                if (context.stackBegin == context.abiStackBegin) {
                    context.abiStackBegin = 0;
                    context.abiStackLimit = 0;
                }

                Stacks.ReturnStackSegment(ref context);
                count++;
            }
//            VTable.Assert(count == 1); // due to unimplemented exception handling code, count may be >1
#endif

            VTable.Assert(context.stackLimit == 0);
            VTable.Assert(context.stackBegin == 0);

#if PAGING
            // See HACK above for why abiStackBegin may be 0
            if (context.abiStackBegin != 0) {
                context.stackLimit = context.abiStackLimit;
                context.stackBegin = context.abiStackBegin;
                Stacks.ReturnStackSegment(ref context);
            }
#endif

            Thread currentThread = Thread.CurrentThread;

            iflag = Processor.DisableInterrupts();
            try {
                threadTableLock.Acquire(currentThread);
                try {
                    threadTable[threadIndex] = null;
                    Transitions.DeadThreadNotification(threadIndex);
                } finally {
                    threadTableLock.Release(currentThread);
                }
            } finally {
                Processor.RestoreInterrupts(iflag);
            }

            if (process != null) {
                process.ServiceOnThreadStop(this);
            }
        }

        //////////////////////////////////////////////////////////////////////
        // Returns true if the thread has been started and is not dead.
        //
        //| <include path='docs/doc[@for="Thread.IsAlive"]/*' />
        public bool IsAlive {
            get { return (threadState == ThreadState.Running); }
        }

#if false
        // Returns a TimeSpan target for timeout.
        static internal SchedulerTime GetTimeoutTarget(TimeSpan delay)
        {
            if (delay == TimeSpan.Infinite) {
#if DEBUG_SLEEP
                DebugStub.Print("  GetTimeTarget(Infinite) = {0}\n",
                                __arglist(SchedulerTime.MaxValue.Ticks));
#endif
                return SchedulerTime.MaxValue;
            }
            else if (delay < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException("delay", "ArgumentOutOfRange_AddTimeout");
            }
            SchedulerTime now = SchedulerTime.Now;
#if DEBUG_SLEEP
            DebugStub.Print("  GetTimeTarget({0}) = {1:d12} + {2:d12} => {3:d12}\n",
                            __arglist(
                                delay.ToString(),
                                now.Ticks,
                                delay.Ticks,
                                (now + delay).Ticks));
#endif
            return now + delay;
        }
#endif

        [Inline]
        static internal bool TimeoutTargetPassed(SchedulerTime target)
        {
            return SchedulerTime.Now >= target;
        }

        //////////////////////////////////////////////////////////////////////
        // Waits for the thread to die.
        //
        // Exceptions: ThreadStateException if the thread has not been started yet.
        //
        //| <include path='docs/doc[@for="Thread.Join"]/*' />
        public void Join()
        {
            Join(SchedulerTime.MaxValue);
        }

        //////////////////////////////////////////////////////////////////////
        // Waits for the thread to die or for timeout milliseconds to elapse.
        // Returns true if the thread died, or false if the wait timed out.
        //
        // Exceptions: ArgumentException if timeout < 0.
        //             ThreadStateException if the thread has not been started.
        //
        //| <include path='docs/doc[@for="Thread.Join2"]/*' />
        public bool Join(TimeSpan timeout)
        {
            if (threadState == ThreadState.Unstarted) {
                throw new ThreadStateException();
            }
            else if (threadState == ThreadState.Stopped) {
                return true;
            }
            return joinEvent.WaitOne(timeout);
        }

        public bool Join(SchedulerTime timeout)
        {
            if (threadState == ThreadState.Unstarted) {
                throw new ThreadStateException();
            }
            else if (threadState == ThreadState.Stopped) {
                return true;
            }
            return joinEvent.WaitOne(timeout);
        }

        //////////////////////////////////////////////////////////////////////
        // Suspends the current thread for timeout milliseconds. If timeout
        // == 0, forces the thread to give up the remainder of its timeslice.
        //
        // Exceptions: ArgumentException if timeout < 0.
        //
        public static void Sleep(int milliseconds)
        {
            Sleep(TimeSpan.FromMilliseconds(milliseconds));
        }

        //| <include path='docs/doc[@for="Thread.Sleep"]/*' />
        public static void Sleep(SchedulerTime stop)
        {
            Tracing.Log(Tracing.Audit, "Sleep until stop");
            Thread.CurrentThread.WaitAny(null, 0, stop);
            Tracing.Log(Tracing.Audit, "Sleep until stop finished");
        }

        //| <include path='docs/doc[@for="Thread.Sleep"]/*' />
        public static void Sleep(TimeSpan timeout)
        {
            Tracing.Log(Tracing.Audit, "Sleep until time");
            SchedulerTime stop = SchedulerTime.Now + timeout;
            Thread.CurrentThread.WaitAny(null, 0, stop);
            Tracing.Log(Tracing.Audit, "Sleep until time finished");
        }

        //////////////////////////////////////////////////////////////////////
        // Wait for a length of time proportional to 'iterations'.  Each
        // iteration is should only take a few machine instructions.  Calling
        // this method is preferable to coding an explicit busy loop because the
        // hardware can be informed that it is busy waiting.
        //
        //| <include path='docs/doc[@for="Thread.SpinWait"]/*' />
        [NoHeapAllocation]
        public static void SpinWait(int iterations)
        {
            for (int i = iterations; i > 0; i--) {
                // Ensure that the optimizer doesn't remove this
                NativeNoOp();
            }
        }

        [Intrinsic]
        [NoHeapAllocation]
        public static extern void NativeNoOp();

        internal static int GetCurrentProcessIndex() {
            return Thread.CurrentProcess.ProcessId;
        }

        internal bool WaitForMonitor(SchedulerTime stop)
        {
            return autoEvent.WaitOne(stop);
        }

        internal bool WaitForEvent(SchedulerTime stop)
        {
            return autoEvent.WaitOne(stop);
        }

        internal bool WaitForEvent(TimeSpan timeout)
        {
            return autoEvent.WaitOne(timeout);
        }

        internal static void WaitForGCEvent(int currentThreadIndex)
        {
            Tracing.Log(Tracing.Audit, "WaitForGCEvent({0})",
                        (UIntPtr) currentThreadIndex);
            VTable.Deny(Scheduler.IsIdleThread(currentThreadIndex));
            bool iflag = Processor.DisableInterrupts();
            try {
                bool didLock =
                    Scheduler.EnsureDispatchLock(currentThreadIndex);
                Thread target = null;
                Thread currentThread = threadTable[currentThreadIndex];
                try {
                    if (currentThread.gcEventSignaled) {
                        // Reset the flag, fall out and keep running.
                        currentThread.gcEventSignaled = false;
                        if (didLock) {
                            Scheduler.DispatchRelease();
                        }
                        return;
                    }
                    currentThread.gcEventBlocked = true;
                    target = Scheduler.OnThreadBlocked(currentThread,
                                                       SchedulerTime.MaxValue);
                    if (target == null) {
                        target = Processor.CurrentProcessor.IdleThread;
                    } else {
                        // This should not be called passing the idle thread
                        Scheduler.SelectingThread(target);
                    }
                } catch {
                    if (didLock) {
                        Scheduler.DispatchRelease();
                    }
                    throw;
                }
                Processor.CurrentProcessor.NumContextSwitches++;
                // This has the side effect of releasing the dispatcher lock.
                Processor.SwitchToThreadContextNoGC(ref target.context);
            } finally {
                Processor.RestoreInterrupts(iflag);
            }
        }

        internal void SignalEvent()
        {
            autoEvent.Set();
        }

        internal void SignalMonitor()
        {
            autoEvent.Set();
        }

        [Inline]
        internal static void SignalGCEvent(int threadIndex) {
            SignalGCEvent(Thread.GetCurrentThreadIndex(), threadIndex);
        }

        internal static void SignalGCEvent(int currentThreadIndex,
                                           int threadIndex)
        {
            Tracing.Log(Tracing.Audit, "SignalGCEvent({0})",
                        (UIntPtr) threadIndex);
            VTable.Deny(Scheduler.IsIdleThread(threadIndex));
            bool iflag = Processor.DisableInterrupts();
            try {
                bool didLock =
                    Scheduler.EnsureDispatchLock(currentThreadIndex);
                try {
                    Thread targetThread = threadTable[threadIndex];
                    if (targetThread == null) {
                        // There is nothing to signal, so just keep going.
                    } else if (targetThread.gcEventBlocked) {
                        targetThread.gcEventBlocked = false;
                        Scheduler.OnThreadUnblocked(targetThread);
                    } else {
                        // Nobody was waiting for the event.
                        targetThread.gcEventSignaled = true;
                    }
                } finally {
                    if (didLock) {
                        Scheduler.DispatchRelease();
                    }
                }
            } finally {
                Processor.RestoreInterrupts(iflag);
            }
        }

        //| <include path='docs/doc[@for="Thread.CurrentThread"]/*' />
        public static extern Thread CurrentThread
        {
            [NoStackLinkCheck]
            [NoHeapAllocation]
            [Intrinsic]
            get;
        }

        public static Process CurrentProcess
        {
            [NoStackLinkCheck]
            [NoHeapAllocation]
            get {
                return Processor.GetCurrentThread().process;
            }
        }

        public Process Process
        {
            [NoHeapAllocation]
            get { return process; }
        }

        public ThreadHandle Handle
        {
            [NoHeapAllocation]
            get { return threadHandle; }
        }

        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoHeapAllocation]
        private static Thread GetCurrentThreadNative()
        {
            return Processor.GetCurrentThread();
        }

        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoHeapAllocation]
        public static int GetCurrentThreadIndex()
        {
            return Processor.GetCurrentThread().threadIndex;
        }

        [NoHeapAllocation]
        public static UIntPtr GetThreadLocalValue()
        {
            return Processor.GetCurrentThread().threadLocalValue;
        }

        [NoHeapAllocation]
        public static void SetThreadLocalValue(UIntPtr value)
        {
            Processor.GetCurrentThread().threadLocalValue = value;
        }

        //////////////////////////////////////////////////////////////////////
        // Return the thread state as a consistent set of bits.  This is more
        // general then IsAlive or IsBackground.
        //
        //| <include path='docs/doc[@for="Thread.ThreadState"]/*' />
        public ThreadState ThreadState
        {
            [NoHeapAllocation]
            get { return threadState; }
        }

        [NoHeapAllocation]
        public int GetThreadId()
        {
            return threadIndex;
        }

        // Return true if the thread is in kernel mode, false if the
        // thread is in process mode.
        // Note that by the time this method returns, the thread might
        // have already switched to a different mode; in other words,
        // don't rely on this result of this method being up-to-date unless
        // the thread is suspended or blocked.
        [NoHeapAllocation]
        public unsafe bool IsInKernelMode()
        {
            return context.IsInKernelMode();
        }

        [NoHeapAllocation]
        internal unsafe void SetKernelMode()
        {
            context.SetKernelMode();
        }

        [NoHeapAllocation]
        internal unsafe void SetProcessMode()
        {
            context.SetProcessMode();
        }

        // Briefly suspend a thread in order to modify its state.
        // When Freeze() returns, the thread is guaranteed to:
        //   - be unscheduled (not running on any processor)
        //   - not to be inside a DisableInterrupts block
        //     (unless that block voluntarily context switches)
        //   - not to hold the dispatch lock (because
        //     CurrentThread holds the dispatch lock)
        // These guarantees hold only until the dispatch lock
        // is released.
        //
        // No other guarantees are made about the thread state; in particular,
        // the thread could be in the middle of a kernel operation.  (This
        // distinguishes Freeze from Suspend.)
        //
        // A thread should not try to freeze itself
        // (this would deadlock, as can many other uses of Freeze).
        //
        // precondition: interrupts enabled
        // precondition: dispatch lock not acquired
        //   (so that other processor schedulers can run)
        // postcondition: interrupts disabled
        // postcondition: dispatch lock acquired
        // postcondition: ActiveProcessor == null
        // postcondition: freezeCount > 0
        //
        // Example:
        //   t.Freeze();
        //   ...(do some stuff, without releasing dispatch lock)...
        //   Scheduler.DispatchRelease();
        //   Processor.RestoreInterrupts(true);
        internal void Freeze()
        {
            Debug.Assert(this != CurrentThread);
            bool iflag = Processor.DisableInterrupts();
            Debug.Assert(iflag);
            try {
                Scheduler.DispatchLock();
                try {
                    switch (threadState) {
                        case ThreadState.Unstarted:
                        case ThreadState.Stopped:
                        case ThreadState.Suspended:
                            break;
                        case ThreadState.Running:
                        case ThreadState.Stopping:
                            if (ActiveProcessor == null) {
                                break;
                            }
                            Scheduler.OnThreadFreezeIncrement(this);
                            WaitUntilInactive();
                            // (Try to be courteous to the scheduler -- don't
                            // ask it to resume a stopped thread.)
                            if (threadState != ThreadState.Stopped) {
                                Scheduler.OnThreadFreezeDecrement(this);
                            }
                            break;
                        default:
                            Debug.Assert(false, "unreachable default case");
                            return;
                    }
                }
                catch(Exception e) {
                    Scheduler.DispatchRelease();
                    throw e;
                }
            }
            catch(Exception e) {
                Processor.RestoreInterrupts(iflag);
                throw e;
            }
            Debug.Assert(ActiveProcessor == null);
            Scheduler.AssertDispatchLockHeld();
        }

        internal void Unfreeze()
        {
            Scheduler.DispatchRelease();
            Processor.RestoreInterrupts(true);
        }

        // precondition: interrupts disabled with iflag==true
        // precondition: dispatch lock acquired
        // invariant: freezeCount > 0
        // postcondition: interrupts disabled with iflag==true
        // postcondition: dispatch lock acquired
        // postcondition: ActiveProcessor == null
        private void WaitUntilInactive()
        {
            Scheduler.AssertDispatchLockHeld();
            Debug.Assert(this != CurrentThread);
            Debug.Assert(freezeCount > 0);
            if (ActiveProcessor != null) {
                // TODO: send interrupt to ActiveProcessor

                // spin for exponentially backed off delay
                int count = 31;
                while (ActiveProcessor != null) {
                    Scheduler.DispatchRelease();
                    Processor.RestoreInterrupts(true);
                    Thread.SpinWait(count);
                    count = (count == 0x7ffffffu) ? count : count + count + 1;
                    bool iflag = Processor.DisableInterrupts();
                    Debug.Assert(iflag);
                    Scheduler.DispatchLock();
                }
            }
        }

        // Do not call Thread.Suspend unless you are suspending all the
        // threads in a process.  This should only be called from the
        // kernel service thread.
        //
        // Return value:
        //   true means the thread is now suspended, unstarted, or stopped.
        //   false means the thread must be allowed to run a little longer,
        //     so try calling Suspend later.
        //
        // precondition: interrupts enabled
        // precondition: dispatch lock not acquired
        // postcondition: interrupts enabled
        // postcondition: dispatch lock not acquired
        internal bool Suspend(bool aboutToStop)
        {
            /*
            case (kernel,  unblocked, running):   return false;
            case (kernel,  unblocked, stopping):  return false;
            case (process, unblocked, running):   DoSuspend(); return true;
            case (kernel,  blocked,   running):   DoSuspend(); return true;
            case (kernel,  unblocked, unstarted): return true;
            case (_,       _,         suspended): return true;
            case (kernel,  unblocked, stopped):   return true;
            case _: error
            // Also: in the (kernel, blocked, running) case, if
            // aboutToStop==true, call WaitDone().
            */
            Freeze();
            try {
                if (threadState == ThreadState.Suspended) {
                    return true;
                }
                else if (IsInKernelMode()) { // in kernel mode, not suspended
                    if (blocked) {
                        if (threadState == ThreadState.Running) {
                            DoSuspend();
                            if (aboutToStop) {
                                WaitDone(null);
                            }
                            return true;
                        }
                        else {
                            Debug.Assert(false, "unexpected thread state");
                            return false;
                        }
                    }
                    else { // in kernel mode, unblocked, not suspended
                        switch (threadState) {
                            case ThreadState.Running:
                            case ThreadState.Stopping:
                                // Let the thread run a while longer, but
                                // let it know that eventually, it should
                                // suspend:
                                // MULTIPROCESSOR NOTE: halforgc.asm reads
                                // suspendAlert with no explicit
                                // synchronization.  We don't need the
                                // read to be consistent with the
                                // following write immediately, but it
                                // must become consistent eventually.
                                context.suspendAlert = true;
                                return false;
                            case ThreadState.Unstarted:
                            case ThreadState.Stopped:
                                // Note: now that process.state==Suspending[Recursive], a barrier
                                // prevents starting threads, an Unstarted thread stays
                                // Unstarted.
                                return true;
                            case ThreadState.Suspended:
                                Debug.Assert(false, "unexpected thread state");
                                return false;
                            default:
                                Debug.Assert(false, "unreachable default case");
                                return false;
                        }
                    }
                }
                else { // in process mode, not suspended
                    if (!blocked && threadState == ThreadState.Running) {
                        DoSuspend();
                        return true;
                    }
                    else {
                        Debug.Assert(false, "unexpected thread state");
                        return false;
                    }
                }
            }
            finally {
                Scheduler.DispatchRelease();
                Processor.RestoreInterrupts(true);
            }
        }

        private void DoSuspend()
        {
            threadState = ThreadState.Suspended;
            Scheduler.OnThreadFreezeIncrement(this);
        }

        // Do not call Thread.Resume unless you are resuming all the
        // threads in a process.  This should only be called from the
        // kernel service thread.
        internal void Resume()
        {
            bool iflag = Processor.DisableInterrupts();
            try {
                Scheduler.DispatchLock();
                try {
                    if (threadState == ThreadState.Suspended) {
                        threadState = ThreadState.Running;
                        Scheduler.OnThreadFreezeDecrement(this);
                    }
                }
                finally {
                    Scheduler.DispatchRelease();
                }
            }
            finally {
                Processor.RestoreInterrupts(iflag);
            }
        }

        // Do not call Thread.Stop unless you are stopping all the
        // threads in a process.  This should only be called from the
        // kernel service thread.
        //
        // precondition: interrupts enabled
        // precondition: dispatch lock not acquired
        // postcondition: interrupts enabled
        // postcondition: dispatch lock not acquired
        internal void Stop()
        {
            /*
            case (kernel,  unblocked, unstarted): return;
            case (kernel,  unblocked, stopped): return;
            case (kernel,  _,         suspended):
                if (blocked) WaitDone(null);
                t.throw ProcessStopException
                threadState = ThreadState.Running;
                Scheduler.OnThreadFreezeDecrement(this);
                return;
            case (process, unblocked, suspended):
                t.(pop frames and throw ProcessStopException)
                threadState = ThreadState.Running;
                Scheduler.OnThreadFreezeDecrement(this);
                return;
            case _: error
            */
            Freeze();
            try {
                bool kernelMode = IsInKernelMode();
                if (threadState == ThreadState.Suspended) {
                    if (kernelMode) {
                        if (blocked) {
                            WaitDone(null);
                        }
                        // context.eip = __throwBeyondMarker;
                        // context.eax = context.stackMarkers; // kernel->kernel marker;
                        // context.ecx = processStopException;
                        setStopContext(this, processStopException);
                    }
                    else if (!kernelMode && !blocked) {
                        // context.eip = __throwBeyondMarker;
                        // context.eax = context.stackMarkers; // kernel->process marker;
                        // context.ecx = processStopException;
                        setStopContext(this, processStopException);
                    }
                    else {
                        Debug.Assert(false, "unexpected thread state");
                    }
                    Debug.Assert(!blocked);
                    threadState = ThreadState.Running;
                    Scheduler.OnThreadFreezeDecrement(this);
                }
                else if (
                    kernelMode && !blocked &&
                    (      threadState == ThreadState.Stopped
                        || threadState == ThreadState.Unstarted)) {
                    // nothing needs to happen here.
                }
                else {
                    Debug.Assert(false, "unexpected thread state");
                }
            }
            finally {
                Scheduler.DispatchRelease();
                Processor.RestoreInterrupts(true);
            }
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        [StackBound(4)]
        private static extern void setStopContext(Thread t, Exception exn);

        //////////////////////////////////////////////////////////////////////
        // Allocates an un-named data slot. The slot is allocated on ALL the
        // threads.
        //| <include path='docs/doc[@for="Thread.AllocateDataSlot"]/*' />
        public static LocalDataStoreSlot AllocateDataSlot()
        {
            return m_LocalDataStoreMgr.AllocateDataSlot();
        }

        //////////////////////////////////////////////////////////////////////
        // Allocates a named data slot. The slot is allocated on ALL the
        // threads.  Named data slots are "public" and can be manipulated by
        // anyone.
        //| <include path='docs/doc[@for="Thread.AllocateNamedDataSlot"]/*' />
        public static LocalDataStoreSlot AllocateNamedDataSlot(String name)
        {
            return m_LocalDataStoreMgr.AllocateNamedDataSlot(name);
        }

        //////////////////////////////////////////////////////////////////////
        // Looks up a named data slot. If the name has not been used, a new
        // slot is allocated.  Named data slots are "public" and can be
        // manipulated by anyone.
        //| <include path='docs/doc[@for="Thread.GetNamedDataSlot"]/*' />
        public static LocalDataStoreSlot GetNamedDataSlot(String name)
        {
            return m_LocalDataStoreMgr.GetNamedDataSlot(name);
        }

        //////////////////////////////////////////////////////////////////////
        // Frees a named data slot. The slot is allocated on ALL the
        // threads.  Named data slots are "public" and can be manipulated by
        // anyone.
        //| <include path='docs/doc[@for="Thread.FreeNamedDataSlot"]/*' />
        public static void FreeNamedDataSlot(String name)
        {
            m_LocalDataStoreMgr.FreeNamedDataSlot(name);
        }

        //////////////////////////////////////////////////////////////////////
        // Retrieves the value from the specified slot on the current thread.
        //| <include path='docs/doc[@for="Thread.GetData"]/*' />
        public static Object GetData(LocalDataStoreSlot slot)
        {
            m_LocalDataStoreMgr.ValidateSlot(slot);

            if (localDataStore != null) {
                return localDataStore.GetData(slot);
            }
            return null;
        }

        //////////////////////////////////////////////////////////////////////
        // Sets the data in the specified slot on the currently running thread.
        //| <include path='docs/doc[@for="Thread.SetData"]/*' />
        public static void SetData(LocalDataStoreSlot slot, Object data)
        {
            // Create new DLS if one hasn't been created for this thread.
            if (localDataStore == null) {
                localDataStore = m_LocalDataStoreMgr.CreateLocalDataStore();
            }
            localDataStore.SetData(slot, data);
        }

        /*=============================================================*/

        internal Object ExceptionState
        {
            [NoHeapAllocation]
            get { return exceptionStateInfo;}
            [NoHeapAllocation]
            set { exceptionStateInfo = value;}
        }

        //
        // This is just designed to prevent compiler warnings.
        // This field is used from native, but we need to prevent the compiler warnings.
        //
#if _DEBUG
        private void DontTouchThis()
        {
            threadStart = null;
            m_Priority = 0;
        }
#endif
        //////////////////////////////////////////////////////////////////////
        // Volatile Read & Write and MemoryBarrier methods.
        // Provides the ability to read and write values ensuring that the values
        // are read/written each time they are accessed.
        //

        //| <include path='docs/doc[@for="Thread.VolatileRead"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern byte VolatileRead(ref byte address);

        //| <include path='docs/doc[@for="Thread.VolatileRead1"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern short VolatileRead(ref short address);

        //| <include path='docs/doc[@for="Thread.VolatileRead2"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern int VolatileRead(ref int address);

        //| <include path='docs/doc[@for="Thread.VolatileRead3"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern long VolatileRead(ref long address);

        //| <include path='docs/doc[@for="Thread.VolatileRead4"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern sbyte VolatileRead(ref sbyte address);

        //| <include path='docs/doc[@for="Thread.VolatileRead5"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern ushort VolatileRead(ref ushort address);

        //| <include path='docs/doc[@for="Thread.VolatileRead6"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern uint VolatileRead(ref uint address);

        //| <include path='docs/doc[@for="Thread.VolatileRead7"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern IntPtr VolatileRead(ref IntPtr address);

        //| <include path='docs/doc[@for="Thread.VolatileRead8"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern UIntPtr VolatileRead(ref UIntPtr address);

        //| <include path='docs/doc[@for="Thread.VolatileRead9"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern ulong VolatileRead(ref ulong address);

        //| <include path='docs/doc[@for="Thread.VolatileRead10"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern float VolatileRead(ref float address);

        //| <include path='docs/doc[@for="Thread.VolatileRead11"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern double VolatileRead(ref double address);

        //| <include path='docs/doc[@for="Thread.VolatileRead12"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern Object VolatileRead(ref Object address);

        //| <include path='docs/doc[@for="Thread.VolatileWrite"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref byte address, byte value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite1"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref short address, short value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite2"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref int address, int value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite3"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref long address, long value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite4"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref sbyte address, sbyte value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite5"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref ushort address, ushort value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite6"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref uint address, uint value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite7"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref IntPtr address, IntPtr value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite8"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref UIntPtr address, UIntPtr value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite9"]/*' />
        [CLSCompliant(false)]
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref ulong address, ulong value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite10"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref float address, float value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite11"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref double address, double value);

        //| <include path='docs/doc[@for="Thread.VolatileWrite12"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void VolatileWrite(ref Object address, Object value);

        //| <include path='docs/doc[@for="Thread.MemoryBarrier"]/*' />
        [Intrinsic]
        [NoHeapAllocation]
        public static extern void MemoryBarrier();

        [NoHeapAllocation]
        internal static unsafe void DisplayAbbrev(ref ThreadContext context, String s)
        {
            fixed (ThreadContext * pContext = &context) {
                DebugStub.Print("{0}: ctx={1:x8} esp={2:x8} ebp={3:x8} eip={4:x8} " +
                                "thr={5:x8} efl={6:x8} p={7:x8} n={8:x8}\n",
                                __arglist(
                                    s,
                                    (UIntPtr)pContext,
                                    context.esp,
                                    context.ebp,
                                    context.eip,
                                    Kernel.AddressOf(context.thread),
                                    context.efl,
                                    (UIntPtr)context.prev,
                                    (UIntPtr)context.next));
            }
        }

        [NoHeapAllocation]
        internal static unsafe void Display(ref ThreadContext context, String s)
        {
            fixed (ThreadContext * pContext = &context) {
                DebugStub.Print("{0}: ctx={1:x8} num={2:x2}\n",
                                __arglist(
                                    s,
                                    (UIntPtr)pContext,
                                    context.num));
            }

            DebugStub.Print("  thr={0:x8} prv={1:x8} nxt={2:x8}\n",
                            __arglist(
                                (UIntPtr)Kernel.AddressOf(context.thread),
                                (UIntPtr)context.prev,
                                (UIntPtr)context.next));

            DebugStub.Print("  eax={0:x8} ebx={1:x8} ecx={2:x8} edx={3:x8}\n",
                            __arglist(
                                context.eax,
                                context.ebx,
                                context.ecx,
                                context.edx));

            DebugStub.Print("  esp={0:x8} ebp={1:x8} esi={2:x8} edi={3:x8}\n",
                            __arglist(
                                context.esp,
                                context.ebp,
                                context.esi,
                                context.edi));

            DebugStub.Print("  eip={0:x8} efl={1:x8} err={2:x8} cr2={3:x8}\n",
                            __arglist(
                                context.eip,
                                context.efl,
                                context.err,
                                context.cr2));
        }

        ///////////////////////////////////////////////// Blocking operations.
        //
        internal int WaitAny(WaitHandle[] waitHandles,
                             int waitHandlesCount,
                             TimeSpan timeout)
        {
            SchedulerTime stop = SchedulerTime.Now + timeout;
            return WaitAny(waitHandles, waitHandlesCount, stop);
        }

        internal int WaitAny(WaitHandle[] waitHandles,
                             int waitHandlesCount,
                             SchedulerTime stop)
        {
            VTable.Assert(threadState == ThreadState.Running);
            VTable.Assert(!blocked);

            Kernel.Waypoint(100);
            ThreadEntry[] entries = GetWaitEntries(waitHandlesCount);

            bool iflag = Processor.DisableInterrupts();
            try {
                Thread target = null;

                Scheduler.DispatchLock();
                try {
                    Kernel.Waypoint(101);

                    // Enqueue on all of the non-signaled handles.
                    for (int marked = 0; marked < waitHandlesCount; marked++) {
                        if (waitHandles[marked].AcquireOrEnqueue(entries[marked])) {
                            // If one of the handles was signaled, then abort waits...
#if DEBUG_DISPATCH
                            DebugStub.Print("Thread {0:x8} WaitAny 004 [Presignal] "+
                                            "on {1:x8}\n",
                                            __arglist(
                                                Kernel.AddressOf(this),
                                                Kernel.AddressOf(waitHandles[marked])));
#endif // DEBUG_DISPATCH
                            for (int released = 0; released < marked; released++) {
                                entries[released].RemoveFromQueue();
                            }
                            Scheduler.DispatchRelease();
                            return marked;
                        }
                    }

                    blocked = true;
                    blockedOn = entries;
                    blockedOnCount = waitHandlesCount;
                    blockedUntil = stop;
                    Monitoring.Log(Monitoring.Provider.Thread,
                                   (ushort)ThreadEvent.WaitAny, 0,
                                   (uint)stop.Ticks, (uint)(stop.Ticks >> 32),
                                   (uint)this.threadIndex, 0, 0);
                    unblockedBy = WaitHandle.WaitTimeout;

#if DEBUG_DISPATCH
                    DebugStub.Print("Thread {0:x8} WaitAny 007 [Blocking] on\n",
                                    __arglist(Kernel.AddressOf(this)));
                    for (int i = 0; i < waitHandlesCount; i++) {
                        DebugStub.Print("   {0:d3}: {1:x8}\n",
                                        __arglist(i, Kernel.AddressOf(waitHandles[i])));
                    }
#endif // DEBUG_DISPATCH

                    target = Scheduler.OnThreadBlocked(this, stop);
                    Scheduler.SelectingThread(target);
                }
                catch(Exception e) {
                    Scheduler.DispatchRelease();
                    throw e;
                }

                if (target == null) {
#if DEBUG_DISPATCH
                    DebugStub.Print("Thread {0:x8} WaitAny 013 [Idle Thread]\n",
                                    __arglist(Kernel.AddressOf(this)));
#endif // DEBUG_DISPATCH
                    target = Processor.CurrentProcessor.IdleThread;
                    VTable.Deny(Transitions.UnderGCControl(target.threadIndex));
                }
#if DEBUG_DISPATCH
                DebugStub.Print("Thread {0:x8} WaitAny 014 [SwitchTo] thread {1:x8}\n",
                                __arglist(
                                    Kernel.AddressOf(this),
                                    Kernel.AddressOf(target)));
#endif // DEBUG_DISPATCH
                target.SwitchTo();

                // Execution will return here only after either the timeout
                // period has been passed or one of the handles has been
                // signalled. If we were unblocked, then WaitDone will have
                // been called and unblockedBy will not be WaitHandle.WaitTimeout.
                // If we timed out, then WaitFail will have been called.

            }
            finally {
                //Kernel.Waypoint(114);
                Processor.RestoreInterrupts(iflag);
#if DEBUG_DISPATCH
                DebugStub.Print("Thread {0:x8} WaitAny 019\n",
                                __arglist(Kernel.AddressOf(this)));
#endif // DEBUG_DISPATCH
            }

            return unblockedBy;
        }

        // This method should only be called by low-level synchronization
        // primitives derived from WaitHandle.  If you need to cause a
        // thread to sleep or wake up, you should use those primitives
        // instead.
        [NoHeapAllocation]
        internal void WaitDone(ThreadEntry entry)
        {
            Scheduler.AssertDispatchLockHeld();
            // DebugStub.Break();  //

            for (int i = 0; i < blockedOnCount; i++) {
                if (blockedOn[i] == entry) {
                    unblockedBy = i;
                }
                else {
                    blockedOn[i].RemoveFromQueue();
                }
            }

            // Tell the scheduler this thread is no longer blocked.
            Scheduler.OnThreadUnblocked(this);

            // Clear out the blocked information, after we know we won't want it.
            blocked = false;
            blockedOn = null; //No more beneficiaries of inherited time.
            blockedOnCount = 0;
        }

        // This method is called when the scheduler has timed-out the wait.
        [NoHeapAllocation]
        internal void WaitFail()
        {
            Scheduler.AssertDispatchLockHeld();
            // DebugStub.Break();  //

#if DEBUG_DISPATCH
            DebugStub.Print("Thread {0:x8} [WaitFail]\n",
                            __arglist(Kernel.AddressOf(this)));
#endif // DEBUG_DISPATCH

            VTable.Assert(unblockedBy == WaitHandle.WaitTimeout);

            for (int i = 0; i < blockedOnCount; i++) {
                blockedOn[i].RemoveFromQueue();
            }

            Monitoring.Log(Monitoring.Provider.Thread,
                           (ushort)ThreadEvent.WaitFail, 0,
                           (uint)blockedUntil.Ticks,
                           (uint)(blockedUntil.Ticks >> 32),
                           (uint)this.threadIndex, 0, 0);

            // Clear out the blocked information, after we know we won't want it.
            blocked = false;
            blockedOn = null; //No more beneficiaries of inherited time.
            blockedOnCount = 0;
        }

        ///////////////////////////////////////////////// Context Switch Code.
        //

        // This method should only be called by the scheduler and by Yield().
        //  precondition: Scheduler.dispatchLock held
        // postcondition: Scheduler.dispatchLock released
        [NoHeapAllocation]
        private void SwitchTo()
        {
            Thread currentThread = CurrentThread;
#if DONT

            Tracing.Log(Tracing.Audit, "Schedule(old={0:x}, new={1:x})",
                        Kernel.AddressOf(currentThread),
                        Kernel.AddressOf(this));
#endif
#if DEBUG_SWITCH
            Display(ref this.context, "New Context");
#endif
            Scheduler.AssertDispatchLockHeld();
            Processor.CurrentProcessor.NumContextSwitches++;
            //Tracing.LogContextSwitch(CurrentThread.GetThreadId(), this.GetThreadId());
#if DEBUG_SWITCH
            Thread.DisplayAbbrev(ref currentThread.context, "swi bef");
#endif
            Kernel.Waypoint(2);
            Monitoring.Log(Monitoring.Provider.Thread,
                           (ushort)ThreadEvent.SwitchTo, 0,
                           (uint)this.context.threadIndex, 0, 0, 0, 0);
            Processor.SwitchToThreadContext(ref currentThread.context,
                                            ref this.context);
            // Kernel.Waypoint(7);
#if DEBUG_SWITCH
            Thread.DisplayAbbrev(ref Thread.CurrentThread.context, "swi aft");
#endif
#if DONT
            Display(ref currentThread.context, "Old Context");

            Display(ref threadTable[0].context, "[0] Context");
            Display(ref threadTable[1].context, "[1] Context");
#endif
        }


        //
        //////////////////////////////////////////////////////////////////////

        [NoHeapAllocation]
        public static void Yield()
        {
            bool iflag = Processor.DisableInterrupts();
            try {
                Thread target = CurrentThread;

                Scheduler.DispatchLock();
                try {
                    Kernel.Waypoint(1);
                    target = Scheduler.OnThreadYield(CurrentThread);
#if DEBUG_DISPATCH
                    DebugStub.Print("Yield.Selecting\n");
#endif // DEBUG_DISPATCH
                    Scheduler.SelectingThread(target);
                }
                catch(Exception e) {
                    Scheduler.DispatchRelease();
                    throw e;
                }

                if (target == null) {
                    target = CurrentThread;
                }
                target.SwitchTo();
            }
            finally {
                //Kernel.Waypoint(114);
                Processor.RestoreInterrupts(iflag);
            }
        }

        [NoHeapAllocation]
        public bool IsWaiting()
        {
            return blocked;
        }

        [NoHeapAllocation]
        public bool IsStopped()
        {
            return (threadState == ThreadState.Stopped);
        }

        [NoHeapAllocation]
        public bool IsStopping()
        {
            return (threadState == ThreadState.Stopping ||
                    threadState == ThreadState.Stopped);
        }

        [PreInitRefCounts]
        static unsafe Thread()
        {
            threadIndexGenerator = 1;

            DebugStub.Print("Thread()");

            // Enable Thread.CurrentThread as soon as we can!
            initialThread = Magic.toThread(GCs.BootstrapMemory.Allocate(typeof(Thread)));
            initialThread.threadState = ThreadState.Running;
            initialThread.SetKernelMode();
            initialThread.threadIndex = 0;

            // Allocate tables for thread management
            threadTable = (Thread[])
                GCs.BootstrapMemory.Allocate(typeof(Thread[]), maxThreads);

            // Initialize the thread and event tables
            threadTable[initialThread.threadIndex] = initialThread;

            initialThread.context.threadIndex =
                unchecked((ushort)(initialThread.threadIndex));
            Transitions.InitializeStatusWord(ref initialThread.context);
            initialThread.context.processId = unchecked((ushort)(1));

            // Prevent stack linking.
            initialThread.context.stackBegin = 0;
            initialThread.context.stackLimit = 0;
            initialThread.context.UpdateAfterGC(initialThread);
            Processor.SetCurrentThreadContext(ref initialThread.context);

#if DEBUG_THREAD_CONTEXT_ALIGNMENT
            Tracing.Log(Tracing.Debug, "Thread.alignment = {0}",
                        (((RuntimeType)typeof(Thread)).classVtable).baseAlignment);
            Tracing.Log(Tracing.Debug, "ThreadContext.alignment = {0}",
                        (((RuntimeType)typeof(ThreadContext)).classVtable).baseAlignment);
            Tracing.Log(Tracing.Debug, "MmxContext.alignment = {0}",
                        (((RuntimeType)typeof(MmxContext)).classVtable).baseAlignment);

            Tracing.Log(Tracing.Debug, "&initialThread         = {0:x8}",
                        Kernel.AddressOf(initialThread));
            fixed (void *v = &initialThread.context) {
                Tracing.Log(Tracing.Debug, "&initialThread.context = {0:x8}",
                            (UIntPtr)v);
            }
            fixed (void *v = &initialThread.context.mmx) {
                Tracing.Log(Tracing.Debug, "&initialThread.context.mmx = {0:x8}",
                            (UIntPtr)v);
            }
            fixed (void *v = &initialThread.context.mmx.st0) {
                Tracing.Log(Tracing.Debug, "&initialThread.context.mmx.st0 = {0:x8}",
                            (UIntPtr)v);
            }
#endif

            VTable.Assert((int)(((RuntimeType)typeof(Thread)).classVtable).baseAlignment == 16);
            VTable.Assert((int)(((RuntimeType)typeof(ThreadContext)).classVtable).baseAlignment == 16);
            VTable.Assert((int)(((RuntimeType)typeof(MmxContext)).classVtable).baseAlignment == 16);

            Tracing.Log(Tracing.Debug, "InitialThread={0:x8}",
                        Kernel.AddressOf(initialThread));
            Monitoring.Log(Monitoring.Provider.Thread,
                           (ushort)ThreadEvent.ThreadPackageInit);
            initialThread.bumpAllocator.Dump();

            Tracing.Log(Tracing.Debug, "Class constructor Thread() exiting\n");
        }

        internal static unsafe void FinishInitializeThread()
        {
            // Set the fields of initialThread
            int stackVariable;
            initialThread.context.stackBegin =
                (new UIntPtr(&stackVariable) + 0xfff) & new UIntPtr(~0xfffU);
            initialThread.context.stackLimit = 0;

            initialThread.autoEvent = new AutoResetEvent(false);
            initialThread.joinEvent = new ManualResetEvent(false);
            initialThread.schedulerEntry = new ThreadEntry(initialThread);
            initialThread.GetWaitEntries(1); // Cache allows wait without alloc
            Transitions.RuntimeInitialized();
            Transitions.ThreadStart();

            // Instantiate the static variable that needs to be initialized
            m_LocalDataStoreMgr = new LocalDataStoreMgr();

            processStopException = new ProcessStopException();

            // Tell the scheduler to initialize the thread.
            Scheduler.OnThreadStateInitialize(initialThread, false);
        }

        /// <summary> Prepares a new Thread to take on role as kernel thread
        /// for upcoming processor.  Called by Bootstrap processor. </summary>
        public static Thread PrepareKernelThread()
        {
            Thread kernelThread = new Thread(null);
            GC.NewThreadNotification(kernelThread, false);
            return kernelThread;
        }

        public static void BindKernelThread(Thread  kernelThread,
                                            UIntPtr stackBegin,
                                            UIntPtr stackLimit)
        {
            kernelThread.context.processId  = initialThread.context.processId;
            kernelThread.context.stackBegin = stackBegin;
            kernelThread.context.stackLimit = 0/* stackLimit */;

            kernelThread.context.UpdateAfterGC(kernelThread);
            Processor.SetCurrentThreadContext(ref kernelThread.context);
            kernelThread.threadState = ThreadState.Running;
            Transitions.ThreadStart();
        }

        [NoHeapAllocation]
        public void DumpStackInfo()
        {
            Tracing.Log(Tracing.Debug, "<< thr={0:x8} beg={1:x8} lim={2:x8} ptr={3:x8} >>",
                        Kernel.AddressOf(this),
                        context.stackBegin,
                        context.stackLimit,
                        Processor.GetStackPointer());
        }

        public Thread GetRunnableBeneficiary()
        {
            // this does a depth first search, not sure if that is a good idea.
            if (threadState == ThreadState.Running) {
                return this;
            }
            Thread tempThread;
            for (int i = 0; blockedOn != null && i < blockedOnCount; i++) {
                if (blockedOn[i] != null) {
                    tempThread = blockedOn[i].GetBeneficiary();
                    if (tempThread != null) {
                        tempThread = tempThread.GetRunnableBeneficiary();
                    }
                    if (tempThread != null) {
                        return tempThread;
                    }
                }
            }
            return null;
        }

#if THREAD_TIME_ACCOUNTING
        // timestamp of last update for ExecutionTime
        internal ulong LastUpdateTime
        {
            [NoHeapAllocation]
            get { return context.lastExecutionTimeUpdate; }
            [NoHeapAllocation]
            set { context.lastExecutionTimeUpdate = value; }
        }

        // fixme: where to init. this one ???
        //        FinishInitializeThread() seems to be called before
        //        Processor.CyclesPerSecond is set up
        //static private ulong multiplier = Processor.CyclesPerSecond /
        //                                  TimeSpan.TicksPerSecond
#else
        protected TimeSpan executionTime;
#endif
        public TimeSpan ExecutionTime
        {
#if THREAD_TIME_ACCOUNTING
            //[NoHeapAllocation]
            get
            {
                ulong m = Processor.CyclesPerSecond / TimeSpan.TicksPerSecond;

                bool saved = Processor.DisableInterrupts();
                try
                {
                    if (Processor.GetCurrentThread() == this)
                    {
                        ulong now = Processor.CycleCount;
                        context.executionTime += now -
                            context.lastExecutionTimeUpdate;
                        LastUpdateTime = now;
                    }
                }
                finally
                {
                    Processor.RestoreInterrupts(saved);
                }

                // fixme: this division is bad (slow), hot to get rid of it?
                return new TimeSpan((long)(context.executionTime / m));
            }
#else
            [NoHeapAllocation]
            get { return executionTime; }
#endif
        }

#if THREAD_TIME_ACCOUNTING
        // This provides access to the raw cycle counter, so access to it
        // should be fast, compared to ExecutionTime.  This might be useful
        // for monitoring code which calls this often and can postprocess
        // these times otherwise
        public ulong RawExecutionTime
        {
            //[NoHeapAllocation]
            get
            {
                bool saved = Processor.DisableInterrupts();
                try
                {
                    if (Processor.GetCurrentThread() == this)
                    {
                        ulong now = Processor.CycleCount;
                        context.executionTime += now -
                            context.lastExecutionTimeUpdate;
                        LastUpdateTime = now;
                    }
                }
                finally
                {
                    Processor.RestoreInterrupts(saved);
                }

                return context.executionTime;
            }
        }
#endif

        internal static
        void VisitBootstrapData(NonNullReferenceVisitor visitor)
        {
            visitor.VisitReferenceFields(initialThread);
            visitor.VisitReferenceFields(threadTable);
        }

        internal static void UpdateAfterGC()
        {
            // Update all the thread pointers in the thread contexts
            for (int i = 0; i < threadTable.Length; i++) {
                Thread thread = threadTable[i];
                if (thread != null) {
                    thread.context.UpdateAfterGC(thread);
                }
            }
        }

        // Cache for ABI synchronization
        private WaitHandle[] syncHandles;
        internal WaitHandle[] GetSyncHandles(int num)
        {
            if (syncHandles == null || syncHandles.Length < num) {
                syncHandles = new WaitHandle[num + 8];
            }
            return syncHandles;
        }

        // Cache for handle synchronization
        private ThreadEntry[] entries;
        internal ThreadEntry[] GetWaitEntries(int num)
        {
            if (entries == null || entries.Length < num) {
                num += 8;   // So we don't have to do this too frequently.
                entries = new ThreadEntry[num];
                for (int i = 0; i < num; i++) {
                    entries[i] = new ThreadEntry(this);
                }
            }
            return entries;
        }

        // Caches for Select synchronization
        // We use stacks, because selectable abstractions might
        // internally implement HeadMatches using select receive
        // which is called from within an outer select.
        // NOTE however that internal selects should never block
        // (use timeout)
        private Stack selectBoolsStack;
        private Stack selectObjectsStack;
        private Stack selectSyncHandlesStack;

        public bool[] PopSelectBools(int size)
        {
            if (selectBoolsStack == null) {
                selectBoolsStack = new Stack();
            }
            if (selectBoolsStack.Count == 0) {
                return new bool [size];
            }
            bool[] selectBools = (bool[])selectBoolsStack.Pop();
            if (selectBools.Length < size) {
                return new bool [size];
            }
            return selectBools;
        }

        public void PushSelectBools(bool[] cache) {
            selectBoolsStack.Push(cache);
        }

        public ISelectable[] PopSelectObjects(int size)
        {
            if (selectObjectsStack == null) {
                selectObjectsStack = new Stack();
            }
            if (selectObjectsStack.Count == 0) {
                return new ISelectable [size];
            }
            ISelectable[] selectObjects = (ISelectable[])selectObjectsStack.Pop();
            if (selectObjects.Length < size) {
                return new ISelectable [size];
            }
            return selectObjects;
        }
        public void PushSelectObjects(ISelectable[] cache) {
            for (int i=0; i<cache.Length; i++) {
                cache[i] = null;
            }
            selectObjectsStack.Push(cache);
        }

        public SyncHandle[] PopSelectSyncHandles(int size)
        {
            if (selectSyncHandlesStack == null) {
                selectSyncHandlesStack = new Stack();
            }
            if (selectSyncHandlesStack.Count == 0) {
                return new SyncHandle [size];
            }
            SyncHandle[] selectSyncHandles = (SyncHandle[])selectSyncHandlesStack.Pop();
            if (selectSyncHandles.Length < size) {
                return new SyncHandle [size];
            }
            return selectSyncHandles;
        }
        public void PushSelectSyncHandles(SyncHandle[] cache) {
            for (int i=0; i<cache.Length; i++) {
                cache[i] = new SyncHandle();
            }
            selectSyncHandlesStack.Push(cache);
        }


        // Given a frame's range in memory (its esp/ebp), check whether
        // the frame contains the top transition record.  If so,
        // prepare to skip over a process's frames.
        [AccessedByRuntime("referenced from halasm.asm")]
        [NoStackLinkCheck] // We don't want to throw an exception here;
            // Therefore, we cannot risk allocating stack segments,
            // and we should only call other NoStackLinkCheck functions (XXX).
        internal static unsafe UIntPtr CheckKernelProcessBoundary(UIntPtr esp, UIntPtr ebp, Exception exn)
        {
            ThreadContext *context = Processor.GetCurrentThreadContext();
            System.GCs.CallStack.TransitionRecord *topMarker = context->processMarkers;
            System.GCs.CallStack.TransitionRecord *secondMarker = context->stackMarkers;
            UIntPtr topMarkerPtr = (UIntPtr) topMarker;
            // If the top marker is in our frame, we've reached a boundary:
            if (esp < topMarkerPtr && topMarkerPtr <= ebp) {
                Thread.CurrentThread.lastUncaughtException = exn;
                //   Is this a ProcessStopException?  If not, it's a bug; log the bug.
                if (!(exn is ProcessStopException)) {
                    // Log the bug, but don't do anything that could
                    // throw another exception (e.g. memory allocation).
                    // XXX: what if stack allocation throws an exception here?
                    DebugStub.Print("Bug: kernel exception thrown to process (saved to Thread.LastUncaughtException)\n");
                    Tracing.Log(Tracing.Warning, "Bug: kernel exception thrown to process (saved to Thread.LastUncaughtException)\n");
                }
                //   atomic
                //   {   // do these together so we never enter process mode
                //       remove top process->kernel marker from marker list
                //       remove top kernel->process marker from marker list
                //   }
                bool iflag = Processor.DisableInterrupts();
                Scheduler.DispatchLock();
                try {
                    context->processMarkers = context->processMarkers->oldTransitionRecord;
                    context->stackMarkers = context->stackMarkers->oldTransitionRecord;
                    context->SetKernelMode();
                }
                finally {
                    Scheduler.DispatchRelease();
                    Processor.RestoreInterrupts(iflag);
                }

                //Return the kernel->process marker, in preparation for these operations:
                //   edx := retAddr from *stackBottom from kernel->process marker
                //   restore esp,ebp from kernel->process marker
                //   while(top kernel->process marker not in stack segment)
                //       pop (and free) stack segment
                //   restore ebx,edi,esi from kernel->process marker
                return new UIntPtr(secondMarker);
            }
            else {
                return 0;
            }
        }

        // Discard any garbage stack segments that follow the segment
        // containing the marker.  After this runs, the topmost stack
        // segment will contain the marker.
        [AccessedByRuntime("referenced from halasm.asm")]
        [NoStackLinkCheck]
        internal static unsafe void DiscardSkippedStackSegments(System.GCs.CallStack.TransitionRecord *marker)
        {
            ThreadContext *context = Processor.GetCurrentThreadContext();
            UIntPtr markerPtr = new UIntPtr(marker);
            //   while(top kernel->process marker not in stack segment)
            //       pop (and free) stack segment

            //
            // HACKHACK think about what this is doing. The topmost
            // stack segment is the one *currently in use*. On a paging
            // system, freeing it unmaps the underlying physical page.
            // Needless to say, our ability to use esp after that is
            // severely compromised.
            //
#if !PAGING
            while ((context->stackBegin != 0) && !(context->stackLimit <= markerPtr && markerPtr < context->stackBegin)) {
                Microsoft.Singularity.Memory.Stacks.ReturnStackSegment(ref *context);
            }
#endif

            // Unlink marker:
            context->stackMarkers = marker->oldTransitionRecord;
        }

        // Most recently thrown exception object that the thread
        // did not catch at all (i.e. that propagated to the bottom
        // of the stack without encountering an appropriate catch clause).
        public Exception LastUncaughtException
        {
            [NoHeapAllocation]
            get {
                return lastUncaughtException;
            }
        }

#if PAGING
        // Switch to a different protection domain. This is an advanced stunt
        // for use by kernel threads only
        internal static void SwitchToDomain(ProtectionDomain newDomain)
        {
            Thread currentThread = CurrentThread;
            currentThread.CheckAddressSpaceConsistency();

            AddressSpace processorSpace = Processor.GetCurrentAddressSpace();
            AddressSpace newSpace = newDomain.AddressSpace;

            if (newSpace != processorSpace) {
                Processor.ChangeAddressSpace(newSpace);
                currentThread.tempDomain = newDomain;
            }

            currentThread.CheckAddressSpaceConsistency();
        }

        // Call this to snap back to our parent process' domain.
        internal static void RevertToParentDomain()
        {
            Thread currentThread = CurrentThread;
            currentThread.CheckAddressSpaceConsistency();
            Processor.ChangeAddressSpace(currentThread.process.Domain.AddressSpace);
            currentThread.tempDomain = null;
            currentThread.CheckAddressSpaceConsistency();
        }

        // This property provides the correct answer even when an
        // arbitrary protection domain is temporarily being used
        internal ProtectionDomain CurrentDomain {
            get {
                CheckAddressSpaceConsistency();
                return (tempDomain != null) ? tempDomain : process.Domain;
            }
        }

        [Inline]
        private void CheckAddressSpaceConsistency()
        {
            ProtectionDomain currentDomain = (tempDomain != null) ? tempDomain : process.Domain;
            DebugStub.Assert(Processor.GetCurrentAddressSpace() ==
                             currentDomain.AddressSpace);
        }
#endif
    }
}
