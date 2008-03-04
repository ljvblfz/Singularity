////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   RialtoScheduler.cs
//
//  Note:
//

// #define LIFO
#define ASCEND_RESERV
#define LOG_SCHEDULER_DETAILS
// #define DEBUG_TREE

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
            Rialto.RialtoScheduler.RegisterScheduler();
        }
    }
#endif
}

namespace Microsoft.Singularity.Scheduling.Rialto
{
    /// <summary>
    /// Summary description for RialtoScheduler.
    /// </summary>
    public class RialtoScheduler : ICpuScheduler
    {
#region Constants
        static readonly TimeSpan AmountCpu = CpuResource.MaxPeriod; //(MaxPeriod * 100);
        //TODO: TICKS vs. TIME_SPAN vs DATE_TIME

        public static readonly TimeSpan ContextSwitch = new TimeSpan(200);
        public static readonly TimeSpan MinSlice = new TimeSpan(10 * ContextSwitch.Ticks);
        public static readonly TimeSpan AFewSlice =  new TimeSpan(3 * MinSlice.Ticks);
        public static readonly TimeSpan RobinSlice = new TimeSpan(10 * TimeSpan.TicksPerMillisecond);
#endregion

#region Static data members
        public static OneShotReservation     ReservationFreePool;
        public static GraphNode SchedulerPlan;
        //static GraphNode           NextPlan;
        private static DateTime     changePlanTime;
        // static Mutex   SchedPlanMutex;

        public static GraphNode      CurrentPlanNode;

        private static RialtoActivity      circularActivityList;
        private static RialtoActivity      robinActivityNext;
        private static RialtoActivity      robinActivityCurrent;  // activity currently executing from

        //static TimeSpan       CurrentSlice;
        private static TimeSpan     idleTime;
        private static bool         idle;

        private static RialtoThread     currentThreadGlobal;
        public static bool          EarliestDeadlineFirstBlocked;

        private static RialtoActivity      standbyActivities; // head of the Standby Activities list
        private static RialtoActivity  currentStandbyActivity; // Standby Activity executing, if any

        private static TimeSpan     robinSliceLeft = RialtoScheduler.RobinSlice; //new TimeSpan(0);  // the round robin list, if any, and its CPU slice

        public static DateTime      SchedulingTime; // moment of the last reschedules
        private static bool         needToReschedule;
        private static RialtoThread     directSwitchTo;

        private static TimeSpan     freeCpu;            // (freeCpu/AmountCpu) * 100. == % of CPU free

        //static NodeProxy[]    nodeProxies;
        private static ArrayList    nodeProxies = new ArrayList();
        //static int            ProxyCount;
        //static int            MaxProxies;

        private static TimeSpan[][] freeSlots;       // root of helper data structure
        //static int            FreeLevels;         // levels in the helper data structures

        public static TimeSpan      LA_Time = RialtoScheduler.MinSlice;

        private static TimeSpan     minPeriod;          // minimum period in the system

        public static GraphNode          FreeNodes;           // list of free nodes in current scheduling tree
        private static GraphNode         tempFreeNodes;       // list of free nodes in new scheduling tree

        public static int           ResolutionAttempt;

        private static int hackTries;

#endregion

        static public void RegisterScheduler()
        {
            CpuResource.RegisterSystemScheduler(new RialtoScheduler());
        }

        public override ISchedulerProcessor CreateSchedulerProcessor(Processor processor)
        {
            return new RialtoProcessor(processor);
        }

        public override ISchedulerThread CreateSchedulerThread(Thread thread)
        {
            return new RialtoThread(thread);
        }

        public override ISchedulerActivity CreateSchedulerActivity()
        {
            return new RialtoActivity();
        }

        public override ISchedulerCpuReservation ReserveCpu(ISchedulerActivity schedulerActivity, CpuResourceAmount cpuAmount, TimeSpan period)
        {
            RialtoActivity activity = (RialtoActivity) schedulerActivity;

            if (activity.MyRecurringCpuReservation == null) {
                activity.MyRecurringCpuReservation = new RecurringReservation();
                activity.MyRecurringCpuReservation.Slice = new TimeSpan(0);
                activity.MyRecurringCpuReservation.Period = CpuResource.MaxPeriod;
            }
            //Activity activity = GetActivity(activityId);
            TimeSpan    oldSlice;
            TimeSpan    oldPeriod;
            TimeSpan    amount = CpuResource.Provider().CpuToTime(cpuAmount);
            GraphNode   newSchedulerPlan;
            GraphNode   oldSchedulerPlan;
            TimeSpan    deltaReservation;
            DateTime    timeNow;
#if DEBUG_TREE
            TimeSpan    requestedSlice = amount;
            TimeSpan    requestedPeriod = period;
#endif
            bool    flagContext = false;
            bool    localNeedToReschedule;
            //  bool            InterruptsDisableFlag;
            ulong   start;
            ulong   stop;

            //  if (InThreadContext()) {
            //    Mutex_Lock(&SchedPlanMutex);
            //    flagContext = true;
            //  }
            //  else {
            //    Debug.Assert(Processor.InterruptsDisabled());
            //  }
            Scheduler.LogRecurringCpuReservation(); // record fixed cost
            // hack!!!!
            hackTries = 0;

            start = Processor.CycleCount;

            localNeedToReschedule = false;
            oldSlice  = activity.MyRecurringCpuReservation.Slice;
            oldPeriod = activity.MyRecurringCpuReservation.Period;
            Debug.Assert(oldPeriod.Ticks != 0);

            if (period.Ticks == 0) {
                Debug.Assert(amount.Ticks == 0);
                period = oldPeriod;
            }
            else if (amount.Ticks != 0) {
                if (period > CpuResource.MaxPeriod) {
                    amount = new TimeSpan((amount.Ticks * CpuResource.MaxPeriod.Ticks)/ period.Ticks);
                    period = CpuResource.MaxPeriod;
                }

                if (amount < RialtoScheduler.MinSlice) {
                    amount = RialtoScheduler.MinSlice;
                    // DebugStub.Print("ReserveRecurringCpu:: S_FALSE amount < RialtoScheduler.MinSlice.\n");
                    goto Exit;
                }
            }
            else if (oldSlice.Ticks == 0) {
                // case slice == 0
                // DebugStub.Print("ReserveRecurringCpu:: S_OK amount == 0.\n");
                goto Exit;
            }
            deltaReservation = new TimeSpan((amount.Ticks * AmountCpu.Ticks) / period.Ticks - (oldSlice.Ticks * AmountCpu.Ticks)/oldPeriod.Ticks);

            // Handle a simple case first.

            if (deltaReservation > freeCpu) {
                // not enough free CPU
                //      slice  = (TIME)((freeCpu + (oldSlice * AmountCpu)/oldPeriod) * period);
                amount  = new TimeSpan((((freeCpu.Ticks + (oldSlice.Ticks * AmountCpu.Ticks)/oldPeriod.Ticks) * period.Ticks)/ AmountCpu.Ticks));
                if (amount < RialtoScheduler.MinSlice)
                    amount = new TimeSpan(0);
                // DebugStub.Print("ReserveRecurringCpu:: S_FALSE deltaReservation > freeCpu.\n");
                goto Exit;
                // return old reservation
            }

            if (IncrementalReserveActivity(activity, ref amount, ref period)) {
#if DEBUG_TREE
                DebugStub.Print("IncReservActivity: 0x{0:x} Req: {1}/{2} Get: " +
                                "{3}/{4} Activ {5}:{6}\n",
                                __arglist(
                                    activity,
                                    requestedSlice,
                                    requestedPeriod,
                                    amount,
                                    period,
                                    activity.MyRecurringCpuReservation.Slice,
                                    activity.MyRecurringCpuReservation.Period));

                timeNow = SystemClock.GetKernelTime();

                PrintSchedPlan(SchedulerPlan, timeNow);
#endif

                GetActivityRecurringCpuReservation(FreeNodes, out oldPeriod, out oldSlice);
                freeCpu = new TimeSpan((oldSlice.Ticks * AmountCpu.Ticks) / oldPeriod.Ticks);
#if VERBOSE
                DebugStub.Print("ReserveRecurringCpu:: S_OK Via Incremental Reservation.\n");
#endif
                goto Exit;
            }

            activity.MyRecurringCpuReservation.Slice  = amount;
            activity.MyRecurringCpuReservation.Period = period;

            newSchedulerPlan = BuildSchedulerPlan();
            // Scheduler.LogReservedActivity(ProxyCount);
            if (newSchedulerPlan != null) {
                // if successful
                bool iflag = Processor.DisableInterrupts();

                timeNow = SystemClock.GetKernelTime();
                if (!OneShotReservation.SatisfyAcceptedConstraint(timeNow)) {
                    Processor.RestoreInterrupts(iflag);
                    FreeSchedulerPlan(newSchedulerPlan);
                    activity.MyRecurringCpuReservation.Slice  = oldSlice;
                    activity.MyRecurringCpuReservation.Period = oldPeriod;
                    amount  = new TimeSpan((period.Ticks * freeCpu.Ticks)/AmountCpu.Ticks);
#if VERBOSE
                    DebugStub.Print("ReserveRecurringCpu:: S_FALSE SatisfyAcceptedConstraint false.\n");
#endif
                    goto Exit;
                }
                // TODO: compute time change
                //  set a timer interrupt, if needed, for the moment when the plan will
                //  change
                oldSchedulerPlan = SynchronizeSchedulerPlans(newSchedulerPlan, timeNow, ref localNeedToReschedule);
                Processor.RestoreInterrupts(iflag);

                if (oldSchedulerPlan != null) {
                    // this may execute with preemption enabled
                    FreeSchedulerPlan(oldSchedulerPlan);
                }
                GetActivityRecurringCpuReservation(activity.MyRecurringCpuReservation.AssignedNodes, out period, out amount);
                activity.MyRecurringCpuReservation.Slice  = amount;
                activity.MyRecurringCpuReservation.Period = period;

#if DEBUG_TREE
                DebugStub.Print("ReservActivity: 0x{0:x} Req: {1}/{2} Get {3}/{4} --- {5}:{6}\n",
                                __arglist(activity,
                                          requestedSlice,
                                          requestedPeriod,
                                          amount,
                                          period,
                                          activity.MyRecurringCpuReservation.Slice,
                                          activity.MyRecurringCpuReservation.Period));

                PrintSchedPlan(newSchedulerPlan, timeNow);
#endif
                GetActivityRecurringCpuReservation(FreeNodes, out oldPeriod, out oldSlice);
                freeCpu = new TimeSpan((oldSlice.Ticks * AmountCpu.Ticks) / oldPeriod.Ticks);

            }
            else {
                // otherwise return old reservation (possible null)
                Debug.Assert(oldPeriod.Ticks != 0);
                activity.MyRecurringCpuReservation.Slice  = oldSlice;
                activity.MyRecurringCpuReservation.Period = oldPeriod;
                amount = new TimeSpan((period.Ticks * freeCpu.Ticks)/AmountCpu.Ticks);
                minPeriod = SchedulerPlan.Period;
#if VERBOSE
                DebugStub.Print("ReserveRecurringCpu:: S_FALSE new scheduler plan null.\n");
#endif
            }
#if VERBOSE
            DebugStub.Print("ReserveRecurringCpu:: S_OK new scheduler plan.\n");
#endif
            Exit:
                if (flagContext) {
                    //    Debug.Assert(InThreadContext());
                    //   if (localNeedToReschedule && !DuringBootstrap()) {
                    //     RescheduleWithInterruptsEnabled();
                    //  }
                    //  localNeedToReschedule = false;
                    //  Mutex_Unlock(&SchedPlanMutex);
                }
                else {
                    if (localNeedToReschedule) {
                        localNeedToReschedule = false;
                        Reschedule();
                    }
                }
                stop = Processor.CycleCount;
#if PRINT_RESERV_OVERHEAD
                DebugStub.Print("ReservActivity internal timing: {0} cycles, " +
                                "Free CPU {0} " +
                                "hackTries {0}\n",
                                __arglist(stop-start,
                                          freeCpu,
                                          hackTries));
#endif
            if (activity.MyRecurringCpuReservation.Slice.Ticks == 0) {
                activity.MyRecurringCpuReservation = null;
            }
            return activity.MyRecurringCpuReservation;
        }

        public override bool ShouldReschedule()
        {
            RialtoThread currentThread = GetCurrentThread();
            if (Scheduler.TimerInterruptedFlag && currentThread != null) {
                //SchedulerClock.CheckInterrupt();
            }

            if (currentThread == null || Scheduler.YieldFlag || currentThread.EnclosingThread.IsWaiting() || currentThread.EnclosingThread.IsStopped()) {
                Reschedule();
            }
            return needToReschedule || Scheduler.TimerInterruptedFlag;
            // || timer should have fired but didn't?
        }

        // DI -- this is somehow similar to EnableInterrupts
        public override bool NextThread(out Thread nextThread)
        {
            Debug.Assert(!Processor.InterruptsDisabled());
            bool iflag = Processor.DisableInterrupts();
            bool halted = false;
            RialtoThread currentThread = GetCurrentThread();

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
                Debug.Assert(false);
                nextThread = null;
            }

            if (nextThread != null) {
                unchecked {
                    Tracing.Log(Tracing.Debug, "NextThread => {0}",
                                (UIntPtr)(uint)nextThread.GetThreadId());
                }
            }
            else {
                Tracing.Log(Tracing.Debug, "NextThread => none");
            }

            needToReschedule = false;
            directSwitchTo = null;
            // DebugStub.Print("-------------------------currentThread-Id {0}\n",
            // __arglist(currentThread.Id));
            //return currentThread;
            Processor.RestoreInterrupts(iflag);
            return halted;
        }

        //////////////////////////////////////////////////////////////////////
        //public static void Yield();

        public RialtoScheduler()
        {
        }

        public override void Initialize()
        {
            RialtoScheduler.InitializeScheduler();
        }

        /// <summary>
        /// Initialize types that would otherwise cause dynamic allocations at runtime with preemption disabled
        /// </summary>
        static void InitializeSchedulerTypes()
        {
            Kernel.InitType(typeof(OneShotReservation));
        }

        public static void InitializeScheduler()
        {
            InitializeSchedulerTypes();

            //TODO: Should all the null's be added in here?
            SchedulerPlan = GraphNode.InitTree();
            SchedulerPlan.NextExec = SystemClock.GetKernelTime(); //perhaps this should happen when the first thread is created, instead
            CurrentPlanNode = SchedulerPlan;
            FreeNodes    = SchedulerPlan;

            SchedulingTime = SystemClock.GetKernelTime();
            freeCpu = AmountCpu;

            minPeriod = CpuResource.MaxPeriod;

            TimeSpan currentSliceLeft = SchedulerPlan.Slice;
            currentSliceLeft /* CurrentSlice */ = minInterval(currentSliceLeft , robinSliceLeft);
            bool iflag = Processor.DisableInterrupts();
            SchedulerClock.SetNextInterrupt(SchedulingTime + currentSliceLeft);
            Processor.RestoreInterrupts(iflag);
        }

#region ISystemScheduler Members

        public override void BeginDelayedConstraint(Hashtable resourceEstimates,
                                                    TimeSpan relativeDeadline,
                                                    ISchedulerTask taskToEnd,
                                                    out ISchedulerTask schedulerTask)
        {
            TimeConstraint timeConstraint = new TimeConstraint();
            timeConstraint.Estimate = CpuResource.Provider().CpuToTime((CpuResourceAmount)resourceEstimates[CpuResource.Provider().ResourceString]);
            timeConstraint.Start = new DateTime(0); //Start now.
            timeConstraint.RelativeDeadline = relativeDeadline;
            timeConstraint.Deadline = new DateTime(0); //A 0-deadline means relative instead.
            schedulerTask = Thread.CurrentThread.SchedulerThread.PrepareDelayedTask(taskToEnd, ref timeConstraint, SystemClock.GetKernelTime());
        }

        //I'm afraid the preemption needs to be disabled here around beginBeforeWait,
        // but I feel like there was a problem.
        public override bool BeginConstraint(Hashtable resourceEstimates,
                                             DateTime deadline,
                                             ISchedulerTask taskToEnd,
                                             out ISchedulerTask schedulerTask)
        {
            DateTime timeNow = SystemClock.GetKernelTime();
            RialtoThread thread = GetCurrentThread();
            ulong start;
            ulong stop;

            Debug.Assert(!Processor.InterruptsDisabled());
            Debug.Assert(taskToEnd == null || taskToEnd == thread.ReservationStack);

            bool endPrevious = (taskToEnd != null);

            thread.IpcCheckFreeConstraint();

            start = Processor.CycleCount;
            TimeConstraint constraint = new TimeConstraint();
            constraint.Deadline = deadline;
            constraint.Estimate = CpuResource.Provider().CpuToTime((CpuResourceAmount)resourceEstimates[CpuResource.Provider().ResourceString]);
            constraint.Start = timeNow;

            bool ok = thread.BeginConstraintBeforeWaitValidate(endPrevious, ref constraint, timeNow);
            schedulerTask = thread.PendingReservation;
            if (ok) {
                OneShotReservation.BeginConstraintBeforeWait(thread, endPrevious, constraint, timeNow);
                bool iflag = Processor.DisableInterrupts();
                ok = OneShotReservation.ResolveConstraint(thread);
                Processor.RestoreInterrupts(iflag);
            }
            else {
                // DebugStub.Print("RialtoScheduler::BeginConstraintBeforeWaitValidate failed\n");
            }
            stop = Processor.CycleCount;

            Scheduler.LogBeginConstraint(thread.EnclosingThread, ok, start, stop);

            //TODO: Check if this is a necessity, or if it's only for sim time.  Call in wrapper if necessary.
            //NextThread();
            return ok;
        }

        public override bool EndConstraint(ISchedulerTask taskToEnd)
        {
            Debug.Assert(taskToEnd == GetCurrentThread().ReservationStack);
            Debug.Assert(!Processor.InterruptsDisabled());
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

#endregion

#region Reserve Activity (x out of y) support functions

        public static void FreeCurrentSchedulerPlan()
        {
            FreeSchedulerPlan(SchedulerPlan);
        }

        static void FreeSchedulerPlan(GraphNode oldPlan)
        {
            GraphNode leftNode, rightNode;
            int i;

            //    Debug.Assert(!Processor.InterruptsDisabled());
            minPeriod = SchedulerPlan.Period;

            if (oldPlan == null)
                return;
            if ((oldPlan.Type == GraphNode.NodeType.Free) || (oldPlan.Type == GraphNode.NodeType.Used)) {
                leftNode = oldPlan.Next; // reuse local
                FreeSchedulerPlan(leftNode);
            }
            else {
                leftNode = oldPlan.Left;
                rightNode = oldPlan.Right;
                FreeSchedulerPlan(leftNode);
                FreeSchedulerPlan(rightNode);
            }

            for (i = 0; i < oldPlan.ReservCount; i++) {
                //  DebugStub.Print("FreeSchedulerPlan {0} {1}\n",
                // __arglist(POINTER(oldPlan->ReservationArray[i]),
                // POINTER(oldPlan->ReservationArray[i])->ReferenceCount));
                ReleaseReservation(oldPlan.ReservationArray[i].AssociatedReservation);
            }
            if (oldPlan.Type == GraphNode.NodeType.Used) {
                Interlocked.Decrement(ref oldPlan.DefaultActivity.CountNodes);
                ActivityObjRelease(oldPlan.DefaultActivity);
            }
            oldPlan = null;    //free(oldPlan);
        }

        static int  NextProxyOnLevel(int nextProxy, int level)
        {
            for (int i = nextProxy; i < nodeProxies.Count; i++) {
                if (((NodeProxy)nodeProxies[i]).TreeLevel == level) {
                    return i;
                }
            }
            return nodeProxies.Count;
        }

        /// <summary>
        /// Search for the existence of any node with a higher TreeLevel "under" me.
        /// </summary>
        /// <param name="Level">TreeLevel (basically tree position, 2^level + place in row)</param>
        /// <returns></returns>
        static bool NextProxyOnHigherLevel(int level)
        {
            // search for a node w/ TreeLevel in a 'cone' under Level
            for (int i = 0; i < nodeProxies.Count; i++) {
                int min = (level << 1) + 1;
                int max = (level << 1) + 2;
                while (((NodeProxy)nodeProxies[i]).TreeLevel >= min) {
                    if (((NodeProxy)nodeProxies[i]).TreeLevel <= max) {
                        return true;
                    }
                    min = (min << 1) + 1;
                    max = (max << 1) + 2;
                }
            }
            return false;
        }

        static void ActivityAddNode(RialtoActivity activity)
        {
            Interlocked.Increment(ref activity.CountNodes);
            ActivityObjAddRef(activity);
        }

        static void ActivityRemoveNode(RialtoActivity activity)
        {
            int newrefcnt;

            Debug.Assert(Processor.InterruptsDisabled());
            ActivityReleaseProtected(activity);
            newrefcnt = Interlocked.Decrement(ref activity.CountNodes);

            Debug.Assert(newrefcnt >= 0);

        }

        // CK: Recursive function to build the GraphNode tree from the GraphNode Proxies.
        // NOTE: Level refers to tree position in full binary tree -- not row.
        static GraphNode BuildLevel(int Level, TimeSpan Period, TimeSpan timeLeft, int NextProxy)
        {
            int i;
            GraphNode ptemp;

            if (timeLeft < RialtoScheduler.MinSlice)
                return null;

            ptemp = new GraphNode(); //(PNODE)malloc(sizeof (GraphNode));
            //memset(ptemp, 0, sizeof(GraphNode));

            ptemp.TimeToOrigin = timeLeft;
            Debug.Assert(Period.Ticks % minPeriod.Ticks == 0);
            ptemp.Period = Period;

            if ((i = NextProxyOnLevel(NextProxy, Level)) < nodeProxies.Count) {
                // allocate an Used GraphNode

                ptemp.Type = GraphNode.NodeType.Used;
                ptemp.Slice = ((NodeProxy)nodeProxies[i]).Slice;
                Debug.Assert(ptemp.Slice >= RialtoScheduler.MinSlice);
                Debug.Assert(Period == ((NodeProxy)nodeProxies[i]).Period);
                ptemp.DefaultActivity = ((NodeProxy)nodeProxies[i]).AssociatedActivity;
                ActivityAddNode(ptemp.DefaultActivity);

                ptemp.SameActivityNext = ptemp.DefaultActivity.MyRecurringCpuReservation.TempAssignedNodes;
                ptemp.DefaultActivity.MyRecurringCpuReservation.TempAssignedNodes = ptemp;

                ptemp.Left = ptemp.Right = null;
                ptemp.Next = BuildLevel(Level, Period,
                    timeLeft - ptemp.Slice - RialtoScheduler.ContextSwitch, i + 1);
            }
            else if (NextProxyOnHigherLevel(Level)) {
                ptemp.Type = GraphNode.NodeType.LeftBranch;
                ptemp.Slice = timeLeft;
                ptemp.Next  = null;
                ptemp.Left  = BuildLevel(2 * Level + 1, Period+Period, timeLeft, 0);
                ptemp.Right = BuildLevel(2 * Level + 2, Period+Period, timeLeft, 0);
            }
            else {
                // free node
                ptemp.Type = GraphNode.NodeType.Free;
                ptemp.Slice = timeLeft;
                ptemp.Next  = null;
                ptemp.SameActivityNext = tempFreeNodes;
                tempFreeNodes = ptemp;
            }
            return ptemp;
        }

        static void FreeHelperDataStructures()
        {
            // Free the (FreeLevels) freeSlots Arrays!
            if (freeSlots != null) {
                for (int i = 0; i < freeSlots.Length; i++) {
                    freeSlots[i] = null; //free(freeSlots[i]);
                }
                if (freeSlots != null) {
                    freeSlots = null; //free(freeSlots);
                }
            }
            nodeProxies = null; //free(nodeProxies); // free the ordered array of reservations
        }

        static bool BuildHelperDataStructures()
        {
            int     count, i;
            RialtoActivity ptemp;
            TimeSpan    T;

            // Allocate 'nodeProxies', 'ordered array' of node proxies:
            nodeProxies = new ArrayList(); //NodeProxy[MaxProxies]; //(NodeProxy*) malloc(MaxProxies * sizeof(NodeProxy));
            //for (i=0; i<MaxProxies; i++) { nodeProxies.Insert(i, new NodeProxy()); } //memset(nodeProxies, 0, MaxProxies * sizeof(NodeProxy));

            //ProxyCount = 0;
            minPeriod = CpuResource.MaxPeriod;

            ptemp = circularActivityList;

            // Order Activities w/ a non-zero reservation in the 'nodeProxies' array!
            do {
                if (ptemp.MyRecurringCpuReservation != null && ptemp.MyRecurringCpuReservation.Slice.Ticks != 0) {
                    //Debug.Assert(nodeProxies.Count <= MaxProxies);
                    for (i = 0; i < nodeProxies.Count; i++) // find the right place
#if ASCEND_PERIOD //Sort the NodeProxy array in ascending order of period i.e. (out of N)
                        if ((ptemp.Period   < ((NodeProxy)nodeProxies[i]).Period) ||
                        ((ptemp.Period == ((NodeProxy)nodeProxies[i]).Period) &&
                            (ptemp.Slice   > ((NodeProxy)nodeProxies[i]).Slice)))
                            break;
# else
#if ASCEND_RESERV //Sort the NodeProxy array in descending order of fraction of CPU
                        //NOTE: The multiplication in numerator and denominator is to allow this to be done as integer math.
                        //This is testing if (ptemp.MyRecurringCpuReservation.Slice.Ticks) / ptemp.MyRecurringCpuReservation.Period.Ticks > (((NodeProxy)nodeProxies[i]).Slice.Ticks) / ((NodeProxy)nodeProxies[i]).Period.Ticks
                        //Converting to multiply only, for integer math.
                        if ((ptemp.MyRecurringCpuReservation.Slice.Ticks * ((NodeProxy)nodeProxies[i]).Period.Ticks) >
                            (((NodeProxy)nodeProxies[i]).Slice.Ticks * ptemp.MyRecurringCpuReservation.Period.Ticks))
                            break;
                    //                        if ((ptemp.MyRecurringCpuReservation.Slice.Ticks * AmountCpu.Ticks * 100) / ptemp.MyRecurringCpuReservation.Period.Ticks > //TODO: Is this enough for accurate?  Or just "close enough"?
                    //                            (((NodeProxy)nodeProxies[i]).Slice.Ticks * AmountCpu.Ticks * 100) / ((NodeProxy)nodeProxies[i]).Period.Ticks)
                    //                            break;
#else
                        break; //TODO: Does this mean anything?
#endif
# endif
                    // make room for the new activity

                    //if (i < ProxyCount)
                    //Array.Copy(nodeProxies, i, nodeProxies, i+1, ProxyCount - i);
                    //memmove(&(nodeProxies[i+1]), &(nodeProxies[i]),
                    //    sizeof(NodeProxy) * (ProxyCount - i));
                    nodeProxies.Insert(i, new NodeProxy());

                    //ProxyCount++;
                    //Debug.Assert(nodeProxies.Count <= MaxProxies);

                    ((NodeProxy)nodeProxies[i]).AssociatedActivity = ptemp;
                    ((NodeProxy)nodeProxies[i]).Slice    = ptemp.MyRecurringCpuReservation.Slice;
                    ((NodeProxy)nodeProxies[i]).Period  = ptemp.MyRecurringCpuReservation.Period;
                    ((NodeProxy)nodeProxies[i]).TreeLevel = -1;
                    if (((NodeProxy)nodeProxies[i]).Period < minPeriod)
                        minPeriod = ((NodeProxy)nodeProxies[i]).Period;
                }
                ptemp = ptemp.Next;
            } while (ptemp != circularActivityList);

            Debug.Assert(minPeriod <= CpuResource.MaxPeriod);
            int freeLevels = 0;
            freeSlots = null;
            for (i = 0; i < nodeProxies.Count; i++) {
                T = minPeriod;
                count = 0;

                while ((((NodeProxy)nodeProxies[i]).Period >= T+T) && (T+T <= CpuResource.MaxPeriod)) {
                    T = T+T;
                    count++;
                }

                if (count > freeLevels) {
                    //NOTE: Basically FreeLevels is the max height of the tree (free list)
                    freeLevels = count;
                }

                //Store the slice as the scaled slice (fraction of the period).
                ((NodeProxy)nodeProxies[i]).Slice = new TimeSpan((((NodeProxy)nodeProxies[i]).Slice.Ticks * T.Ticks)/((NodeProxy)nodeProxies[i]).Period.Ticks);

                if (((NodeProxy)nodeProxies[i]).Slice < RialtoScheduler.MinSlice) {
                    //free(nodeProxies);
                    return false;
                }
                Debug.Assert(T.Ticks % minPeriod.Ticks == 0);
                ((NodeProxy)nodeProxies[i]).Period = T;
                ((NodeProxy)nodeProxies[i]).FreeLevel = count; //CK NOTE: This is essentially the level in the tree this reservation preferably goes.
            }

            // Allocate level 0 activities:
            for (i = 0, T = minPeriod, count = 0; i < nodeProxies.Count; i++) {
                if (((NodeProxy)nodeProxies[i]).Period == minPeriod) {
                    ((NodeProxy)nodeProxies[i]).TreeLevel = 0;
                    T -= RialtoScheduler.ContextSwitch + ((NodeProxy)nodeProxies[i]).Slice;
                }
                else {
                    Debug.Assert(((NodeProxy)nodeProxies[i]).Period.Ticks % minPeriod.Ticks == 0);
                    count++;
                }
            }

            if ((count > 0) && (T < RialtoScheduler.MinSlice)) { //CK- There exist non-level-0 activities, but not enough schedule time Left for more scheduled activities
                //      free(nodeProxies);
                return false;
            }

            // Allocate the (FreeLevels) freeSlots Arrays!
            freeLevels +=1;
            freeSlots = new TimeSpan[freeLevels][]; //(FREESLOT**) malloc(FreeLevels * sizeof(FREESLOT *));
            for (i = 0; i < freeSlots.Length; i++) {
                freeSlots[i] = new TimeSpan[1 << i]; //(FREESLOT*) malloc((1 << i) * sizeof(FREESLOT));
            }

            for (i = 0; i < freeSlots.Length; i++) {
                for (count = 0; count < (1 << i); count++) {
                    freeSlots[i][count] = T; //CK- Initialize all free slots to have T (time left after level-0 activities)
                }
            }

            return true;
        }

        // <summary>
        // Returns the first index of a NodeProxy which has TreeLevel == -1
        // </summary>
        static int NextProxy(int ProxyIndex)
        {
            while ((ProxyIndex < nodeProxies.Count) &&
                   (((NodeProxy)nodeProxies[ProxyIndex]).TreeLevel != -1)) {
                ProxyIndex++;
            }
            return ProxyIndex;
        }

        // <summary>
        // Returns NextProxy(0)
        // </summary>
        static int FirstProxy()
        {
            return NextProxy(0);
        }

        /// <summary>
        /// Updates freeSlots by subtracting the slice from all appropriate descendants and parents.
        /// </summary>
        /// <param name="AllocLevel"></param>
        /// <param name="AllocPosition"></param>
        /// <param name="Slice"></param>
        static void SubtractSlice(int AllocLevel, int AllocPosition, TimeSpan Slice)
        {
            int Level, Position, Sibling, ParentPosition, i;

            freeSlots[AllocLevel][AllocPosition] -= Slice;

            Debug.Assert(freeSlots[AllocLevel][AllocPosition].Ticks >= 0);

            // update Free Slots; the larger periods first:
            // CK- At all points in time, freeSlots seems to keep the time available at the slot, to adding to a place in the tree requires reducing all descendants
            for (i = 1; AllocLevel + i < freeSlots.Length; i++) {
                for (Position = AllocPosition * (1 << i);
                     Position < (AllocPosition + 1) * (1 << i); Position++) {
                    Debug.Assert(freeSlots[AllocLevel + i][Position] >= Slice);
                    freeSlots[AllocLevel + i][Position] -= Slice;
                }
            }

            // smaller periods Next:
            // CK- Update parent(s) if we now have less than our sibling. (parent is min of me and sibling).
            for (Level = AllocLevel, Position = AllocPosition;
                 Level > 0; Level--, Position = ParentPosition) {
                Sibling = (Position & 0x01)==1 ? Position - 1 : Position + 1;
                if (freeSlots[Level][Position] >= freeSlots[Level][Sibling]) {
                    break; // no need to go further
                }
                ParentPosition = Position >> 1;
                freeSlots[Level - 1][ParentPosition] = freeSlots[Level][Position];
            }
        }

        /// <summary>
        /// Updates freeSlots by adding the slice to all appropriate descendants and parents.
        /// </summary>
        /// <param name="AllocLevel"></param>
        /// <param name="AllocPosition"></param>
        /// <param name="Slice"></param>
        static void AddSlice(int AllocLevel, int AllocPosition, TimeSpan Slice)
        {
            int Level, Position, Sibling, ParentPosition, i;

            freeSlots[AllocLevel][AllocPosition] += Slice;

            //Update Free Slots; the larger periods first:
            for (i = 1; AllocLevel + i < freeSlots.Length; i++) {
                for (Position = AllocPosition * (1 << i);
                     Position < (AllocPosition + 1) * (1 << i);
                     Position++) {
                    freeSlots[AllocLevel + i][Position] += Slice;
                }
            }

            // smaller periods Next:
            for (Level = AllocLevel, Position = AllocPosition;
                 Level > 0; Level--, Position = ParentPosition) {
                Sibling = (Position & 0x01)==1 ? Position - 1 : Position + 1;
                ParentPosition = Position >> 1;
                if (freeSlots[Level][Position] <= freeSlots[Level][Sibling]) {
                    freeSlots[Level - 1][ParentPosition] = freeSlots[Level][Position];
                }
                else {
                    freeSlots[Level - 1][ParentPosition] = freeSlots[Level][Sibling];
                }
            }
        }

        // Temporarily, it disrupts the order of the 'NodeProxy' array
        static void SplitSlice(int ProxyIndex, TimeSpan AllocSlice)
        {
            Debug.Assert(AllocSlice >= RialtoScheduler.MinSlice);

            //            if (nodeProxies.Count == MaxProxies) {
            //                MaxProxies += GraphNode.MaxProxySplits;
            //                //ArrayList already takes care of the allocation
            //                //nodeProxies = (NodeProxy*) realloc(nodeProxies, MaxProxies * sizeof(NodeProxy));
            //            }
            //memmove(&nodeProxies[ProxyIndex + 1], &nodeProxies[ProxyIndex],
            //    sizeof(NodeProxy) * (ProxyCount - ProxyIndex));

            //ProxyCount++;
            nodeProxies.Insert(ProxyIndex+1, ((NodeProxy)nodeProxies[ProxyIndex]).Clone());
            //Debug.Assert(nodeProxies.Count <= MaxProxies);

            ((NodeProxy)nodeProxies[ProxyIndex + 1]).Slice = ((NodeProxy)nodeProxies[ProxyIndex]).Slice - AllocSlice;
            ((NodeProxy)nodeProxies[ProxyIndex]).Slice = AllocSlice;
        }

        // Expects the split to be performed by 'SplitSlice' above.
        static void RestoreSlice(int ProxyIndex)
        {
            Debug.Assert(((NodeProxy)nodeProxies[ProxyIndex + 1]).AssociatedActivity == ((NodeProxy)nodeProxies[ProxyIndex]).AssociatedActivity);

            ((NodeProxy)nodeProxies[ProxyIndex]).Slice += ((NodeProxy)nodeProxies[ProxyIndex + 1]).Slice;

            nodeProxies.RemoveAt(ProxyIndex+1);
        }

        /// <summary>
        /// Next largest less than previous max.
        /// </summary>
        /// <param name="PrevMax"></param>
        /// <param name="PrevIndex"></param>
        /// <param name="Level"></param>
        /// <returns></returns>
        static int NextLargestFreeSlot(TimeSpan PrevMax, int PrevIndex, int Level)
        {
            TimeSpan[]  LevelSlots;
            int         position, MaxPosition = (1 << Level);
            TimeSpan        MaxValue = RialtoScheduler.MinSlice;

            LevelSlots = freeSlots[Level];
            for (position = 0; position < LevelSlots.Length; position++) {
                if ((LevelSlots[position] < PrevMax) ||
                    ((LevelSlots[position] == PrevMax) && (position > PrevIndex))) {
                    if (LevelSlots[position] > MaxValue) {
                        MaxPosition = position;
                        MaxValue    = LevelSlots[position];
                    }
                }
            }
            return MaxPosition;
        }

        /// <summary>
        /// Assigns Activities (w/ non-zero reservations) to their Scheduling Tree Branches/SubTrees
        /// Recursive.
        /// </summary>
        /// <param name="ProxyIndex"></param>
        /// <returns></returns>
        static bool AssignBranch(int ProxyIndex)
        {
            int         level, position;
            TimeSpan    slice, FirstHalf;
            TimeSpan[]  LevelSlots;

            if (hackTries++ > GraphNode.MaxMarcelTries) {
                return false;
            }
            if (ProxyIndex >= nodeProxies.Count) {
                return true;
            }
            level = ((NodeProxy)nodeProxies[ProxyIndex]).FreeLevel;
            Debug.Assert(level >= 0);

            LevelSlots = freeSlots[level];
            slice = ((NodeProxy)nodeProxies[ProxyIndex]).Slice;
            if (slice < RialtoScheduler.MinSlice) {
                return false;
            }

            // Search for a fit that will leave enough free time in slot:
            for (position = NextLargestFreeSlot(TimeSpan.MaxValue, 0, level);
                 position < LevelSlots.Length;
                 position = NextLargestFreeSlot(LevelSlots[position], position, level)) {
                if (LevelSlots[position] - slice - RialtoScheduler.ContextSwitch >= RialtoScheduler.MinSlice) {
                    // try allocation
                    SubtractSlice(level, position, slice + RialtoScheduler.ContextSwitch);
                    //Note: TreeLevel is a composite number.  Its value defines both the freelevel and the
                    //position within that level.  It's of magnitude 1 << level, guaranteeing that
                    //position is less than 1 << level, so you can tell the level by the magnitude, and the
                    //position by looking at the remainder, so to speak.
                    ((NodeProxy)nodeProxies[ProxyIndex]).TreeLevel = (1 << level) - 1 + position;
                    if (AssignBranch(NextProxy(ProxyIndex))) {
                        // try allocate Next activity
                        return true; // look for A solution, not for the BEST one
                    }

                    ((NodeProxy)nodeProxies[ProxyIndex]).TreeLevel = -1;
                    AddSlice(level, position, slice + RialtoScheduler.ContextSwitch); // restore the freeSlots[] values
                }
                else {
                    //Since the slot is only getting smaller, no need to continue looking.
                    break;
                }
            }

            // Search for a 'perfect fit':
            for (position = 0; position < LevelSlots.Length; position++) {
                if ((slice <= LevelSlots[position]) && // try allocation
                    (LevelSlots[position] - slice < (RialtoScheduler.AFewSlice))) {

                    ((NodeProxy)nodeProxies[ProxyIndex]).Slice = LevelSlots[position];
                    SubtractSlice(level, position, LevelSlots[position]);

                    //Note: TreeLevel is a composite number.  Its value defines both the freelevel and the
                    //position within that level.  It's of magnitude 1 << level, guaranteeing  that
                    //position is less than 1 << level, so you can tell the level by the magnitude, and the
                    //position by looking at the remainder, so to speak.
                    ((NodeProxy)nodeProxies[ProxyIndex]).TreeLevel = (1 << level) - 1 + position; // assign branch
                    if (AssignBranch(NextProxy(ProxyIndex))) {
                        // try allocate Next activity
                        return true; // return if successful (look for A solution, not for the BEST one)
                    }
                    ((NodeProxy)nodeProxies[ProxyIndex]).Slice = slice;
                    ((NodeProxy)nodeProxies[ProxyIndex]).TreeLevel = -1;
                    AddSlice(level, position, LevelSlots[position]); // restore the freeSlots[] values
                }
            }

            // We'll have to split the slice:
            for (position = NextLargestFreeSlot(TimeSpan.MaxValue, 0, level);
                position < LevelSlots.Length;
                position = NextLargestFreeSlot(LevelSlots[position], position, level)) {

                FirstHalf = minInterval(LevelSlots[position] - RialtoScheduler.MinSlice - RialtoScheduler.ContextSwitch,
                    slice - RialtoScheduler.MinSlice - RialtoScheduler.ContextSwitch);
                if (FirstHalf >= RialtoScheduler.MinSlice) {
                    Debug.Assert(LevelSlots[position] >= RialtoScheduler.MinSlice + FirstHalf);
                    SplitSlice(ProxyIndex, FirstHalf);
                    SubtractSlice(level, position, FirstHalf + RialtoScheduler.ContextSwitch);
                    //Note: TreeLevel is a composite number.  Its value defines both the freelevel and the
                    //position within that level.  It's of magnitude 1 << level, guaranteeing  that
                    //position is less than 1 << level, so you can tell the level by the magnitude, and the
                    //position by looking at the remainder, so to speak.
                    ((NodeProxy)nodeProxies[ProxyIndex]).TreeLevel = (1 << level) - 1 + position;
                    if (AssignBranch(NextProxy(ProxyIndex))) {
                        return true; // look for A solution, not for the BEST one
                    }
                    ((NodeProxy)nodeProxies[ProxyIndex]).TreeLevel = -1;
                    AddSlice(level, position, FirstHalf + RialtoScheduler.ContextSwitch); // restore Previous freeSlots[]
                    RestoreSlice(ProxyIndex);
                }
                else {
                    //No point in continuing, the slots only get smaller.
                    break;
                }
            }
#if NOT_DEFINED
            if (slice > (RialtoScheduler.MinSlice << 1)) {
                FirstHalf = slice >> 1;
                for (position = 0; position < LevelSlots.Length; position++) {
                    if (LevelSlots[position] - FirstHalf - RialtoScheduler.ContextSwitch >= RialtoScheduler.MinSlice) {
                        // try allocation w/ split
                        Debug.Assert(LevelSlots[position] >= RialtoScheduler.MinSlice);
                        SplitSlice(ProxyIndex, FirstHalf);
                        SubtractSlice(level, position, FirstHalf + RialtoScheduler.ContextSwitch);
                        nodeProxies[ProxyIndex].TreeLevel = (1 << level) - 1 + position;
                        if (AssignBranch(NextProxy(ProxyIndex))) {
                            return true; // look for A solution, not for the BEST one
                        }
                        nodeProxies[ProxyIndex].TreeLevel = -1;
                        AddSlice(level, position, FirstHalf + RialtoScheduler.ContextSwitch); // restore Previous freeSlots[]
                        RestoreSlice(ProxyIndex);
                    }
                }
            }
#endif
            // Sorry....
            return false;
        }

        static GraphNode BuildSchedulerPlan()
        {
            GraphNode newPlan = null;

            if (circularActivityList == null) {
                // build a 'large' free node
                newPlan = GraphNode.InitTree();
            }
            else {
                if (BuildHelperDataStructures()) {
                    // build the sorted nodeProxies and freeSlots arrays

                    Scheduler.LogReservedActivity(nodeProxies.Count); // record additional cost
                    if (AssignBranch(FirstProxy())) {
                        newPlan = BuildLevel(0,     // Level
                                             minPeriod,// Period
                                             minPeriod,// timeLeft
                                             0);  // NextProxy
                    }
                }
            }
            FreeHelperDataStructures();
            return newPlan;
        }

        // TODO: (OLD) look only to the list of nodes of the activity
        static TimeSpan ReservedPeriod(GraphNode plan, RialtoActivity activity)
        {
            if (plan == null) {
                return new TimeSpan(0);
            }

            switch(plan.Type)
            {
                case GraphNode.NodeType.Used:
                    if (plan.DefaultActivity == activity) {
                        return maxInterval(plan.Period, ReservedPeriod(plan.Next, activity));
                    }
                    else {
                        return ReservedPeriod(plan.Next, activity);
                    }
                case GraphNode.NodeType.Free:
                    return ReservedPeriod(plan.Next, activity);
                default:  // branch node
                    return maxInterval(ReservedPeriod(plan.Left, activity),
                        ReservedPeriod(plan.Right, activity));
            }
        }

        // TODO  - look only to the list of nodes of the activity
        static TimeSpan ReservedSlice(GraphNode plan, TimeSpan period, RialtoActivity activity)
        {
            if (plan == null) {
                return new TimeSpan(0);
            }

            switch(plan.Type)
            {
                case GraphNode.NodeType.Used:
                    if (plan.DefaultActivity == activity) {
                        return (new TimeSpan((plan.Slice.Ticks * period.Ticks) / plan.Period.Ticks) +
                                ReservedSlice(plan.Next, period, activity));
                    }
                    else {
                        return ReservedSlice(plan.Next, period, activity);
                    }
                case GraphNode.NodeType.Free:
                    return ReservedSlice(plan.Next, period, activity);
                default: // branch node
                    return ReservedSlice(plan.Left, period, activity) +
                        ReservedSlice(plan.Right, period, activity);
            }
        }

        static void  GetActivityRecurringCpuReservation(GraphNode startList, out TimeSpan period, out TimeSpan slice)
        {
            GraphNode node;
            TimeSpan  tempPeriod, tempSlice;

            if ((node = startList) == null) {
                period = CpuResource.MaxPeriod;
                slice = new TimeSpan(0);
                return;
            }
            tempPeriod = new TimeSpan(0);
            tempSlice = new TimeSpan(0);
            while (node != null) {
                if (node.Period > tempPeriod) tempPeriod = node.Period;
                node = node.SameActivityNext;
            }
            Debug.Assert(tempPeriod.Ticks != 0);
            node = startList;
            while (node != null) {
                tempSlice += new TimeSpan((node.Slice.Ticks * tempPeriod.Ticks) / node.Period.Ticks);
                node = node.SameActivityNext;
            }
            period = tempPeriod;
            slice = tempSlice;
            return;
        }

        static void SetNextExec(GraphNode plan, DateTime time)
        {
            if (plan == null) {
                return;
            }

            plan.NextExec = time;

            if (plan.Type == GraphNode.NodeType.LeftBranch) {
                SetNextExec(plan.Left, time);
                SetNextExec(plan.Right, time + plan.Period);
            }
            else if (plan.Type == GraphNode.NodeType.RightBranch) {
                SetNextExec(plan.Left, time + plan.Period);
                SetNextExec(plan.Right, time);
            }
            else
                SetNextExec(plan.Next, time + plan.Slice + RialtoScheduler.ContextSwitch);

        }

        static GraphNode SynchronizeSchedulerPlans(GraphNode newPlan,
                                                   DateTime timeChange,
                                                   ref bool localNeedToReschedule)
        {
            GraphNode oldPlan;
            RialtoActivity activity = circularActivityList; // any pointer into the activity circular list works
            Debug.Assert(Processor.InterruptsDisabled());

            // set the pointers to the Used and Free nodes in the new plan:
            FreeNodes = tempFreeNodes;
            tempFreeNodes = null;
            do {
                if (activity.MyRecurringCpuReservation != null && activity.MyRecurringCpuReservation.Slice.Ticks > 0) {
                    activity.MyRecurringCpuReservation.AssignedNodes = activity.MyRecurringCpuReservation.TempAssignedNodes;
                    activity.MyRecurringCpuReservation.TempAssignedNodes = null;
                }
                activity = activity.Next;
            } while (activity != circularActivityList);

#if VERBOSE
            DebugStub.Print("SettingNextExec: {0}\n", __arglist(timeChange.Ticks));
#endif
            SetNextExec(newPlan, timeChange); // TODO: extend later!

            oldPlan = SchedulerPlan;
            SchedulerPlan = newPlan;
            CurrentPlanNode = SchedulerPlan;
            localNeedToReschedule = true;

            return oldPlan;
        }

        static void ClearActivityNodes(GraphNode startNode,
                                       RialtoActivity activity)
        {
            GraphNode temp;

            if (startNode == null) {
                return;
            }

            temp = startNode;
            Debug.Assert((temp.Type == GraphNode.NodeType.Used) && (temp.DefaultActivity == activity));

            while (temp.SameActivityNext != null) {
                ActivityRemoveNode(activity);
                temp.Type = GraphNode.NodeType.Free;
                temp = temp.SameActivityNext;
                Debug.Assert((temp.Type == GraphNode.NodeType.Used) && (temp.DefaultActivity == activity));
            }

            ActivityRemoveNode(activity);   // for the last node in the list
            temp.Type = GraphNode.NodeType.Free;
            temp.DefaultActivity = null;

            // this should be non preemptible
            temp.SameActivityNext = FreeNodes;
            FreeNodes = startNode;
        }

        // attaches nodes found to TempAssignedNodes
        static bool AllocateActivityNodes(RialtoActivity activity,
                                          ref TimeSpan slice,
                                          ref TimeSpan period)
        {
            GraphNode node, bestNode = null;
            GraphNode tmpNode, tmpBest = null;
            TimeSpan  adjustedSlice, bestPeriod = new TimeSpan(0), diffSlice;
            bool  split = false;
            bool iflag = false; //So far we haven't disabled preemption.
            TimeSpan bestDiffSlice = new TimeSpan(0), currentPeriod;
            bool currentSplit;
            GraphNode rightNode, leftNode, next, free;
            for (tmpNode = null, node = FreeNodes; node != null; tmpNode = node, node = node.SameActivityNext) {
                //node = *tmpNode;
                Debug.Assert(node.Type == GraphNode.NodeType.Free);

                if (node.Period > period ||
                    node.Period < bestPeriod) {
                    continue;
                }

                // the activity may be assigned a node with reservations
                //  this increases the chances to find a node, although
                //  with such a node, the activity will not fully benefit of the
                //  allocation until the reservations are finished
                //  The alternative to the current choice:
                //  if (node.ReservCount > 0)
                //      continue;

                // S97 -- if the request's period is larger than the maximum period
                //        in the system, extend the tree with branch and activity
                //        nodes instead of converting the reservation to the maximum
                //        existing period
                //     -- is a request's period is equal to some period in the system, pick
                //        the best free node (not the first fit node) to make the
                //        reservation from.

                // - node.Next != null -- can not be transformed in a branch node
                //                        apply the scaling technique
                // - node.Next == null
                if (node.Next != null || node.ReservCount != 0) {
                    currentPeriod = node.Period;
                }
                else {
                    currentPeriod = period;
                }
                diffSlice = new TimeSpan(-1);


                while (currentPeriod >= node.Period) {
                    adjustedSlice = new TimeSpan((slice.Ticks * currentPeriod.Ticks)/ period.Ticks);
                    if (adjustedSlice < RialtoScheduler.MinSlice) {
                        break;
                    }
                    diffSlice = node.Slice - adjustedSlice - RialtoScheduler.ContextSwitch;
                    if (diffSlice.Ticks >= 0) {
                        break;
                    }
                    currentPeriod = new TimeSpan(currentPeriod.Ticks / 2);
                }
                if (diffSlice.Ticks < 0) {
                    continue;
                }

                if (diffSlice >= RialtoScheduler.AFewSlice) {
                    currentSplit = true;
                }
                else {
                    currentSplit = false;
                }

                if (currentSplit && diffSlice < RialtoScheduler.MinSlice) {
                    // the remaining slice is too long to be entirely reserved for the
                    // current request, but is also too small to represent an
                    // acceptable slice
                    continue;
                }
                // assume accepted

                if (bestNode == null ||    // no previous fit
                    currentPeriod > bestPeriod ||  // larger period
                    // at equal periods...
                    (split != currentSplit && split) || // the no split has priority
                    (split == currentSplit &&          // at the same split type...
                     // is split, larger diff is better, and otherwise,
                     // smaller diff is better
                    ((currentSplit  && bestDiffSlice < diffSlice) ||
                    (!currentSplit && bestDiffSlice > diffSlice)))) {
                    bestNode = node;
                    tmpBest = tmpNode;
                    bestPeriod = currentPeriod;
                    bestDiffSlice = diffSlice;
                    split = currentSplit;
                }
            }

            if (bestNode == null) {
                return false;
            }

            currentPeriod = bestNode.Period;
            node = null;
            adjustedSlice = new TimeSpan((slice.Ticks * bestPeriod.Ticks)/ period.Ticks);

            if (currentPeriod != bestPeriod) {
                // not the same period: need to extend the tree up to bestPeriod.

                // create the subtree, but do not modify yet the bestNode (try
                // to do atomically the modifications that require preemption disabled.
                rightNode = new GraphNode(); //(PNODE)malloc(sizeof(GraphNode));
                //memset(rightNode, 0, sizeof(GraphNode));
                leftNode = new GraphNode(); //(PNODE)malloc(sizeof(GraphNode));
                //memset(leftNode, 0, sizeof(GraphNode));

                TimeSpan halfPeriod = currentPeriod; //CK TEST

                // set rightNode as a free node except for SameActivityNext
                currentPeriod += currentPeriod;
                rightNode.Type = GraphNode.NodeType.Free;
                rightNode.Period = currentPeriod;
                rightNode.Slice = bestNode.Slice;
                rightNode.NextExec = bestNode.NextExec + halfPeriod; //CK TEST currentPeriod
                rightNode.TimeToOrigin = bestNode.TimeToOrigin;
                free = rightNode;
                // set leftNode as a branch node.
                node = leftNode;

                for (;;) {
                    node.Type = GraphNode.NodeType.LeftBranch;
                    node.Period = currentPeriod;
                    Debug.Assert(currentPeriod.Ticks % minPeriod.Ticks == 0);
                    node.Slice = bestNode.Slice;
                    node.NextExec = bestNode.NextExec;
                    node.TimeToOrigin = bestNode.TimeToOrigin;

                    if (currentPeriod == bestPeriod) {
                        break;
                    }

                    halfPeriod = currentPeriod; //CK TEST
                    currentPeriod += currentPeriod;

                    // set the right branch.
                    next = new GraphNode(); // (GraphNode)malloc(sizeof(GraphNode));
                    //memset(Next, 0, sizeof(GraphNode));
                    next.Type = GraphNode.NodeType.Free;
                    next.Period = currentPeriod;
                    Debug.Assert(currentPeriod.Ticks % minPeriod.Ticks == 0);
                    next.Slice = bestNode.Slice;
                    next.NextExec = bestNode.NextExec + halfPeriod; //CK TEST currentPeriod
                    next.TimeToOrigin = bestNode.TimeToOrigin;
                    next.SameActivityNext = free;
                    free = next;

                    node.Right = next;
                    next = new GraphNode(); // (GraphNode)malloc(sizeof(GraphNode));
                    //memset(Next, 0, sizeof(GraphNode));
                    node.Left = next;
                    node = next;
                }
                if (split) {
                    next = new GraphNode();
                    iflag = Processor.DisableInterrupts();
                    // insert Used node before the Free node:
                    next.Slice = node.Slice - adjustedSlice - RialtoScheduler.ContextSwitch;
                    node.Slice = adjustedSlice;
                    node.Next  = next;

                    next.Type            = GraphNode.NodeType.Free;
                    next.NextExec      = node.NextExec + node.Slice + RialtoScheduler.ContextSwitch;
                    next.TimeToOrigin    = node.TimeToOrigin - node.Slice - RialtoScheduler.ContextSwitch;
                    next.SameActivityNext = free;
                    free = next;

                    // now set all the characteristics the new node
                    next.Period    = node.Period;
                    Debug.Assert(node.Period.Ticks % minPeriod.Ticks == 0);
                    Debug.Assert(next.TimeToOrigin.Ticks >= 0);
                }
                else {
                    iflag = Processor.DisableInterrupts();
                }

                // modify bestNode.
                bestNode.Type = GraphNode.NodeType.LeftBranch;
                bestNode.Slice = bestNode.Slice;
                bestNode.Left = leftNode;
                bestNode.Right = rightNode;
                if (tmpBest == null) {
                    FreeNodes = bestNode.SameActivityNext;
                }
                else {
                    tmpBest.SameActivityNext = bestNode.SameActivityNext;
                }

                // add the new free nodes to the global list.
                rightNode.SameActivityNext = FreeNodes;
                FreeNodes = free;
            }
            else {
                // allocate node at bestNode's period.

                if (split) {
                    node = new GraphNode(); //(PNODE)malloc(sizeof(GraphNode));
                    //memset(node, 0, sizeof(GraphNode));

                    // insert Used node before Free node:
                    node.Slice     = bestNode.Slice - adjustedSlice - RialtoScheduler.ContextSwitch;
                    node.NextExec      = bestNode.NextExec + adjustedSlice + RialtoScheduler.ContextSwitch;
                    node.TimeToOrigin    = bestNode.TimeToOrigin - adjustedSlice - RialtoScheduler.ContextSwitch;
                    node.Period    = bestNode.Period;
                    Debug.Assert(node.TimeToOrigin.Ticks >= 0);
                    Debug.Assert(node.Period.Ticks % minPeriod.Ticks == 0);

                    iflag = Processor.DisableInterrupts();
                    bestNode.Slice  = adjustedSlice;
                    bestNode.Next        = node;
                    if (tmpBest == null) {
                        FreeNodes = bestNode.SameActivityNext;
                    }
                    else {
                        tmpBest.SameActivityNext = bestNode.SameActivityNext;
                    }

                    if ((node.ReservCount = bestNode.ReservCount) > 0) {
                        int i;
                        node.ReservationArray = (ReservationSlice[])bestNode.ReservationArray.Clone();
                        //memcpy(node.ReservationArray, bestNode.ReservationArray,
                        //    sizeof(ReservationSlice) * node.ReservCount);
                        for (i = 0; i < node.ReservCount; i++) {
                            node.ReservationArray[i] = (ReservationSlice)node.ReservationArray[i].Clone();
                            AddRefReservation(node.ReservationArray[i].AssociatedReservation);
                        }
                    }
                    node.SameActivityNext = FreeNodes;
                    FreeNodes = node;
                }
                else {
                    if (tmpBest == null) {
                        FreeNodes = bestNode.SameActivityNext;
                    }
                    else {
                        tmpBest.SameActivityNext = bestNode.SameActivityNext;
                    }
                }
                node = bestNode;
            }

            node.Type      = GraphNode.NodeType.Used;
            node.DefaultActivity = activity;
            node.SameActivityNext  = activity.MyRecurringCpuReservation.TempAssignedNodes;
            activity.MyRecurringCpuReservation.TempAssignedNodes = node;

            Processor.RestoreInterrupts(iflag);

            ActivityAddNode(activity);

            slice  = node.Slice;
            period = node.Period;
            Debug.Assert(period.Ticks % minPeriod.Ticks == 0);

            return true;
        }

        static bool IncrementalReserveActivity(RialtoActivity activity,
                                                        ref TimeSpan slice,
                                                        ref TimeSpan period)
            // preemption disabled for the entire or part of the function
        {
            // UNLESS success is returned, DON'T modify slice and period
            TimeSpan    newSlice, newPeriod;
            GraphNode    node;

            Debug.Assert(period <= CpuResource.MaxPeriod);
            // adjust Period and scale Slice down
            if (slice.Ticks == 0 && activity.MyRecurringCpuReservation.Slice.Ticks > 0) {
                bool iflag = Processor.DisableInterrupts();
                node = activity.MyRecurringCpuReservation.AssignedNodes;
                activity.MyRecurringCpuReservation.AssignedNodes = null;
                ClearActivityNodes(node, activity);
                activity.MyRecurringCpuReservation.Slice  = slice;
                activity.MyRecurringCpuReservation.Period = period;

                Processor.RestoreInterrupts(iflag);
                return true;
            }
            if (period < minPeriod) {
                return false;
            }

            for (newPeriod = minPeriod; newPeriod + newPeriod <= period; newPeriod += newPeriod) {
                // no body.
            }
            newSlice  = new TimeSpan((slice.Ticks * newPeriod.Ticks)/ period.Ticks);

            if ((activity.MyRecurringCpuReservation.Slice.Ticks > 0) && (activity.MyRecurringCpuReservation.Period > newPeriod)) {
                // new period smaller than previous one
                if (AllocateActivityNodes(activity, ref newSlice, ref newPeriod)) {
                    // uses TempAssignedNodes
                    Debug.Assert((newPeriod <= period) && (newSlice.Ticks >= (slice.Ticks * newPeriod.Ticks)/ period.Ticks));
                    Debug.Assert(newPeriod.Ticks != 0);
                    bool iflag = Processor.DisableInterrupts();
                    node = activity.MyRecurringCpuReservation.AssignedNodes;
                    activity.MyRecurringCpuReservation.AssignedNodes = activity.MyRecurringCpuReservation.TempAssignedNodes;
                    activity.MyRecurringCpuReservation.TempAssignedNodes = null;
                    activity.MyRecurringCpuReservation.Slice  = slice  = newSlice;
                    activity.MyRecurringCpuReservation.Period = period = newPeriod;
                    Debug.Assert(period == newPeriod);
                    Debug.Assert(newPeriod.Ticks % minPeriod.Ticks == 0);

                    ClearActivityNodes(node, activity);
                    Processor.RestoreInterrupts(iflag);
                    return true;
                }
                else {
                    return false;
                }
            }


            // compute the additional slice you need to allocate
            newSlice  -= new TimeSpan((activity.MyRecurringCpuReservation.Slice.Ticks * newPeriod.Ticks)/activity.MyRecurringCpuReservation.Period.Ticks);
            Debug.Assert(newSlice.Ticks >= 0);
            if (newSlice < RialtoScheduler.MinSlice) {
                return false;
            }

            if (AllocateActivityNodes(activity, ref newSlice, ref newPeriod)) {
                freeCpu -= new TimeSpan((newSlice.Ticks * AmountCpu.Ticks)/newPeriod.Ticks);

                // transfer the new nodes from the TempAssignedNodes list to the AssignedNodes list:
                node = activity.MyRecurringCpuReservation.TempAssignedNodes;

                while (node.SameActivityNext != null) {
                    node = node.SameActivityNext;
                }

                bool iflag = Processor.DisableInterrupts();
                node.SameActivityNext = activity.MyRecurringCpuReservation.AssignedNodes;
                activity.MyRecurringCpuReservation.AssignedNodes = activity.MyRecurringCpuReservation.TempAssignedNodes;
                activity.MyRecurringCpuReservation.TempAssignedNodes = null;
                GetActivityRecurringCpuReservation(activity.MyRecurringCpuReservation.AssignedNodes, out newPeriod, out newSlice);
                activity.MyRecurringCpuReservation.Slice  = slice  = newSlice;
                activity.MyRecurringCpuReservation.Period = period = newPeriod;
                Processor.RestoreInterrupts(iflag);

                Debug.Assert(newPeriod.Ticks != 0);
                // *pNewSlice is the total slice, not only the additional one

                Debug.Assert((newPeriod <= period) && (newSlice.Ticks  >= (slice.Ticks * newPeriod.Ticks)/ period.Ticks));
                return true;
            }
            return false;
        }

        // Reserve Activity (x out of any y) support functions end.
        // ----------------------------------------------------------------------------------

#endregion

        // NOTE: 6/15/2004.  This comment copied verbatim from the SOSP'97.  As such, it may not
        // be fully accurate.  Read for yourself.
        //
        // In the following, reservation refers to a data structure allocated
        // when 'BeginConstraint' is executed.
        //
        // IMPORTANT:
        // The OneShotReservation data structure is always allocated, whether
        // the constraint is found feasible or not.
        // The OneShotReservation data structure is freed after the
        // matching 'EndConstraint' call is executed AND the estimate is exhausted.
        // Constraints that are not found feasible are assigned a zero estimate.
        //
        // At 'BeginConstraint', the new reservation is placed on a stack associated with
        // the issuing thread; ReservationStack (in Thread) points to the top of the stack and
        // SurroundingReservation (in OneShotReservation) points to the Next reservation on the stack.
        // Reservations are removed from this stack at 'EndConstraint'.
        // The stack mirrors the 'nesting' relationship between constraints.
        //
        // In addition, new reservations are placed on a double linked list:
        // - feasible constraints on the 'GuaranteedReservations' list or, if Start < CurrentTime,
        //   on the 'IdleReservations' list.
        // - constraints found non-feasible are placed on the 'UnfinishedConstraints' list
        //   associated with the activity of the issuing thread; since 'GetRunnableThread' looks for
        //   a runnable thread in the 'UnfinishedConstraints' list first, the issuing thread has
        //   'higher priority' than the other threads of the same activity (and the matching
        //   'EndConstraint' is executed earlier).
        //
        // Feasible NonCritical constraints can have their CPU reservation partially stolen by
        // a Critical Constraint in the same activity.
        // When this happens, the estimate of the victim reservation is diminished accordingly,
        // and if the estimate gets to zero it is transfered to the 'UnfinishedConstraints' list
        //
        // When a reservation becomes active, it is moved from the 'pIdleReservation' to the
        // 'GuaranteedReservations'
        //
        //  Reservations WITH NO SurroundingReservations(i.e., NOT nested)
        //  --------------------------------------------------------------
        // If 'EndConstraint' is executed while the reservation is active, the status of the
        // reservation changes to an activity reservation, i.e. the remaining CPU is allocated
        // to the activity and not only to the issuing thread (see 'GetRunnableThread').
        // The reservation is not removed from the 'p(Non)GuaranteedReservations' lists until its
        // estimate is exhausted.
        //
        // An active reservation is removed from the 'p(Non)GuaranteedReservations' lists
        // when (see 'Reschedule' and 'EnqueueReservation'):
        // - CurrentTime >= Deadline or
        // - Estimate <= 0; the estimate is decremented every time the active thread of the
        //   reservation (see above and 'GetRunnableThread') is executed. If the matching
        //   'EndConstraint' has not been executed by the issuing thread, the reservation is
        //   is placed 'UnfinishedConstraints' list of the issuing thread's activity.
        //
        //  Reservations WITH SurroundingReservations(i.e., nested)
        //  ---------------------------------------------------------
        // such a reservation may inherit time for any of the reservations on its nested stack.
        // When an inherit happens, the estimate of the 'giving' reservation is diminished
        // accordingly.
        // When a nested reservation is granted (i.e., estimate > 0), it will be positioned on the
        // idle or Guaranteed lists AND, all other surrounding reservations will be dequeued (from
        // which ever Guaranteed or Unfinished list they are.
        // The reservation is not removed from the 'GuaranteedReservations' lists until its
        // estimate is exhausted or at EndConstraint.
        //
        // If 'EndConstraint' is executed
        //   while the reservation is active (i.e., estimate > 0),
        //      the estimate is given to the immediately surrounding reservation, and this is
        //      placed on Guaranteed list.
        //  while the reservation has no estimate (on the Unfinished list)
        //      it will be simply removed from the list
        //
        // When a reservation gets to estimate 0 before EndConstraint
        // (see 'Reschedule', StealNonCriticalCpu)
        //   it is placed on the Unfinished list. The same happen with all the surrounding
        //   reservations up to the first with estimate non zero which is placed on the
        //   Guaranteed list.
        //

#region OneShotCpuReservation related functions

        // OneShotCpuReservation related functions begin:

        public static int ReleaseReservationProtected(OneShotReservation reservation)
        {
            int  newrefcnt;
            Debug.Assert(Processor.InterruptsDisabled());

            Debug.Assert(reservation.ReferenceCount > 0);
            newrefcnt = Interlocked.Decrement(ref reservation.ReferenceCount);

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
            int newrefcnt;
            // Debug.Assert(!Processor.InterruptsDisabled());
            Debug.Assert(reservation.ReferenceCount > 0);
            newrefcnt = reservation.ReferenceCount - 1;
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

        //=========================================================

        public static void AddRefReservation(OneShotReservation reservation)
        {
            Debug.Assert(reservation.ReferenceCount >= 0);
            reservation.ReferenceCount++;
        }

#if DEBUG_RESERV
        static void PrintQueueReserv(OneShotReservation reservation, DateTime timeNow)
        {
            OneShotReservation pSave = reservation;

            DebugStub.Print("ReservQueue {0} {1:d}\n",
                            __arglist((pSave == null? "null":""), timeNow));
            while (reservation != null) {
                DebugStub.Print("R{0} T{1}:A{2} -  Deadline {3:d} Estimate {4:d} Lax {5:d}",
                                __arglist(
                                    reservation.ReservationId,
                                    (reservation.OriginalThread != null
                                     ? reservation.OriginalThread.Id : -1),
                                    (reservation.OriginalThread != null
                                     ? reservation.OriginalThread.pActivity.Id
                                     : reservation.pActivity.Id),
                                    reservation.Deadline,
                                    reservation.Estimate,
                                    reservation.Deadline - timeNow));

                if ((reservation = reservation.Next) == pSave) {
                    break;
                }
            }
        }
#endif
#endregion

#region Potpourri (Unregioned functions)

        static GraphNode NextNode(GraphNode currentNode)
        {
            if (currentNode.Next != null) {
                currentNode = currentNode.Next;
            }
            else {
                currentNode = SchedulerPlan;
            }

            while ((currentNode.Type == GraphNode.NodeType.RightBranch) ||
                   (currentNode.Type == GraphNode.NodeType.LeftBranch)) {

                if (currentNode.Type == GraphNode.NodeType.RightBranch) {
                    currentNode.Type = GraphNode.NodeType.LeftBranch;    // Next time take the other branch
                    currentNode = currentNode.Right;
                }
                else if (currentNode.Type == GraphNode.NodeType.LeftBranch) {
                    currentNode.Type = GraphNode.NodeType.RightBranch;// Next time take the other branch
                    currentNode = currentNode.Left;
                }
            }
            Debug.Assert(currentNode != null);
            Debug.Assert(currentNode.TimeToOrigin.Ticks >= 0);
            Debug.Assert(currentNode.Period.Ticks % minPeriod.Ticks == 0);

            return currentNode;
        }


        // returns true if there is at least one free slot left in the array
        static bool CleanReservationArray(GraphNode node, DateTime timeNow)
        {
            int i, j;
            OneShotReservation reservation;

            for (i = j = 0; i < node.ReservCount; i++) {
                reservation = node.ReservationArray[i].AssociatedReservation;
                if (node.ReservationArray[i].End <= timeNow + RialtoScheduler.AFewSlice || !reservation.Valid ||
                    (reservation.Estimate <= RialtoScheduler.AFewSlice && reservation.OriginalThread == null)) {
                    //  DebugStub.Print("CleanReservationArray {0} {1}\n",
                    // __arglist(reservation, reservation.ReferenceCount));
                    ReleaseReservationProtected(reservation); // each slice has own reference
                }
                else {
                    if (j < i) {
                        node.ReservationArray[j] = (ReservationSlice)node.ReservationArray[i].Clone();
                    }
                    //memcpy(&(node.ReservationArray[j]), &(node.ReservationArray[i]), sizeof(ReservationSlice));
                    j++;
                }
            }
            node.ReservCount = j;
            Debug.Assert(node.ReservCount <= GraphNode.MaxNumReservations);
            return (j < GraphNode.MaxNumReservations);
        }

        public static RialtoThread GetCurrentThread()
        {
            return currentThreadGlobal;
        }

        public static void IChangeCurrentThread(RialtoThread thread)
        {
            currentThreadGlobal = thread;
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
            return (Time0 < Time1 ? Time1: Time0);
        }

        // The Queue of Standby Activities is circular and
        // standbyActivities points to the Next activity
        // to get spare CPU. By default, the Queue is FIFO.
        public static void EnqueueStandbyActivity(RialtoActivity activity)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            activity.LastNode = null;

            if (activity.NextStandbyActivity != null) {
                Debug.Assert(activity.PreviousStandbyActivity != null);
                return;
            }

            if (standbyActivities == null) {
                activity.PreviousStandbyActivity =
                    activity.NextStandbyActivity = activity;
                standbyActivities = activity;
                return;
            }
            // else ...
            // insert in front of 'standbyActivities'
            activity.NextStandbyActivity = standbyActivities;
            activity.PreviousStandbyActivity = standbyActivities.PreviousStandbyActivity;
            standbyActivities.PreviousStandbyActivity.NextStandbyActivity = activity;
            standbyActivities.PreviousStandbyActivity = activity;
#if LIFO
            standbyActivities = activity;
#endif
        }

        static void DequeueStandbyActivity(RialtoActivity activity)
        {
            Debug.Assert(Processor.InterruptsDisabled());

            if (activity.NextStandbyActivity == null) {
                // if not in Queue, a NOP
                Debug.Assert(activity.PreviousStandbyActivity == null);
                return;
            }

            if (activity.NextStandbyActivity == activity) {
                // last one in the Queue
                Debug.Assert(activity.PreviousStandbyActivity == activity);
                standbyActivities = null;
            }
            else {
                activity.PreviousStandbyActivity.NextStandbyActivity =
                    activity.NextStandbyActivity;
                activity.NextStandbyActivity.PreviousStandbyActivity =
                    activity.PreviousStandbyActivity;
                if (standbyActivities == activity) {
                    // advance the Queue head
                    standbyActivities = activity.NextStandbyActivity;
                }
            }
            activity.NextStandbyActivity = activity.PreviousStandbyActivity = null;
        }

        // Add Activity in the last Round-Robin position
        public static void EnqueueActivity(RialtoActivity activity)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            if (circularActivityList == null) {
                robinActivityNext = circularActivityList = robinActivityCurrent =
                    activity.Next = activity.Previous = activity;
            }
            else {
                activity.Next = robinActivityNext;
                activity.Previous = robinActivityNext.Previous;
                robinActivityNext.Previous.Next = activity;
                robinActivityNext.Previous = activity;
            }
        }

        public static void DequeueActivity(RialtoActivity activity) //TODO: Make an activity function?
        {
            // In our simulated environment, Activity 0 is never removed from the Q
            Debug.Assert(Processor.InterruptsDisabled());
            //NOTE: Might be replaced with a test and return.
            Debug.Assert(activity.Next != null);
            Debug.Assert(activity.Previous != null);

            // Previous. in rialto  if (activity.Id == 0) return;

            DequeueStandbyActivity(activity);

            activity.Next.Previous = activity.Previous;
            activity.Previous.Next = activity.Next;
            if (circularActivityList == activity) {
                circularActivityList = activity.Next;
            }
            if (robinActivityNext == activity) {
                robinActivityNext = activity.Next;
            }
        }

        // if possible, places thread in the 2nd position in the RunnableThreads Q
        // Make this a function on a resource container.
        public static void EnqueueRunThread(RialtoThread thread)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            RialtoActivity activity = thread.AssociatedActivity;
            if (thread.Next != null) {
                Debug.Assert(thread.Previous != null);
                return;
            }

            //  Debug.Assert(thread.QueueType == QUEUE_NONE);

            //if (thread.State == RialtoThread.ThreadState.ThreadWaiting) {
            //    thread.SetState(RialtoThread.ThreadState.ThreadReady);
            //}

            if (activity.RunnableThreads == null) {
                thread.Next = thread.Previous = thread;
                activity.RunnableThreads = thread;
            }
            else {
                thread.Previous = activity.RunnableThreads;
                thread.Next = activity.RunnableThreads.Next;
                activity.RunnableThreads.Next.Previous = thread;
                activity.RunnableThreads.Next = thread;
            }
        }

        public static bool DequeueRunThread(RialtoThread thread)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            if (thread.Next == null) {
                Debug.Assert(thread.Previous == null);
                return false;
            }

            if (thread.Next == thread) {
                Debug.Assert(thread.Previous == thread);
                thread.AssociatedActivity.RunnableThreads = null;
                DequeueStandbyActivity(thread.AssociatedActivity);
            }
            else {
                // multiple threads in the RunnableThreads Q
                thread.Previous.Next = thread.Next;
                thread.Next.Previous = thread.Previous;
                if (thread.AssociatedActivity.RunnableThreads == thread) {
                    thread.AssociatedActivity.RunnableThreads = thread.Next;
                }
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
            // Debug.Assert(CurrentThread()->GetState() == Thread.ThreadState.ThreadRunning);

            // Debug.Assert(NewTimerTimeout > SchedulingTime);

            //TODO: Need LA_Time?
            //            if (NewTimerTimeout > SleepTimeout - LA_Time) {
            //                NewTimerTimeout = SleepTimeout - LA_Time;
            //            }
            if (NewTimerTimeout > RialtoThread.GetSleepTimeout()) {
                NewTimerTimeout = RialtoThread.GetSleepTimeout();
            }

            //DebugStub.Print("Setting Next Interrupt for: {0} ...\n",
            // __arglist(NewTimerTimeout.Ticks));
            bool success = SchedulerClock.SetNextInterrupt(NewTimerTimeout); //TODO: Perhaps only call this if the time changed.

            //DebugStub.Print(success?"SUCCESS\n":"FAILED\n");

            return success;
        }

        public static void MoveToStandby(RialtoThread thread, bool fromWakeUp)
        {
            if (thread.AssociatedActivity.LastNode == null) {
                if (fromWakeUp) {
                    // a thread that wakes up always gets a (small) chance to run:
                    // but ...IReadyThread would not do this enqueue
                    //thread.AssociatedActivity.MyRecurringCpuReservation.SliceLeft = RialtoScheduler.MinSlice;
                    EnqueueStandbyActivity(thread.AssociatedActivity);
                }
            }
            else {
                // sets 'pthd.pActivity.LastNode' to null.
                EnqueueStandbyActivity(thread.AssociatedActivity);
                if (CurrentPlanNode == thread.AssociatedActivity.LastNode) {
                    //TODO: since EnqueueStandbyActivity sets it to null, isn't this a no-op?
                    Reschedule();
                }
            }
        }


        static void UpdateStandbyStatus(RialtoThread thread)
        {
            RialtoActivity  activity = thread.AssociatedActivity;

            if ((activity.RunnableThreads == null) &&
                (CurrentPlanNode.Type == GraphNode.NodeType.Used) &&
                (CurrentPlanNode.DefaultActivity == activity) &&
                ((activity.MyRecurringCpuReservation.SliceLeft = CurrentPlanNode.NextExec + CurrentPlanNode.Slice -
                  SystemClock.GetKernelTime()) >= RialtoScheduler.MinSlice)) {
                activity.LastNode = CurrentPlanNode; // record it.
            }
        }


        public static void UpdateSchedulingStatus()
        {
            TimeSpan timeRun;
            RialtoThread currentThread = GetCurrentThread();

            DateTime newSchedulingTime = SystemClock.GetKernelTime();

            timeRun = newSchedulingTime - SchedulingTime;
            SchedulingTime = newSchedulingTime;

#if false
            Tracing.Log(Tracing.Debug, "UpdateSchedulingStatus");
#endif

            if (idle) {
                // UpdateIdleTime
                idleTime += timeRun;
                return;
            }

            // if IDLE compute CurrentPlanNode & set currentThread to null
            if (currentThread != null) {
                // unless thread just exited
                // Update Thread & Activity execution times
                currentThread.AddExecutionTime(timeRun);
                // In the current implementation, the scheduler has to call CurrentTask on the
                //       known running thread, since the thread may not actually be running when
                //       UpdateSchedulingStatus() is called.  It may be the scheduler thread running.
                //       I left things the way they are though since Scheduler is suitable to
                //       use for any application/non-kernel code.  We may need to document it there
                //       too, or perhaps have it ask the CpuResource what the default task is at the
                //       moment, since presumably it may be the CpuResource which actually tracks that.
                currentThread.EnclosingThread.CurrentTask().AddResourceAmountUsed(CpuResource.Provider().ResourceString, CpuResource.Provider().TimeToCpu(timeRun));
                //Scheduler.CurrentTask().AddResourceAmountUsed(CpuResource.Provider().ResourceString, CpuResource.Provider().TimeToCpu(timeRun));

                //                if (currentThread.AssociatedActivity != null) {
                //                    currentThread.AssociatedActivity.MyRecurringCpuReservation.EnclosingCpuReservation.AddTimeUsed(timeRun);
                //                }
            }
            if (OneShotReservation.CurrentReservation != null) {
                // slice used for a reservation.
                OneShotReservation.CurrentReservation.Estimate -= timeRun;
            }
            else if (currentStandbyActivity != null) {
                if (currentStandbyActivity.MyRecurringCpuReservation != null) {
                    currentStandbyActivity.MyRecurringCpuReservation.SliceLeft -= timeRun;
                }
                currentStandbyActivity = null;
            }
            else if (robinActivityCurrent != null) {
                // slice used for RoundRobin.
                robinSliceLeft -= timeRun;
                robinActivityCurrent = null;
            }
        }


        // Always called when another thread needs to be scheduled.
        static bool RescheduleInterrupt()
        {
            Debug.Assert(Processor.InterruptsDisabled());
            Debug.Assert(!Processor.InterruptsDisabled());
            RialtoThread currentThread = GetCurrentThread();

            DateTime nextStart;
            RialtoThread previousThread;

          RescheduleAgain: // !!!!!!!!!! SIM ONLY !!!!!!!!!
            if (idle) {
                currentThread = null;
            }

            previousThread = currentThread;

            EarliestDeadlineFirstBlocked = false;

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

#if LOG_SCHEDULER_DETAILS
            bool logDirectSwitch = (directSwitchTo != null);
            long logCurrentNodeSliceLeft1 = 0;
            long logCurrentNodeSliceLeft2 = 0;
            long logCurrentNodeSliceLeft3 = 0;
            int logCurrentPlanNodeId = -1;
            int logReservCount = 0;
            bool logRunningDefault = false;
            bool logRunningStandby = false;
            bool logRunningRobin = false;
            long logCurrentNodeSliceLeft4 = 0;
            long logCurrentNodeSliceLeft = 0;
#endif

            //Why not check this directly?
            if (directSwitchTo != null) {
                //Debug.Print("directSwitchTo!\n");
                Debug.Assert(OneShotReservation.CurrentReservation != null);
                Scheduler.LogContextSwitch();  // Context Switch statistics
                //  Debug.Assert(currentThread.QueueType == QUEUE_NONE);
                currentThread = directSwitchTo;
                directSwitchTo = null;
                Scheduler.LogReschedule();
                // DebugStub.Print("Directed Context Switch.\n");
                goto exitOK; // don't set TIMER interrupt
            }

            OneShotReservation.ClearCurrentReservation();
            currentThread = null;

            // Finished first stage, i.e. updated state.
            // Start second stage: wakeup threads & select Next CPU slice.

            RialtoThread.WakeThreads();

            // NOTE: In the original Rialto Simulator Code (& MMOSA code)
            // The call to DrainDeferredConditions() was made here.
            // In Singularity, this will basically be replaced with a
            // queue of wait-events to fix.

            OneShotReservation.FreshenReservationQueues();

#if NOT_DEFINED
            if (SchedulingTime >= changePlanTime) {
                GraphNode ptemp = SchedulerPlan;
                SchedulerPlan = pNextPlan;
                pNextPlan = null;
                changePlanTime = TIME_FOREVER;
                Debug.Assert(false);
                FreeSchedulerPlan(ptemp);
                CurrentPlanNode = SchedulerPlan;
            }
#endif
            TimeSpan currentNodeSliceLeft;
            // Compute currentNodeSliceLeft (possible (very) negative).

            Debug.Assert(CurrentPlanNode != null);
            //DebugStub.Print("Next Exec: {0} Slide: {1} SchedulingTime: {2}\n",
            //__arglist(CurrentPlanNode.NextExec.Ticks,
            // CurrentPlanNode.Slice.Ticks,
            // SchedulingTime.Ticks));
            currentNodeSliceLeft = CurrentPlanNode.NextExec + CurrentPlanNode.Slice - SchedulingTime;

            Tracing.Log(Tracing.Debug, "Reservation for {0} ticks",
                        (UIntPtr)unchecked((uint)currentNodeSliceLeft.Ticks));

#if LOG_SCHEDULER_DETAILS
            logCurrentNodeSliceLeft1 = currentNodeSliceLeft.Ticks;
#endif

            while (currentNodeSliceLeft.Ticks < 0) {
                // choose Next node
                GraphNode tempNode = CurrentPlanNode;
                DateTime tempTime = CurrentPlanNode.NextExec;
                CurrentPlanNode.NextExec += CurrentPlanNode.Period; // time of Next execution
                //  If incomplete, the default activity will be added
                //  to the 'pStandbyActiv' List when ready
                CurrentPlanNode = NextNode(CurrentPlanNode);
                Debug.Assert(CurrentPlanNode.NextExec <= tempNode.NextExec);
                Debug.Assert(CurrentPlanNode.NextExec > tempTime);
                Debug.Assert(CurrentPlanNode.NextExec <= SchedulerPlan.NextExec); //In fact, should be min of all nodes.
                currentNodeSliceLeft = CurrentPlanNode.NextExec + CurrentPlanNode.Slice - SchedulingTime;
            }

#if LOG_SCHEDULER_DETAILS
            logCurrentNodeSliceLeft2 = currentNodeSliceLeft.Ticks;
#endif

            if (currentNodeSliceLeft < RialtoScheduler.MinSlice) {
                // choose Next node
                CurrentPlanNode.NextExec += CurrentPlanNode.Period; // time of Next execution
                //  If incomplete, the default activity will be added
                //  to the 'pStandbyActiv' List when ready
                CurrentPlanNode = NextNode(CurrentPlanNode);
                Debug.Assert(CurrentPlanNode.NextExec <= SchedulerPlan.NextExec);
                currentNodeSliceLeft = CurrentPlanNode.NextExec + CurrentPlanNode.Slice - SchedulingTime; // don't waste the leftover
            }

#if LOG_SCHEDULER_DETAILS
            logCurrentNodeSliceLeft3 = currentNodeSliceLeft.Ticks;
            logCurrentPlanNodeId = (CurrentPlanNode.DefaultActivity != null) ? CurrentPlanNode.DefaultActivity.Id : -1;
#endif

            // Do adjustment before computation of CurrentSlice is started
            //  currentNodeSliceLeft -= RESCHEDULE_OVHD; // !!!!!!!!!!! SIM only !!!!!!!!!!!

            //CurrentSlice = currentNodeSliceLeft;
            Debug.Assert(currentNodeSliceLeft /* CurrentSlice */ >= RialtoScheduler.MinSlice);

            if (CurrentPlanNode.ReservCount > 0) {
                if (!CurrentPlanNode.ReservationArray[0].AssociatedReservation.Valid || // need to do some cleaning
                    (CurrentPlanNode.ReservationArray[0].End <= SchedulingTime + RialtoScheduler.AFewSlice) ||
                    (CurrentPlanNode.ReservationArray[0].AssociatedReservation.Estimate <= RialtoScheduler.AFewSlice && CurrentPlanNode.ReservationArray[0].AssociatedReservation.OriginalThread == null)) {
                    CleanReservationArray(CurrentPlanNode, SchedulingTime);
                }

                if (CurrentPlanNode.ReservCount > 0) {
                    // are there any active reservation left?
#if LOG_SCHEDULER_DETAILS
                    logReservCount = CurrentPlanNode.ReservCount;
#endif

                    nextStart = CurrentPlanNode.ReservationArray[0].Start;

                    if (nextStart > SchedulingTime + RialtoScheduler.AFewSlice) {
                        if (nextStart < SchedulingTime + currentNodeSliceLeft) {
                            // GATECH
                            currentNodeSliceLeft /* CurrentSlice */ = nextStart - SchedulingTime; // GATECH
                            Debug.Assert(currentNodeSliceLeft /* CurrentSlice */.Ticks > 0);
                        }
                    }
                    else {
                        // GraphNode OneShotReservation in effect; find a runnable reservation:
                        OneShotReservation.FindRunnableReservation(ref currentThread);
                        if (currentThread != null) {
                            // a runnable reservation was found:
                            // TO DO: optimize the comparisons
                            currentNodeSliceLeft /* CurrentSlice */ = minInterval(currentNodeSliceLeft /* CurrentSlice */,
                                CurrentPlanNode.ReservationArray[0].End - SchedulingTime);
                            currentNodeSliceLeft /* CurrentSlice */ = minInterval(currentNodeSliceLeft /* CurrentSlice */, OneShotReservation.CurrentReservation.Estimate);
                            currentNodeSliceLeft /* CurrentSlice */ = minInterval(currentNodeSliceLeft /* CurrentSlice */,
                                OneShotReservation.CurrentReservation.Deadline - SchedulingTime);
                            // DebugStub.Print("OneShotCpuReservation in effect.\n");
                            goto AdjustSlice;
                        }
                        else {
                            EarliestDeadlineFirstBlocked = true;
                            OneShotReservation.ClearCurrentReservation();
                        }
                    } // else
                } // 2nd if (CurrentPlanNode.ReservCount > 0)
            } // 1st if (CurrentPlanNode.ReservCount > 0)

            // No GraphNode OneShotReservation in effect; run default activity, if any:
            if (CurrentPlanNode.Type == GraphNode.NodeType.Used) {
                DequeueStandbyActivity(CurrentPlanNode.DefaultActivity); // nop if not on list
                if ((currentThread = CurrentPlanNode.DefaultActivity.GetRunnableThread()) != null) {
                    // DebugStub.Print("Running Default Resource Container.\n");
                    Tracing.Log(Tracing.Debug, "Default resource container.");
#if LOG_SCHEDULER_DETAILS
                    logRunningDefault = true;
#endif
                    goto AdjustSlice;
                }
                else if (CurrentPlanNode.DefaultActivity.LastNode == CurrentPlanNode) {
                    CurrentPlanNode.DefaultActivity.LastNode = null;
                }
            }

            // Current GraphNode is Free or the default activity is not runnable;
            // Try to use slice for the queue of Standby Activities */
            while (standbyActivities != null) {
                // Dequeue some of the activities that have too little left to do!
                if (standbyActivities.MyRecurringCpuReservation != null && standbyActivities.MyRecurringCpuReservation.SliceLeft < RialtoScheduler.MinSlice) {
                    DequeueStandbyActivity(standbyActivities);
                    continue; // if it was the last incomplete activity, exit while
                }

                currentThread = standbyActivities.GetRunnableThread();
                currentNodeSliceLeft /* CurrentSlice */ = minInterval(currentNodeSliceLeft /* CurrentSlice */, (standbyActivities.MyRecurringCpuReservation == null? RialtoScheduler.MinSlice: standbyActivities.MyRecurringCpuReservation.SliceLeft));
                currentStandbyActivity = standbyActivities;
                if (currentStandbyActivity.MyRecurringCpuReservation == null) {
                    DequeueStandbyActivity(standbyActivities);
                }
                Debug.Assert(currentThread != null);
                //DebugStub.Print("Running a standby resource container\n");
#if LOG_SCHEDULER_DETAILS
                logRunningStandby = true;
#endif
                goto AdjustSlice;
            }

            // Same about Current GraphNode but no Standby Activity to run.
            // Use slice for the RoundRobin queue (always nonempty).
            if (robinSliceLeft < RialtoScheduler.MinSlice) {
                robinActivityNext  = robinActivityNext.Next;    // don't run it again
                robinSliceLeft = RialtoScheduler.RobinSlice;
            }
            // Find Next Runnable Activity.
            robinActivityCurrent = robinActivityNext; // !!!!!!!!!!!!!SIM ONLY !!!!!!!!!!
            while ((currentThread = robinActivityNext.GetRunnableThread()) == null) {
                robinActivityNext = robinActivityNext.Next;
                robinSliceLeft = RialtoScheduler.RobinSlice;
                if (robinActivityNext == robinActivityCurrent) {
                    // !!!!!!!!SIM ONLY !!!!!!!
                    // in the real scheduler, execute halt
                    if (OneShotReservation.IdleReservations != null) {
                        // reuse nextStart
                        nextStart = minTime(RialtoThread.GetSleepTimeout(), OneShotReservation.IdleReservations.Start);
                    }
                    else {
                        nextStart = RialtoThread.GetSleepTimeout();
                        //DebugStub.Print("idle, sleeping until {0} cf maxvalue {1}\n",
                        // __arglist(nextStart.Ticks, DateTime.MaxValue.Ticks));
                    }

                    if (nextStart == DateTime.MaxValue) {
                        Scheduler.StopSystem();
                    }
                    Tracing.Log(Tracing.Debug, "");

                    if (! ResetTimerTimeout(nextStart)) {
                        //Error setting timer.  Try scheduling again.
                        DebugStub.Print("Thought idle, failed to set interrupt.\n");
                        goto RescheduleAgain;
                    }
                    idle = true;

                    // !!!!!!!!SIM ONLY !!!!!!!
                    currentThread = null;           // !!!!!!!!SIM ONLY !!!!!!!
                    OneShotReservation.ClearCurrentReservation();
                    robinActivityCurrent  = null;
                    currentStandbyActivity = null;

                    if (DateTime.MaxValue != nextStart) {
                        Scheduler.LogTimeJump();
                    }

                    //DebugStub.Print("Halted.\n");
                    return true;            // !!!!!!!!SIM ONLY !!!!!!!
                }
                // !!!!!!!!SIM ONLY !!!!!!!
            }
            //DebugStub.Print("Running Round Robin Resource Container\n");
#if LOG_SCHEDULER_DETAILS
            logRunningRobin = true;
#endif
            robinActivityCurrent  = robinActivityNext; // we probably need only one of the two variables

            currentNodeSliceLeft /* CurrentSlice */ = minInterval(currentNodeSliceLeft /* CurrentSlice */, robinSliceLeft);

#if LOG_SCHEDULER_DETAILS
            logCurrentNodeSliceLeft4 = currentNodeSliceLeft.Ticks;
#endif

        AdjustSlice: // always set timer before Next wakeup time!

            Debug.Assert(currentThread != null);
            if (currentThread != previousThread) {
                Scheduler.LogContextSwitch(); // Context Switch statistics
            }

            if (OneShotReservation.IdleReservations != null) {
                // reuse nextStart
                nextStart = minTime(RialtoThread.GetSleepTimeout(), OneShotReservation.IdleReservations.Start);
            }
            else {
                nextStart = RialtoThread.GetSleepTimeout();
            }

            if (SchedulingTime + currentNodeSliceLeft /* CurrentSlice */ > nextStart) {
                currentNodeSliceLeft /* CurrentSlice */ = nextStart - SchedulingTime;
            }

#if LOG_SCHEDULER_DETAILS
            logCurrentNodeSliceLeft = currentNodeSliceLeft.Ticks;

#if VERBOSEX
            unchecked
            {
                Tracing.LogSchedulerDetails(
                    logReservCount, logDirectSwitch, logRunningDefault, logRunningStandby, logRunningRobin,
                    (int) logCurrentNodeSliceLeft1, (int) logCurrentNodeSliceLeft2, (int) logCurrentNodeSliceLeft3,
                    (int) logCurrentNodeSliceLeft4, (int) logCurrentNodeSliceLeft, logCurrentPlanNodeId);
            }
#endif
#endif

            Scheduler.LogReschedule();
            if (!ResetTimerTimeout(SchedulingTime + currentNodeSliceLeft) || Scheduler.TimerInterruptedFlag) {
                //TODO: What do we REALLY want here?
                currentThread = null;           // !!!!!!!!SIM ONLY !!!!!!!
                OneShotReservation.ClearCurrentReservation();
                robinActivityCurrent  = null;
                currentStandbyActivity = null;
                goto RescheduleAgain;
            }

          exitOK:

            if (currentThread != previousThread) {
                IChangeCurrentThread(currentThread);
            }
            idle = false;

            // Not necessarily true:  Debug.Assert(!Scheduler.TimerInterruptedFlag);

            return false;
        }

#endregion

#region Constraint feasibility analysis support functions
        // Constraint Feasibility Analysis support functions begin:
        // ----------------------------------------------------------------------------------
        static TimeSpan ComputeAvailableCpu(DateTime begin, DateTime end, GraphNode node)
        {
            if (begin >= end) {
                return new TimeSpan(0);
            }
            else {
                TimeSpan reserved = new TimeSpan(0);
                DateTime nextExecution;
                int k;

                if (node == CurrentPlanNode) {
                    nextExecution = node.NextExec + node.Period;
                    if (begin < node.NextExec + node.Slice) { // TempStart >= CurrentTime
                        reserved += node.NextExec + node.Slice - begin;
                    }
                }
                else {
                    nextExecution = node.NextExec;
                }

                if (begin > nextExecution) {
                    k = (int) ((begin - nextExecution).Ticks/node.Period.Ticks);
                    reserved -= new TimeSpan(k * node.Slice.Ticks) + minInterval(node.Slice,
                        begin - nextExecution - new TimeSpan(k * node.Period.Ticks));
                }

                if (end > nextExecution) {
                    k = (int) ((end - nextExecution).Ticks/node.Period.Ticks);
                    reserved += new TimeSpan(k * node.Slice.Ticks) + minInterval(node.Slice,
                        end - nextExecution - new TimeSpan(k * node.Period.Ticks));
                }

                return reserved;
            }
        }

        // GraphNode can provide more than time than necessary between start and deadline; come shorter deadline:
        static TimeSpan AvailableCpuAndDeadline(DateTime begin,
                                                DateTime end,
                                                out DateTime shortDeadline,
                                                GraphNode node,
                                                TimeSpan requested)
        {
            TimeSpan reserved = new TimeSpan(0);
            DateTime nextExecution;
            DateTime temp;
            TimeSpan tempSpan;
            int  k;

            shortDeadline = end;
            if (node == CurrentPlanNode) {
                //If the node specified is the current one running?
                if (node.NextExec + node.Slice - begin >= requested) {
                    //If there's enough time between begin and the end of current node's execution
                    shortDeadline = begin + requested; //case 1
                    return requested;
                }
                else if (node.NextExec.Ticks + node.Slice.Ticks - begin.Ticks >= 0) {
                    //case 2
                    reserved += node.NextExec + node.Slice - begin; // reserved < requested
                }
                nextExecution = node.NextExec + node.Period;
            }
            else {
                nextExecution = node.NextExec;
            }

            if (begin > nextExecution) {
                //case 3: note -- precludes case 2
                k = (int) ((begin - nextExecution).Ticks/node.Period.Ticks); // k -> number of complete periods before begin.
                reserved -= new TimeSpan(k * node.Slice.Ticks) + minInterval(node.Slice,
                    begin - nextExecution - new TimeSpan(k * node.Period.Ticks)); // calculates the total time remaining before begin we could have reserved -- subtracts it from reserved
            }

            k = (int) ((requested - reserved).Ticks/node.Slice.Ticks); //case 2 above -- number of remaining slices needed
            //case 3 above -- number of complete slices in the calculation + number of slices for the request
            tempSpan = requested - new TimeSpan(k * node.Slice.Ticks) - reserved;   //remainder ms from (requested-reserved)/slice
            if (tempSpan.Ticks > 0) {
                temp = nextExecution + new TimeSpan(k * node.Period.Ticks) + tempSpan;
            }
            else {
                temp = nextExecution + new TimeSpan(((k>0)?k-1:0) * node.Period.Ticks) + node.Slice;
            }

            if (temp <= end) {
                shortDeadline = temp;
                return requested;
            }

            if (end > nextExecution) {
                k = (int) ((end - nextExecution).Ticks/node.Period.Ticks);
                reserved += new TimeSpan(k * node.Slice.Ticks) + minInterval(node.Slice,
                    end - nextExecution - new TimeSpan(k * node.Period.Ticks));
            }
            return reserved;
        }

        // add a new pointer to a reservation to a node and clean the ordered array
        static void AddReservationToReservationArray(GraphNode node,
                                                     OneShotReservation reservation,
                                                     DateTime start,
                                                     DateTime end,
                                                     TimeSpan available,
                                                     DateTime timeNow)
        {
            int i, j, insertPosition = -1;
            int release = 0;
            OneShotReservation tempReservation;

            Debug.Assert(node.ReservCount < GraphNode.MaxNumReservations);
            Debug.Assert((start < end) && (available.Ticks > 0));
            Debug.Assert(node.ReservCount <= GraphNode.MaxNumReservations);

            for (i = j = 0; i < node.ReservCount; i++) {
                tempReservation = node.ReservationArray[i].AssociatedReservation;
                if (node.ReservationArray[i].End <= timeNow || !tempReservation.Valid) {
                    ReleaseReservationProtected(tempReservation);
                    release++;
                }
                else {
                    if ((insertPosition < 0) && (start < node.ReservationArray[i].Start)) {
                        insertPosition = j;
                    }
                    if (j < i) {
                        node.ReservationArray[j] = (ReservationSlice)node.ReservationArray[i].Clone();
                    }
                    //memcpy(&(node.ReservationArray[j]), &(node.ReservationArray[i]), sizeof(ReservationSlice));
                    j++;
                }
            }
            if (insertPosition < 0) {
                insertPosition = j;
            }
            else {
                Array.Copy(node.ReservationArray, insertPosition, node.ReservationArray, insertPosition+1, (j-insertPosition));
            }
            //memmove(&(node.ReservationArray[insertPosition+1]), &(node.ReservationArray[insertPosition]),
            //    sizeof(ReservationSlice) * (j - insertPosition));

            node.ReservationArray[insertPosition] = new ReservationSlice();
            node.ReservationArray[insertPosition].Start = start;
            node.ReservationArray[insertPosition].End    = end;
            node.ReservationArray[insertPosition].Available = available;
            node.ReservationArray[insertPosition].AssociatedReservation = reservation;
            AddRefReservation(reservation);
            Debug.Assert(node.ReservCount == j + release);
            node.ReservCount = j + 1;
            Debug.Assert(node.ReservCount <= GraphNode.MaxNumReservations);
        }

        public static void ReserveSlices(DateTime start,
                                         DateTime deadline,
                                         TimeSpan requestedTime,
                                         RialtoThread task,
                                         out TimeSpan leftUnreserved,
                                         ref TimeSpan timeToInherit,
                                         OneShotReservation reservation,
                                         GraphNode nodeList,
                                         DateTime timeNow)
        {
            GraphNode        node;
            OneShotReservation localReservation;
            int         i;
            TimeSpan    stillNeeded, toBeInherited, availableCpu, tempSpan;
            DateTime    tempStart, tempEnd, tempMiddle, newEnd, temp;

            Debug.Assert(Processor.InterruptsDisabled());

            stillNeeded = requestedTime;
            if (timeToInherit.Ticks >= 0) {
                toBeInherited = timeToInherit;
            }
            else {
                toBeInherited = new TimeSpan(0);
            }

            ArrayList newNodeReservations = new ArrayList();

            for (node = nodeList; node != null; node = node.SameActivityNext) {

                //NewReservCount = 0;
                newNodeReservations.Clear();

                // check if there is room to place another node reservation for this node
                if ((node.ReservCount == GraphNode.MaxNumReservations) && !CleanReservationArray(node, timeNow)) {
                    continue; // failed to find room for another reservation
                }

                tempStart = start; // compute all CPU time available from this node
                for (i = 0; (i < node.ReservCount) && (tempStart < deadline); i++) {
                    // the Begin fields are in increasing order:
                    localReservation = node.ReservationArray[i].AssociatedReservation;
                    if (!localReservation.Valid) {
                        continue;
                    }
                    tempEnd = node.ReservationArray[i].End;
                    if (tempEnd <= tempStart) {
                        continue; // this node reservation doesn't count
                    }

                    tempMiddle = node.ReservationArray[i].Start;

                    // tempEnd > tempStart
                    // tempStart..min(tempMiddle, deadline) can be allocated,
                    // max(tempStart, tempMiddle)..min(tempEnd, deadline) may be stolen or inherited

                    if ((tempMiddle > tempStart) &&
                        (node.ReservCount + newNodeReservations.Count < GraphNode.MaxNumReservations)) {
                        // allocate [tempStart , min(tempMiddle, deadline)] or a fraction of it:
                        temp = minTime(tempMiddle, deadline);
                        availableCpu = AvailableCpuAndDeadline(tempStart, temp, out newEnd, node, stillNeeded);
                        Debug.Assert((availableCpu <= stillNeeded) || (newEnd <= temp));

                        if (availableCpu >= stillNeeded) {
                            // success:
                            ReservationSlice tempSlice = new ReservationSlice();
                            tempSlice.Start     = tempStart;
                            tempSlice.End       = newEnd;
                            tempSlice.Available = availableCpu;
                            tempSlice.AssociatedReservation = reservation;
                            newNodeReservations.Add(tempSlice);
                            //NewNodeReserv[NewReservCount] = new ReservationSlice();
                            goto early_exit;
                        }
                        else if (availableCpu.Ticks > 0) {
                            ReservationSlice tempSlice = new ReservationSlice();
                            stillNeeded -= availableCpu;
                            tempSlice.Start     = tempStart;
                            tempSlice.End       = temp;
                            tempSlice.Available = availableCpu;
                            tempSlice.AssociatedReservation = reservation;
                            newNodeReservations.Add(tempSlice);
                            //NewNodeReserv[NewReservCount] = new ReservationSlice();
                        }
                    }

                    // max(tempStart, tempMiddle)..min(tempEnd, deadline) may be inherited:
                    if ((timeToInherit.Ticks != -1) && (localReservation.ReservTask == task)) {
                        // not yet stolen slice
                        // we can count on the time provided by this reservation
                        // as it is somewhere down the current stack of constraints
                        if (localReservation.ResolutionEpoch != ResolutionAttempt) {
                            localReservation.InheritedEstimate = new TimeSpan(0);
                            localReservation.ResolutionEpoch  = ResolutionAttempt;
                        }
                        tempSpan = localReservation.Estimate
                            - localReservation.InheritedEstimate;
                        if (OneShotReservation.CurrentReservation == localReservation) {
                            tempSpan -= timeNow - SchedulingTime;
                        }

                        if ((tempStart > tempMiddle) || (tempEnd > deadline)) {
                            // tempStart >= timeNow
                            availableCpu = minInterval(ComputeAvailableCpu(maxTime(tempStart, tempMiddle),
                                minTime(tempEnd, deadline), node), tempSpan);
                        }
                        else { // use $$-ed value
                            availableCpu = minInterval(node.ReservationArray[i].Available, tempSpan);
                        }

                        // inherit no more than needed:
                        availableCpu = minInterval(availableCpu, stillNeeded);
                        if (availableCpu.Ticks > 0) {
                            toBeInherited += availableCpu;
                            stillNeeded -= availableCpu;
                            localReservation.InheritedEstimate += availableCpu;
                            Debug.Assert(localReservation.InheritedEstimate
                                <= localReservation.Estimate);
                            if (stillNeeded.Ticks == 0) {
                                goto exit;
                            }
                        }
                    } // Inherit End.

                    tempStart = tempEnd;
                } // for (i = 0;......)

                for (i = 0; i < newNodeReservations.Count; i++) {
                    // add collected slice reservations:
                    AddReservationToReservationArray(node, ((ReservationSlice)newNodeReservations[i]).AssociatedReservation,
                        ((ReservationSlice)newNodeReservations[i]).Start,
                        ((ReservationSlice)newNodeReservations[i]).End,
                        ((ReservationSlice)newNodeReservations[i]).Available, timeNow);
                }

                if (node.ReservCount < GraphNode.MaxNumReservations) {
                    // if there is room for one more reservation
                    // Compute the last possible CPU slice(s) to be allocated at this node:
                    availableCpu = AvailableCpuAndDeadline(tempStart, deadline, out newEnd, node, stillNeeded);
                    if (availableCpu >= stillNeeded) {
                        AddReservationToReservationArray(node, reservation, tempStart, newEnd, availableCpu, timeNow);
                        stillNeeded = new TimeSpan(0);
                        goto exit;
                    }

                    if (availableCpu.Ticks > 0) {
                        stillNeeded -= availableCpu;
                        AddReservationToReservationArray(node, reservation, tempStart, deadline, availableCpu, timeNow);
                    }
                }
            } // for each node IN THE input list (free or from own activity)....
          exit:
            leftUnreserved = stillNeeded;
            if (timeToInherit.Ticks != -1) {
                timeToInherit  = toBeInherited;
            }
            return;

          early_exit:
            for (i = 0; i < newNodeReservations.Count; i++) {
                // add collected slice reservations:
                AddReservationToReservationArray(node,
                                                 ((ReservationSlice)newNodeReservations[i]).AssociatedReservation,
                                                 ((ReservationSlice)newNodeReservations[i]).Start,
                                                 ((ReservationSlice)newNodeReservations[i]).End,
                                                 ((ReservationSlice)newNodeReservations[i]).Available, timeNow);
            }
            goto exit;
        }

        public static bool CheckReservation(OneShotReservation reservation,
                                            DateTime timeNow)
        {
            TimeSpan                    timeLeft, timeInherited;
            GraphNode                   nodeList;
            TimeSpan                    timeToInherit;

            if (reservation.SurroundingReservation != null)  {
                if (!reservation.Valid) {
                    return CheckReservation(reservation.SurroundingReservation, timeNow);
                }
                if (!CheckReservation(reservation.SurroundingReservation, timeNow)) {
                    return false;
                }
            }
            else if (!reservation.Valid) {
                return true;
            }

            timeInherited = new TimeSpan(0);

            if (reservation.AssociatedActivity != null) {
                timeToInherit = new TimeSpan(-1);
                nodeList = reservation.AssociatedActivity.MyRecurringCpuReservation.TempAssignedNodes;
            }
            else {
                ResolutionAttempt++;
                timeToInherit = timeInherited;
                nodeList = reservation.OriginalThread.AssociatedActivity.MyRecurringCpuReservation.TempAssignedNodes;
            }

            TimeSpan foo = new TimeSpan(0); //TODO: May need a separate flag to signal not interested if 0 isn't good enough
            ReserveSlices(reservation.Start, reservation.Deadline, reservation.Estimate,
                          reservation.ReservTask, // taskId null Activ Reservs
                          out timeLeft, ref timeToInherit,
                          reservation, nodeList, timeNow);

            if (timeLeft.Ticks != 0) {
                ReserveSlices(reservation.Start, reservation.Deadline, reservation.Estimate,
                              reservation.ReservTask, // taskId null Activ Reservs
                              out timeLeft, ref timeToInherit,
                              reservation, tempFreeNodes, timeNow);
            }

            if (timeLeft.Ticks > 0) {
                return false;
            }
            if (reservation.OriginalThread != null && timeInherited.Ticks > 0) {
                OneShotReservation.InheritOnEarliestDeadlineFirst(reservation.SurroundingReservation, timeInherited);
            }

            return true;
        }

        public static void DirectSwitchOnWait()
        {
            if (SchedulerClock.TimeToInterrupt() < RialtoScheduler.MinSlice) {
                return; //Don't do a directed context switch if there isn't much time.
            }
            if (OneShotReservation.CurrentReservation != null) {
                Debug.Assert(OneShotReservation.GuaranteedReservations != null);
                directSwitchTo = OneShotReservation.GuaranteedReservations.GetRunnableThread();
            }
            //  else if ((threadWaiter == GetCurrentThread()) &&
            //       (threadWaiter.AssociatedActivity.RunnableThreads == null)) {
            //      directSwitchTo = pTH(thread);
            //  }
        }

        public static void DirectSwitchOnWakeup()
        {
            if (((OneShotReservation.CurrentReservation != null) &&
                (OneShotReservation.CurrentReservation != OneShotReservation.GuaranteedReservations)) ||
                EarliestDeadlineFirstBlocked) {
                Reschedule();
            }
        }

        public static void DirectSwitchOnConstraint()
        {
            if ((OneShotReservation.CurrentReservation != null) &&
                (OneShotReservation.GuaranteedReservations != null) &&
                (OneShotReservation.GuaranteedReservations.ReservTask != (GetCurrentThread()))) {
                Reschedule();
            }
        }
#endregion

        public static void Reschedule()
        {
            needToReschedule = true;
        }

        public static void ActivityObjAddRef(RialtoActivity activity)
        {
            Interlocked.Increment(ref activity.ReferenceCount);
        }


        public static void ActivityObjRelease(RialtoActivity activity)
        {
            Debug.Assert(activity.ReferenceCount >= 1);
            activity.ReleaseReference();
        }

        public static void ActivityReleaseProtected(RialtoActivity activity)
        {
            Debug.Assert(activity.ReferenceCount >= 1);
            Debug.Assert(Processor.InterruptsDisabled(), "Interrupts not disabled!");
            // XXX Need to implement HelperActivityRelease() that does release with preemption enabled
            // activity.ReleaseReference();
            // DebugStub.Print("Need to implement HelperActivityRelease()\n");
        }


        ///////////////////////////////////////////////////// Debugging Tools.
        //
#if DEBUG_TREE
        static void PrintNode(GraphNode pNode, int level, string indent)
        {
            int k;
            if (pNode == null) {
                DebugStub.Print("\n");
                return;
            }
            if (pNode.Type == GraphNode.NodeType.Used) {
                DebugStub.Print("{0}L{1} -- Per {2} --- Act {3} Slice {4}\n",
                                __arglist(indent, level, pNode.Period,
                                          pNode.DefaultActivity.Id,
                                          pNode.Slice));
            }
            else if (pNode.Type == GraphNode.NodeType.Free) {
                DebugStub.Print("{0}L{1} -- Per {2} --- FREE Slice {3}\n",
                                __arglist(indent, level, pNode.Period,
                                          pNode.Slice));
            }
            else {
                // BRANCH
                DebugStub.Print("{0}L{1} -- Per {2} --- BRANCH\n",
                                __arglist(indent, level, pNode.Period));
                string newIndent = indent + "  ";
                PrintNode (pNode.Left, level+1, newIndent);
                PrintNode (pNode.Right, level+1, newIndent);
                Debug.Assert(pNode.Next == null);
            }
            PrintNode(pNode.Next, level, indent);
        }

        static void PrintSchedPlan(GraphNode SchedPlan, DateTime StartingAt)
        {
            int level= 0;
            DebugStub.Print("Scheduling plan starting at {0}\n",
                            __arglist(StartingAt.Ticks));
            PrintNode(SchedPlan, level, "");

        }
#endif

#if DEBUG_RESERV
        // This code was ported over with PrintNode but hasn't been fully converted to working C# yet
        static TimeSpan ComputeTotalAvailableCpu(GraphNode pNode, int ReservIndex, DateTime TimeNow)
        {
            DateTime TempStart, TempEnd;
            TimeSpan AvailableCpu = 0;
            PRESERVATION pReserv;
            PRESERVATION pResInterest;
            int i;
            pResInterest = (PRESERVATION) POINTER(pNode.ReservArray[ReservIndex]);
            TempStart = maxTime(pResInterest->Start, TimeNow);
            for (i = 0; i < ReservIndex; i++) {
                // the Begin fields are in increasing order:
                pReserv = POINTER(pNode.ReservArray[i]);

                if (!pReserv->Valid) {
                    continue;
                }

                TempEnd = NODE_DEADLINE(pNode.ReservArray[i]);
                if (TempEnd <= TempStart) {
                    continue; // this node reservation doesn't count
                }

                // TempEnd > TempStart &&  NodeReservation->ReservTaskId != TaskId
                // TempStart..min(pReserv->Start, Deadline) can be allocated,
                // max(TempStart, pReserv->Start)..min(TempEnd, Deadline) may be stolen
                AvailableCpu +=
                    ComputeAvailableCpu(TempStart, minTime(min(pReserv->Start, TimeNow),
                    pResInterest->Deadline), pNode);
                TempStart = TempEnd;
            }
            AvailableCpu += ComputeAvailableCpu(TempStart,
                NODE_DEADLINE(pNode.ReservArray[ReservIndex]), pNode);
            //  assert (AvailableCpu != 0);
            return AvailableCpu;
        }

        static void PrintReservationNode(GraphNode pNode, int level, string indent, DateTime TimeNow)
        {
            int i, k;
            if (pNode == null) {
                DebugStub.Print("\n");
                return;
            }

            if (pNode.Type == GraphNode.NodeType.Used ||
                pNode.Type == GraphNode.NodeType.Free) {

                if (pNode.Type == GraphNode.NodeType.Used) {
                    DebugStub.Print("{0}L{1} -- Per {2} --- Act {3} Slice {4}\n",
                                    __arglist(indent, level, pNode.Period,
                                              pNode.DefaultActivity.Id,
                                              pNode.Slice));
                }
                else {
                    // FREE
                    DebugStub.Print("{0}L{1} -- Per {2} --- FREE Slice {3}\n",
                                    __arglist(indent, level, pNode.Period,
                                              pNode.Slice));
                }

                for (i = 0; i < pNode.ReservCount; i++) {
                    PRESERVATION pRes = POINTER(pNode.ReservArray[i]);
                    Debug.Assert(NODE_DEADLINE(pNode.ReservArray[i]) != 0);
                    DebugStub.Print("{0}  R{1} - T{2}:A{3} (T{4}:A{5}) {6}-{7}"+
                                    "{8} {9} Av.{10} Est {11}\n",
                                    __arglist(
                                        indent,
                                        pRes->ReservationId,
                                        (pRes->OriginalThread
                                         ? pRes->OriginalThread.Id : -1),
                                        (pRes->OriginalThread
                                         ? pRes->OriginalThread->pActivity.Id
                                         : pRes->pActivity.Id),
                                        (pRes->ActiveThread
                                         ? pRes->ActiveThread.Id : -1),
                                        (pRes->ActiveThread
                                         ? pRes->ActiveThread->pActivity.Id
                                         : pRes->pActivity.Id),
                                        pRes->Start,
                                        NODE_DEADLINE(pNode.ReservArray[i]),
                                        ((UINT)pNode.ReservArray[i] & 0x02 ? "STL":"NSTL"),
                                        (pRes->Valid ? "Vld":"NVld"),
                                        (pRes->Valid
                                         ? ComputeTotalAvailableCpu(pNode, i, TimeNow.Ticks)
                                         : -1),
                                        pRes->Estimate));
                }
            }
            else {
                // BRANCH
                DebugStub.Print("{0}L{1} -- Per {2} --- BRANCH\n",
                                __arglist(indent, level, pNode.Period));
                string newIndent = indent + "  ";
                PrintReservationNode (pNode.Left, level+1, newIndent, TimeNow);
                PrintReservationNode (pNode.Right, level+1, newIndent, TimeNow);
                Debug.Assert(pNode.Next == null);
            }
            PrintReservationNode(pNode.Next, level, indent, TimeNow);
        }

        void PrintReservations(PRESERVATION pReservation, BOOL ok, DateTime TimeNow)
        {
            int level = 0;

            DebugStub.Print("Reserv {0} (T{1}:A{2}): {3} {4} {5} ({6}) {7} at {8}\n",
                            __arglist(
                                pReservation->ReservationId,
                                (pReservation->OriginalThread != null
                                 ? pReservation->OriginalThread.Id: -1),
                                (pReservation->OriginalThread != null
                                 ? pReservation->OriginalThread->pActivity.Id
                                 : pReservation->pActivity.Id),
                                pReservation->Start,
                                pReservation->Deadline,
                                pReservation->Estimate,
                                (pReservation->Criticality == CRITICAL ? "CRT":"NCRT"),
                                (ok ? "OK":"NO"),
                                TimeNow.Ticks));
            //  DebugStub.Print(" (O {0} + I {0})\n",
            // __arglist(pReservation->OwnEstimate, pReservation->InheritedEstimate));
            PrintReservationNode(pSchedPlan, level, "", TimeNow);
        }
#endif
    }
}
