////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   RobinScheduler.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Microsoft.Singularity;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling
{
#if !SIMULATOR
    public class SystemScheduler
    {
        static public void RegisterScheduler()
        {
            Robin.RobinScheduler.RegisterScheduler();
        }
    }
#endif
}

namespace Microsoft.Singularity.Scheduling.Robin
{
    /// <summary>
    /// Summary description for RobinScheduler.
    /// </summary>
    public class RobinScheduler : ICpuScheduler
    {
        public static readonly TimeSpan ContextSwitch = new TimeSpan(200);
        // minimum time slice to be worth scheduling the CPU: 1 millisec
        public static readonly TimeSpan MinSlice = new TimeSpan(10 * ContextSwitch.Ticks);
        public static readonly TimeSpan AFewSlice = new TimeSpan(3 * MinSlice.Ticks);
        public static readonly TimeSpan RobinSlice = new TimeSpan(10 * TimeSpan.TicksPerMillisecond);


        static public void RegisterScheduler()
        {
            CpuResource.RegisterSystemScheduler(new RobinScheduler());
        }

        public override ISchedulerProcessor CreateSchedulerProcessor(Processor processor)
        {
            return new RobinProcessor(processor);
        }

        public override ISchedulerThread CreateSchedulerThread(Thread thread)
        {
            return new RobinThread(thread);
        }

        public override ISchedulerActivity CreateSchedulerActivity()
        {
            return new RobinActivity();
        }

        public override ISchedulerCpuReservation ReserveCpu(ISchedulerActivity activity,
                                                                    CpuResourceAmount amount,
                                                                    TimeSpan period)
        {
            return null;
        }

        public bool ReserveRecurringCpu(Activity activity,
                                        ref TimeSpan amount,
                                        ref TimeSpan period)
        {
            return true;
        }

        public override bool ShouldReschedule()
        {
            RobinThread currentThread = GetCurrentThread();
            if (Scheduler.TimerInterruptedFlag && currentThread != null) {
                //SchedulerClock.CheckInterrupt();
            }

            if (currentThread == null ||
                Scheduler.YieldFlag ||
                currentThread.EnclosingThread.IsWaiting() ||
                currentThread.EnclosingThread.IsStopped()) {

                Reschedule();
            }
            return NeedToReschedule || Scheduler.TimerInterruptedFlag;
            // || timer should have fired but didn't?
        }

        // DI -- this is somehow similar to EnableInterrupts
        public override bool NextThread(out Thread nextThread)
        {
            Debug.Assert(!Processor.InterruptsDisabled());
            bool iflag = Processor.DisableInterrupts();
            bool halted = false;
            RobinThread currentThread = GetCurrentThread();

            SchedulerClock.CheckInterrupt();
            if (ShouldReschedule()) {
                //Debug.Print("Calling RescheduleInterrupt()\n");
                halted = RescheduleInterrupt(); //TODO: RescheduleInterrupt returns true if the processor needs to be halted.
                currentThread = GetCurrentThread();
            }
            else {
                //Debug.Print("No call to RescheduleInterrupt()\n");
            }
            if (currentThread != null) {
                nextThread = currentThread.EnclosingThread;
            }
            else {
                Debug.Assert(halted);
                nextThread = null;
            }

            ((RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor).NeedToReschedule = false;
            // printf("-------------------------currentThread-Id %d\n", currentThread.Id);
            //return currentThread;
            Processor.RestoreInterrupts(iflag);
            return halted;
        }
        //public static void Yield();

#region Constants

        // Maximum Scheduling Period is 1 sec (i.e. in 100s of nanoseconds).
        static readonly TimeSpan AmountCpu = CpuResource.MaxPeriod;
        //TODO: TICKS vs. TIME_SPAN vs DATE_TIME

#endregion

#region Static data members

        static RobinActivity      CircularActivityList = null;
        /// <summary>
        /// This must ALWAYS (when lock not held) point to a current resource
        /// container in the queue.
        /// </summary>
        static internal RobinActivity NextActivity;


        //moved to be processor specific
        //static RobinActivity        RobinActivityNext = null;
        //static RobinActivity        RobinActivityCurrent = null;  // activity currently executing from

        static OneShotReservation ReservationFreePool;
        static TimeSpan     IdleTime = new TimeSpan(0);
        static bool         Idle
        {
            get { return GetCurrentProcessor().Idle; }
        }

        //moved to be processor specific
        //static RobinThread       CurrentThreadGlobal = null;
        //static TimeSpan       RobinSliceLeft = RobinScheduler.RobinSlice; //new TimeSpan(0);  // the round robin list, if any, and its CPU slice
        //public static DateTime        SchedulingTime = new DateTime(0);
        //static bool           NeedToReschedule = false;

        public static TimeSpan      LA_Time = RobinScheduler.MinSlice;

        public static DateTime SchedulingTime
        {
            get {
                return ((RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor).SchedulingTime;
            }
        }

        public static bool NeedToReschedule
        {
            get {
                return ((RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor).NeedToReschedule;
            }
        }

#endregion

        public RobinScheduler()
        {
        }

        public override void Initialize()
        {
            RobinScheduler.InitializeScheduler();
        }

        public static void InitializeScheduler()
        { //TODO: Should all the null's be added in here?
            for (int i=0; i<Processor.processorTable.Length; i++) {
                ((RobinProcessor)Processor.processorTable[i].SchedulerProcessor).SchedulingTime = SystemClock.GetKernelTime();
            }
//
//            TimeSpan currentSliceLeft = RobinSliceLeft;
            bool iflag = Processor.DisableInterrupts();
            SchedulerClock.SetNextInterrupt(SchedulingTime+RobinScheduler.RobinSlice);
            Processor.RestoreInterrupts(iflag);
        }

#region ISystemScheduler Members

        public override void BeginDelayedConstraint(Hashtable resourceEstimates, TimeSpan relativeDeadline, ISchedulerTask taskToEnd, out ISchedulerTask schedulerTask)
        {
            TimeConstraint timeConstraint = new TimeConstraint();
            timeConstraint.Estimate = CpuResource.Provider().CpuToTime((CpuResourceAmount)resourceEstimates[CpuResource.Provider().ResourceString]);
            timeConstraint.Start = new DateTime(0); //Start now.
            timeConstraint.RelativeDeadline = relativeDeadline;
            timeConstraint.Deadline = new DateTime(0); //A 0-deadline means relative instead.
            schedulerTask = Thread.CurrentThread.SchedulerThread.PrepareDelayedTask(taskToEnd, ref timeConstraint, SystemClock.GetKernelTime());
        }

        public override bool BeginConstraint(Hashtable resourceEstimates, DateTime deadline, ISchedulerTask taskToEnd, out ISchedulerTask schedulerTask)
        {
            DateTime    timeNow = SystemClock.GetKernelTime();
            RobinThread        thread = GetCurrentThread();
            ulong    start, stop; // S97
            Debug.Assert(!Processor.InterruptsDisabled());

            thread.IpcCheckFreeConstraint();

            start = Processor.CycleCount;

            Debug.Assert(taskToEnd == null || taskToEnd == thread.ReservationStack);
            bool end_previous = (taskToEnd != null);
            TimeConstraint constraint = new TimeConstraint();
            constraint.Deadline = deadline;
            constraint.Estimate = CpuResource.Provider().CpuToTime((CpuResourceAmount)resourceEstimates[CpuResource.Provider().ResourceString]);
            constraint.Start = timeNow;

            bool ok = thread.BeginConstraintBeforeWaitValidate(end_previous, ref constraint, timeNow);
            schedulerTask = thread.PendingReservation;
            if (ok) {
                OneShotReservation.BeginConstraintBeforeWait(thread, end_previous, constraint, timeNow);
                bool iflag = Processor.DisableInterrupts();
                ok = OneShotReservation.ResolveConstraint(thread);
                Processor.RestoreInterrupts(iflag);
            }
            stop = Processor.CycleCount;

            Scheduler.LogBeginConstraint(thread.EnclosingThread, ok, start, stop);

            //TODO: Check if this is a necessity, or if it's only for sim time.  Call in wrapper if necessary.
            //NextThread();
            return ok;
        }

        public override bool EndConstraint(ISchedulerTask taskToEnd)
        {
            Debug.Assert(!Processor.InterruptsDisabled());
            Debug.Assert(taskToEnd == GetCurrentThread().ReservationStack);
            bool iflag = Processor.DisableInterrupts();
            bool ok = OneShotReservation.EndPreviousConstraint(GetCurrentThread(), SystemClock.GetKernelTime());
            //
            // need to reschedule only if on a reserved slot and the top of EarliestDeadlineFirst doesn't
            //  belong to this task
            //
            if ((OneShotReservation.CurrentReservation != null) &&
                (OneShotReservation.GuaranteedReservations != null) &&
                (OneShotReservation.GuaranteedReservations.ReservTask != (GetCurrentThread()))) {
                Reschedule();
            }

            Processor.RestoreInterrupts(iflag);
            Scheduler.LogEndConstraint();

            //TODO: In the system wrapper for BeginConstraint -- it needs to call Yield/NextThread/MaybeYield
            //NextThread();
            return ok;
        }

        public static bool ReserveRecurringCpu(RobinActivity activity, ref TimeSpan amount, ref TimeSpan period)
        {
            return true;
        }

#endregion


#region OneShotCpuReservation related functions
        // OneShotCpuReservation related functions begin:

        public static int ReleaseReservationProtected(OneShotReservation reservation)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            Debug.Assert(reservation.ReferenceCount > 0);

            int newrefcnt = Interlocked.Decrement(ref reservation.ReferenceCount);

            if (newrefcnt == 0) {
                // Estimate may be positive when reservation reaches
                // its Deadline w/o using its Estimate
                Debug.Assert((reservation.Next == null) && (reservation.Previous == null));
                if (reservation.OriginalThread != null) {
                    reservation.OriginalThread.ReleaseProtected();
                    //Debug.Assert(reservation.ActiveThread != null);
                    //reservation.ActiveThread.ReleaseProtected();
                }
                else {
                    Debug.Assert(reservation.AssociatedActivity != null);
                    ActivityReleaseProtected(reservation.AssociatedActivity);
                }
                Debug.Assert(reservation.Next == null);
                CacheFreeReservation(reservation);
            }
            return newrefcnt;
        }


        public static OneShotReservation AllocateReservation()
        {
            OneShotReservation reservation;

            //  DisableInterrupts();
            reservation = IpcAllocateReservation();
            //  EnableInterrupts();

            if (reservation == null) {
                reservation = new OneShotReservation(); //(PRESERVATION)malloc(sizeof(struct OneShotReservation)); // to be done while on the I-stack!
                reservation.Clear();
            }
            return reservation;
        }

        static void FreeReservation(OneShotReservation reservation)
        {
            reservation = null; //free(reservation);
        }

        // Garbage-collect unused Reservations.
        public static int ReleaseReservation(OneShotReservation reservation)
        {
            // Debug.Assert(!Processor.InterruptsDisabled());
            Debug.Assert(reservation.ReferenceCount > 0);
            int newrefcnt = reservation.ReferenceCount - 1;
            reservation.ReferenceCount = newrefcnt;

            if (newrefcnt == 0) {
                // Estimate may be positive when reservation reaches its Deadline w/o using its Estimate
                Debug.Assert((reservation.Next == null) && (reservation.Previous == null));
                if (reservation.OriginalThread != null) {
                    reservation.OriginalThread.Release();
                    //Debug.Assert(reservation.ActiveThread != null);
                    //reservation.ActiveThread.Release();
                }
                else if (reservation.AssociatedActivity != null) {
                    ActivityObjRelease(reservation.AssociatedActivity);
                }
                CacheFreeReservation(reservation);
            }
            return newrefcnt;
        }

        // Like CheckFreeConstraint, but callable from the IPC path.
        // Because we can't call AllocateReservation, we use the helper thread
        // if there aren't any free Reservations.
        static void CacheFreeReservation(OneShotReservation reservation)
        {
            if (reservation.OriginalThread != null  &&
                reservation.OriginalThread.FreeReservation == null) {
                reservation.OriginalThread.FreeReservation = reservation;
            }
            else {
                IpcFreeReservation(reservation);
            }
        }


        // Callable with preemption disabled. Allocates a OneShotReservation
        // from the global free list.
        public static OneShotReservation IpcAllocateReservation()
        {
            OneShotReservation reservation;

            if ((reservation = ReservationFreePool) != null) {
                ReservationFreePool = reservation.FreeListNext;
            }

            return reservation;
        }

        // Callable with preemption disabled. Frees a OneShotReservation
        // to the global free list.
        public static void IpcFreeReservation(OneShotReservation reservation)
        {
            Debug.Assert(reservation != null);
            Debug.Assert(reservation.ReferenceCount == 0);

            reservation.FreeListNext = ReservationFreePool;
            ReservationFreePool = reservation;
        }

        //////////////////////////////////////////////////////////////////////

        public static void AddRefReservation(OneShotReservation reservation)
        {
            Debug.Assert(reservation.ReferenceCount >= 0);
            reservation.ReferenceCount++;
        }

#endregion

#region Potpourri (Unregioned functions)

        public static RobinThread GetCurrentThread()
        {
            return ((RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor).RunningThread;
        }

        public static void IChangeCurrentThread(RobinThread thread)
        {
            ((RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor).RunningThread = thread;
        }

        // Auxiliary Routines
        // TODO: Revisit the necessity of these functions.
        public static TimeSpan minInterval(TimeSpan TimeInterv0, TimeSpan TimeInterv1)
        {
            return (TimeInterv0 < TimeInterv1? TimeInterv0: TimeInterv1);
        }

        public static DateTime minTime(DateTime Time0, DateTime Time1)
        {
            return (Time0 < Time1? Time0: Time1);
        }

        static TimeSpan maxInterval(TimeSpan TimeInterv0, TimeSpan TimeInterv1)
        {
            return (TimeInterv0 < TimeInterv1? TimeInterv1: TimeInterv0);
        }

        static DateTime maxTime(DateTime Time0, DateTime Time1)
        {
            return (Time0 < Time1? Time1: Time0);
        }

        internal static RobinProcessor GetCurrentProcessor()
        {
            return (RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor;
        }

        // Add Activity in the last Round-Robin position
        public static void EnqueueActivity(RobinActivity activity)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            if (CircularActivityList == null) {
                NextActivity = CircularActivityList /*= GetCurrentProcessor().CurrentActivity */=
                    activity.Next = activity.Previous = activity;
                //Here modifying other processor specific data rather than global.
//                for (int i=0; i<Processor.processorTable.Length; i++) {
//                    ((RobinProcessor)Processor.processorTable[i].SchedulerProcessor).NextActivity = activity;
//                    ((RobinProcessor)Processor.processorTable[i].SchedulerProcessor).Reschedule();
//                }
                SchedulerClock.SignalOtherProcessors();
            }
            else {
                activity.Next = NextActivity;
                activity.Previous = NextActivity.Previous;
                NextActivity.Previous.Next = activity;
                NextActivity.Previous = activity;
            }
        }

        public static void DequeueActivity(RobinActivity activity) //TODO: Make an activity function?
        {
            // In our simulated environment, Activity 0 is never removed from the Q.
            Debug.Assert(Processor.InterruptsDisabled());
            //NOTE: Might be replaced with a test and return.
            Debug.Assert(activity.Next != null);
            Debug.Assert(activity.Previous != null);

            // Previous. in RoundRobin  if (activity.Id == 0) return;

            activity.Next.Previous = activity.Previous;
            activity.Previous.Next = activity.Next;
            if (CircularActivityList == activity) {
                CircularActivityList = activity.Next;
            }
            if (NextActivity == activity) {
                NextActivity = activity.Next;
            }
//            for (int i=0; i<Processor.processorTable.Length; i++) {
//                RobinProcessor processor = (RobinProcessor)Processor.processorTable[i].SchedulerProcessor;
//                if (processor.NextActivity == activity) {
//                    processor.NextActivity = activity.Next;
//                }
//            }
        }

        // if possible, places thread in the 2nd position in the RunnableThreads Q.
        // Make this a function on a resource container.
        public static void EnqueueRunThread(RobinThread thread, bool fromSleeping)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            RobinActivity activity = thread.AssociatedActivity;
            if (thread.Next != null) {
                Debug.Assert(thread.Previous != null);
                return;
            }

            //  Debug.Assert(thread.QueueType == QUEUE_NONE);

            //if (thread.State == RobinThread.ThreadState.ThreadWaiting)
            //    thread.SetState(RobinThread.ThreadState.ThreadReady);

            if (activity.RunnableThreads == null) {
                thread.Next = thread.Previous = thread;
                activity.RunnableThreads = thread;
            }
            else {
                if (fromSleeping) {
                    thread.Previous = activity.RunnableThreads;
                    thread.Next = activity.RunnableThreads.Next;
                    activity.RunnableThreads.Next.Previous = thread;
                    activity.RunnableThreads.Next = thread;
                }
                else {
                    //If we weren't sleeping/waiting, then we were running --
                    // then put thread at end of list, not 2nd.
                    thread.Next = activity.RunnableThreads;
                    thread.Previous = activity.RunnableThreads.Previous;
                    activity.RunnableThreads.Previous.Next = thread;
                    activity.RunnableThreads.Previous = thread;
                }
            }

            //Editing other processor data again.  Here we actually tell all
            //processors they need to reschedule, since there is a new thread to
            //consider.  (perhaps wrong).
            //NOTE: SignalOtherProcessors only alerts those who are halted at the
            //moment.
            for (int i=0; i<Processor.processorTable.Length; i++) {
                ((RobinProcessor)Processor.processorTable[i].SchedulerProcessor).Reschedule();
            }
            SchedulerClock.SignalOtherProcessors();
        }

        public static bool DequeueRunThread(RobinThread thread)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            if (thread.Next == null) {
                Debug.Assert(thread.Previous == null);
                return false;
            }

            if (thread.Next == thread) {
                Debug.Assert(thread.Previous == thread);
                thread.AssociatedActivity.RunnableThreads = null;
            }
            else {
                // multiple threads in the RunnableThreads Q
                thread.Previous.Next = thread.Next;
                thread.Next.Previous = thread.Previous;
                if (thread.AssociatedActivity.RunnableThreads == thread)
                    thread.AssociatedActivity.RunnableThreads = thread.Next;
            }
            thread.Next = thread.Previous = null;
            return true;
        }
#endregion

#region Additions according to kernel implementation

        static bool ResetTimerTimeout(DateTime NewTimerTimeout)
        {
            DateTime timeNow = SchedulingTime;

            if (timeNow > NewTimerTimeout) {
                return false;
            }

            Debug.Assert(Processor.InterruptsDisabled());
            // Debug.Assert(CurrentThread().GetState() == Thread.ThreadState.ThreadRunning);

            Debug.Assert(NewTimerTimeout > SchedulingTime);
            // Debug.Assert(SleepTimeout > SchedulingTime);

            //TODO: Need LA_Time?
            //            if (NewTimerTimeout > SleepTimeout - LA_Time)
            //                NewTimerTimeout = SleepTimeout - LA_Time;
            if (NewTimerTimeout > RobinThread.GetSleepTimeout()) {
                NewTimerTimeout = RobinThread.GetSleepTimeout();
            }

            //DebugStub.Print("Setting Next Interrupt for: {0}...\n",
            //__arglist(NewTimerTimeout.Ticks);
            bool success = SchedulerClock.SetNextInterrupt(NewTimerTimeout); //TODO: Perhaps only call this if the time changed.

            //DebugStub.Print(success ? "SUCCESS\n" : "FAILED\n");

            return success;
        }

        public static  void UpdateSchedulingStatus()
        {
            TimeSpan timeRun;
            RobinThread currentThread = GetCurrentThread();

            DateTime newSchedulingTime = SystemClock.GetKernelTime();

            timeRun = newSchedulingTime - SchedulingTime;
            RobinProcessor processor = (RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor;
            processor.SchedulingTime = newSchedulingTime;

            if (Idle) {
                // UpdateIdleTime
                IdleTime += timeRun;
                return;
            }

            // if IDLE compute CurrentPlanNode & set currentThread to null.
            if (currentThread != null) {
                // unless thread just exited.
                // Update Thread & Activity execution times.
                currentThread.AddExecutionTime(timeRun);
                Scheduler.CurrentTask().AddResourceAmountUsed(CpuResource.Provider().ResourceString, CpuResource.Provider().TimeToCpu(timeRun));
//                if (currentThread.AssociatedActivity != null) {
//                    currentThread.AssociatedActivity.MyRecurringCpuReservation.EnclosingCpuReservation.AddTimeUsed(timeRun);
//                }
            }

            if (OneShotReservation.CurrentReservation != null) {
                // Slice used for a reservation.
                OneShotReservation.CurrentReservation.Estimate -= timeRun;
            }
            else if (processor.CurrentActivity != null) {
                // Slice used for RoundRobin.
                processor.SliceLeft -= timeRun;
                processor.CurrentActivity = null;
            }
        }

        internal static void CheckInvariants()
        {
            if (CircularActivityList != null) {
                bool listNextFound = (CircularActivityList == NextActivity);
                RobinActivity start = CircularActivityList, current = CircularActivityList.Next, previous = CircularActivityList;
                while (current != start) {
                    //perhaps check thread invariants here too.
                    Debug.Assert(current != null, "resource container list is not circular");
                    Debug.Assert(current.Previous == previous, "back pointer doesn't match forward pointer");
                    if (current == NextActivity) {
                        listNextFound = true;
                    }
                    previous = current;
                    current = current.Next;
                }
                Debug.Assert(listNextFound, "NextActivity isn't in the loop!");
                Debug.Assert(current.Previous == previous, "Head doesn't point to tail");
            }
        }

        // Always called when another thread needs to be scheduled.
        static bool RescheduleInterrupt()
        {
            CheckInvariants();
            Debug.Assert(Processor.InterruptsDisabled());
            Debug.Assert(!Processor.InterruptsDisabled());
            RobinThread currentThread = GetCurrentThread();
            Debug.Assert(currentThread == null || currentThread.ActiveProcessor == GetCurrentProcessor());
            if (currentThread != null) {
                currentThread.ActiveProcessor = null;
            }

            DateTime nextStart;
            RobinThread previousThread;

          RescheduleAgain: // !!!!!!!!!! SIM ONLY !!!!!!!!!
            if (Idle) {
                currentThread = null;
            }

            previousThread = currentThread;

            UpdateSchedulingStatus();

            //  if thread was the head of the runnable threads Q:
            //  Advance the runnable threads Q.
            if ((currentThread != null) &&
                (currentThread.AssociatedActivity != null) &&
                (currentThread.AssociatedActivity.RunnableThreads == currentThread)) {

                currentThread.AssociatedActivity.RunnableThreads = currentThread.Next;
                Debug.Assert(currentThread.AssociatedActivity.RunnableThreads != null);
            }

            OneShotReservation.UpdateCurrentReservation();
            OneShotReservation.ClearCurrentReservation();
            currentThread = null;

            // Finished first stage, i.e. updated state.
            // Start second stage: wakeup threads & select Next CPU slice.

            RobinThread.WakeThreads();

            // NOTE: In the original RoundRobin Simulator Code (& MMOSA code)
            // The call to DrainDeferredConditions() was made here.
            // In Singularity, this will basically be replaced with a
            // queue of wait-events to fix.

            OneShotReservation.FreshenReservationQueues();

            TimeSpan currentNodeSliceLeft;
            RobinProcessor currentProcessor = (RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor;
            if (currentProcessor.CurrentActivity != null && currentProcessor.SliceLeft >= RobinScheduler.MinSlice) {
                currentThread = currentProcessor.CurrentActivity.GetRunnableThread();
                if (currentThread != null) {
                    goto exit;
                }
            }

            // Find Next Runnable Activity.
            currentProcessor.SliceLeft = RobinScheduler.RobinSlice;
            currentProcessor.CurrentActivity = NextActivity; // !!!!!!!!!!!!!SIM ONLY !!!!!!!!!!
            while ((currentThread = NextActivity.GetRunnableThread()) == null) {
                NextActivity = NextActivity.Next;
                //currentProcessor.SliceLeft = RobinScheduler.RobinSlice;
                if (NextActivity == currentProcessor.CurrentActivity) {
                    // !!!!!!!!SIM ONLY !!!!!!!
                    // in the real scheduler, execute halt
                    if (OneShotReservation.IdleReservations != null) {
                        // reuse nextStart
                        nextStart = minTime(RobinThread.GetSleepTimeout(), OneShotReservation.IdleReservations.Start);
                    }
                    else {
                        nextStart = RobinThread.GetSleepTimeout();
                        DebugStub.Print("Idle, sleeping until {0} cf maxvalue {1}\n",
                                        __arglist(nextStart.Ticks,
                                                  DateTime.MaxValue.Ticks));
                    }

                    if (nextStart == DateTime.MaxValue) {
                        Scheduler.StopSystem();
                    }

                    if (! ResetTimerTimeout(nextStart)) {
                        //Error setting timer.  Try scheduling again.
                        DebugStub.Print("Thought Idle, failed to set interrupt.\n");
                        goto RescheduleAgain;
                    }
                    GetCurrentProcessor().Idle = true;

                    // !!!!!!!!SIM ONLY !!!!!!!
                    currentThread = null;           // !!!!!!!!SIM ONLY !!!!!!!
                    OneShotReservation.ClearCurrentReservation();
                    currentProcessor.CurrentActivity = null;

                    if (DateTime.MaxValue != nextStart) {
                        //Scheduler.LogTimeJump();
                    }

                    //DebugStub.Print("Halted.\n");
                    IChangeCurrentThread(null);
                    return true;            // !!!!!!!!SIM ONLY !!!!!!!
                }
                // !!!!!!!!SIM ONLY !!!!!!!
            }
            //DebugStub.Print("Running Round Robin Resource Container\n");
            currentProcessor.CurrentActivity = NextActivity; // we probably need only one of the two variables
            NextActivity = NextActivity.Next;

          exit:
            currentNodeSliceLeft = currentProcessor.SliceLeft;

            Debug.Assert(currentThread != null);
            if (currentThread != previousThread) {
                Scheduler.LogContextSwitch(); // Context Switch statistics
            }

            if (OneShotReservation.IdleReservations != null) {
                // reuse nextStart
                nextStart = minTime(RobinThread.GetSleepTimeout(), OneShotReservation.IdleReservations.Start);
            }
            else {
                nextStart = RobinThread.GetSleepTimeout();
            }

            if (SchedulingTime + currentNodeSliceLeft /* CurrentSlice */ > nextStart) {
                currentNodeSliceLeft /* CurrentSlice */ = nextStart - currentProcessor.SchedulingTime;
            }


            Scheduler.LogReschedule();
            if (!ResetTimerTimeout(currentProcessor.SchedulingTime + currentNodeSliceLeft) || Scheduler.TimerInterruptedFlag) {
                //TODO: What do we REALLY want here?
                currentThread = null;           // !!!!!!!!SIM ONLY !!!!!!!
                OneShotReservation.ClearCurrentReservation();
                currentProcessor.CurrentActivity = null;
                goto RescheduleAgain;
            }

            Debug.Assert(currentThread.ActiveProcessor == null);
            currentThread.ActiveProcessor = GetCurrentProcessor();
            Debug.Assert(currentThread.ActiveProcessor != null);

            if (currentThread != previousThread) {
                IChangeCurrentThread(currentThread);
            }
            GetCurrentProcessor().Idle = false;

            // Not necessarily true:  Debug.Assert(!Scheduler.TimerInterruptedFlag);
            CheckInvariants();
            return false;
        }

#endregion

        public static void Reschedule()
        {
            ((RobinProcessor)Processor.CurrentProcessor.SchedulerProcessor).Reschedule();
        }

        public static void ActivityObjAddRef(RobinActivity activity)
        {
            activity.ReferenceCount++;
        }


        public static void ActivityObjRelease(RobinActivity activity)
        {
            Debug.Assert(activity.ReferenceCount >= 1);
            activity.ReleaseReference();
        }

        public static void ActivityReleaseProtected(RobinActivity activity)
        {
            Debug.Assert(activity.ReferenceCount >= 1);
            Debug.Assert(Processor.InterruptsDisabled(), "Interrupts Not Disabled!");
            activity.ReleaseReference();
        }
    }
}
