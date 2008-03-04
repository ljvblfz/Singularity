////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   RialtoThread.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Singularity;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Rialto
{
    /// <summary>
    /// The kernel structure for the thread.  Contains an associated activity, a
    /// stack of reservations executing the activity, a reference for a free
    /// reservation (rather than allocating a new one all the time), a pending
    /// reservation (used when new constraints are being created), a reference to the
    /// thread it’s waiting on (for use in scheduling inheritance), the basic thread
    /// queue and sleep queue pointers, lists of people waiting on mutexes and cv’s
    /// owned by this thread, bookkeeping for the type of waiting that I’m doing, and
    /// general bookkeeping.
    /// </summary>
    public class RialtoThread : ISchedulerThread
    {
        // Thread State values passed to SetState
        // ThreadReady  0    Able to be run but not yet running
        // ThreadWaiting    1    Waiting for object and/or timeout
        // ThreadRunning    2    Current thread (for a given CPU)

        //public enum ThreadState { ThreadReady = 0, ThreadWaiting = 1, ThreadRunning = 2};

        static private ListNode sleepingThreads = null;             // list of sleeping threads
        static private DateTime sleepTimeout = DateTime.MaxValue;   // Next thread wakeup time
        private TimeSpan executionTime;

        public override TimeSpan ExecutionTime
        {
            get { return executionTime; }
        }

        public RialtoActivity AssociatedActivity;    // Thread Activity
        //public RialtoThread.ThreadState       State;      // RUNNING, WAITING, SLEEPING

        public OneShotReservation  ReservationStack;
        // Stack of Reservations, one per active Constraint
        public OneShotReservation  FreeReservation;
        public OneShotReservation  PendingReservation;
        // PendingConstraint if any

        // DI :: NOTE the Owner field in Kernel is used only for Inheritance control
        //      need to REMOVE it -- this will help to locate easy all these functions and
        //      comment them out
        //public int                Epoch;       // inc-ed at every kernel unlock

        public DateTime     StartTime;  // only while on the sleep Q
        public RialtoThread Next;       // Next and previous threads
        public RialtoThread Previous;

        // DI -- ??!! this might replace Next and Previous from above
        public ListNode Queue;            // List of threads in a queue
        // DI -- need it as a thread may wait on a condition (i.e. use Queue)
        // and wait for the timeout (Condition_TimedWait)
        public ListNode SleepQueue;       // Another queue link, for sleeping only

        public int  ReferenceCount;
        public readonly Thread enclosingThread;

        //public interface
        public RialtoThread(Thread thread)
        {
            enclosingThread = thread;
            Queue = new ListNode(this);
            SleepQueue = new ListNode(this);
            AssociatedActivity    = null;
            StartTime  = new DateTime(0);
            //TODO: BUGBUGBUG
            //Tid = Id; REPLACE WITH DEFAULT TASK?
            Queue.Next = Queue.Previous = Queue;
            SleepQueue.Next = SleepQueue.Previous = SleepQueue;
            //State = RialtoThread.ThreadState.ThreadWaiting;
        }

#region ISchedulerThread Members

        public override Thread EnclosingThread
        {
            get { return enclosingThread; }
        }

        //public interface
        public override void SetActivity(ISchedulerActivity iActivityNew)
        {
            // DebugStub.Print("RialtoThread.SetActivity()\n");
            Debug.Assert(iActivityNew != null);
            Debug.Assert(iActivityNew is RialtoActivity);
            RialtoActivity activityNew = (RialtoActivity)iActivityNew;
            RialtoActivity activityOld = AssociatedActivity;


            if (activityOld == activityNew) {
                return;
            }

            bool iflag = Processor.DisableInterrupts();

            if (activityOld != null) {
                //if (State != RialtoThread.ThreadState.ThreadWaiting)
                if (enclosingThread.ThreadState == System.Threading.ThreadState.Running)
                    RialtoScheduler.DequeueRunThread(this);
                RialtoScheduler.ActivityReleaseProtected(activityOld);
            }

            if (activityNew != null) {
                //if (State != RialtoThread.ThreadState.ThreadWaiting)
                if (enclosingThread.ThreadState == System.Threading.ThreadState.Running) {
                    AssociatedActivity = activityNew;
                    RialtoScheduler.EnqueueRunThread(this);
                }
                RialtoScheduler.ActivityObjAddRef(activityNew);
            }
            else if (this == RialtoScheduler.GetCurrentThread()) {
                //TODO: Double Check! //State == RialtoThread.ThreadState.ThreadRunning)
                if (this == RialtoScheduler.GetCurrentThread() && activityOld != null) {
                    RialtoScheduler.UpdateSchedulingStatus();
                }
            }
            AssociatedActivity = activityNew;
            // DebugStub.Print("Exiting RialtoThread.SetActivity()\n");
            Processor.RestoreInterrupts(iflag);
        }

        //public interface
        public override void Start()
        {
            //TODO: No need for assertion perhaps -- default somehow?
            Debug.Assert(AssociatedActivity != null);
            bool iflag = Processor.DisableInterrupts();
            IReady();
            Processor.RestoreInterrupts(iflag);
        }

        //public interface
        public override void Cleanup()
        {
            OneShotReservation reservation;
            // DebugStub.Print("Cleaning RialtoThread\n");

            SetStateWaiting(DateTime.MaxValue);
            // DebugStub.Print("At this point thread is dequeued.\n");

            while ((reservation = ReservationStack) != null) {
                ReservationStack = reservation.Next; // !!!! don't remove Caller's constraints !!!
                reservation.DequeueReservation();
            }

            RialtoScheduler.ActivityObjRelease(AssociatedActivity);

            //TODO: In the "real" system, cleanup is called by someone who will pick next thread, right?
        }

#endregion

        public static DateTime GetSleepTimeout()
        {
            if (sleepingThreads != null) {
                return sleepTimeout;
            }
            else {
                return DateTime.MaxValue;
            }
        }

        public void AddRef()
        {
            ReferenceCount++;
        }

        // DI -- instead of calling this, one should call ::ReleaseActivity
        public void Release()
        {
            ReferenceCount--;
            Debug.Assert(ReferenceCount >= 0);
        }

        public void ReleaseProtected()
        {
            ReferenceCount--;
            Debug.Assert(ReferenceCount >= 0);
        }

        public override void SetStateWaiting(DateTime timeOut)
        {
            bool iflag = Processor.DisableInterrupts();
            InternalSetStateWaiting(timeOut);
            RialtoScheduler.DirectSwitchOnWait();
            Processor.RestoreInterrupts(iflag);
        }

        public void InternalSetStateWaiting(DateTime timeOut)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            // DI -- don't need activity state update
            //This to handle sleeps and timed waits.
            if (timeOut != DateTime.MaxValue) {
                if (timeOut - SystemClock.GetKernelTime() > RialtoScheduler.MinSlice) {
                    Scheduler.LogSleepAdd();
                    StartTime = timeOut;

                    PutOnSleepQueue();
                }
                else {
                    //Sleeping for less than MinSlice is treated as not sleeping/yield.
                    if (PendingReservation != null) {
                        ResolvePendingReservation();
                    }
                    return;
                }
            }

            RialtoScheduler.DequeueRunThread(this); // from RunnableThreads
            if (this == RialtoScheduler.GetCurrentThread()) {
                // if activity blocks during its own regular CPU slice; same code as in Sleep!
                if ((AssociatedActivity.RunnableThreads == null) &&
                   (RialtoScheduler.CurrentPlanNode.Type == GraphNode.NodeType.Used) &&
                   (RialtoScheduler.CurrentPlanNode.DefaultActivity == AssociatedActivity) &&
                   ((AssociatedActivity.MyRecurringCpuReservation.SliceLeft =
                     RialtoScheduler.CurrentPlanNode.NextExec + RialtoScheduler.CurrentPlanNode.Slice
                     - SystemClock.GetKernelTime()) >= RialtoScheduler.MinSlice)) {

                    AssociatedActivity.LastNode = RialtoScheduler.CurrentPlanNode; // record it
                }
            }

        }


        // DI -- this is the function in !LAXITY version
        //TODO: REPLACE WITH ENCLOSING LOGIC!
        //public void SetState(ThreadState newState)
        //{
        //    State = newState;
        //    Debug.Assert(newState != ThreadState.ThreadWaiting);
        //}

        // DI -- safe
        //TODO: REPLACE WITH ENCLOSING LOGIC!
        void IReady()
        {
            Debug.Assert(Processor.InterruptsDisabled());
            // TODO -- maybe something for directed context switch
            // DI -- don't need PutThreadOnReadyQueue outside schedule.c
            // PutThreadOnReadyQueue(thread);

            RialtoScheduler.EnqueueRunThread(this);

            // When an activity becomes runnable during
            // its own regular CPU slice, a reschedule occurs.
            if (AssociatedActivity.LastNode != null)
            {
                RialtoScheduler.EnqueueStandbyActivity(AssociatedActivity); // sets 'thread.AssociatedActivity.LastNode' to null
                Debug.Assert(AssociatedActivity.LastNode == null && RialtoScheduler.CurrentPlanNode != null, "Removing unused code! IF EXCEPTION HAPPENS, CODE SHOULD BE REPLACED");
                //                if (RialtoScheduler.CurrentPlanNode == thread.AssociatedActivity.LastNode) { //TODO: This is really an equivalence with null?
                //                    RialtoScheduler.Reschedule();
                //                }
            }
        }

        // DI -- this is called only from PutThreadOnSleepQueue
        // DI -- merge them ??
        static ListNode InsertThreadOnSleepQueue(RialtoThread thread, ListNode listThreadHead)
        {
            RialtoThread threadHead, threadTail, tempThread;

            if (listThreadHead == null) {
                // Queue empty
                return thread.SleepQueue;
            }

            threadHead = GetThreadFromListNode(listThreadHead);
            threadTail = GetThreadFromListNode(threadHead.SleepQueue.Previous);

            if (thread.StartTime >= threadTail.StartTime) {
                thread.SleepQueue.InsertIntoList(threadTail.SleepQueue);
                // DI -- if merge: update sleepTimeout here
                return threadHead.SleepQueue;
            }


            if (thread.StartTime < threadHead.StartTime) {
                thread.SleepQueue.InsertIntoList(threadTail.SleepQueue);
                return thread.SleepQueue;
            }

            tempThread = GetThreadFromListNode(threadHead.SleepQueue.Next);
            for (;;) {
                if (tempThread.StartTime > thread.StartTime) {
                    thread.SleepQueue.InsertIntoList(tempThread.SleepQueue.Previous);
                    break;
                }
                tempThread = GetThreadFromListNode(tempThread.SleepQueue.Next);
            }

            return threadHead.SleepQueue;
        }

        public void PutOnSleepQueue()
        {
            sleepingThreads = InsertThreadOnSleepQueue(this,sleepingThreads);
            sleepTimeout = (GetThreadFromListNode(sleepingThreads)).StartTime;
        }


        //        public void Sleep(DateTime time)
        //        {
        //            if (time - SystemClock.GetKernelTime() > GraphNode.MinSlice) {
        //                RialtoScheduler.LogSleepAdd();
        //                StartTime = time;
        //
        //                SetStateWaiting();
        //
        //                // DI -- need it, called from WaitCond also
        //                PutOnSleepQueue();
        //            }
        //        }

        // Di -- safe -- optimized
        void PullThreadOffSleepQueue()
        {
            ListNode listThread, listNewHead;

            listThread = SleepQueue;
            listNewHead = listThread.ListRemove();

            if (sleepingThreads == listThread) {
                if ((sleepingThreads = listNewHead) != null) {
                    sleepTimeout = (GetThreadFromListNode(sleepingThreads)).StartTime;
                }
                else {
                    sleepTimeout = DateTime.MaxValue;
                }
            }
        }

        private void ResolvePendingReservation()
        {
            Debug.Assert(Processor.InterruptsDisabled());
            Debug.Assert(PendingReservation != null);
            bool admitted = OneShotReservation.ResolveConstraint(this);
            //else { DebugStub.WriteLine("Wake()-Resolve returned non S_OK"); }
            if (ReservationStack.EnclosingTask != null) {
                // Not clear if this will work in real system or not.  Here for simulator mainly.
                ReservationStack.EnclosingTask.UpdateSchedulingState(admitted, ReservationStack.Deadline, ReservationStack.ResourcesGranted);
            }
            else {
                Debug.Assert(false);
            }
        }

        // DI -- safe
        public override void Wake()
        {
            bool iflag = Processor.DisableInterrupts();
            PullThreadOffSleepQueue();
            IReady();

            if (PendingReservation != null) {
                ResolvePendingReservation();
            }

            RialtoScheduler.DirectSwitchOnWakeup();
            Processor.RestoreInterrupts(iflag);
        }


        OneShotReservation AllocationReservation()
        {
            OneShotReservation reservation = FreeReservation;

            Debug.Assert(reservation != null);
            FreeReservation = null;
            reservation.Clear();
            return reservation;
        }


        public void IpcCheckFreeConstraint()
        {
            if (FreeReservation == null) {
                OneShotReservation reservation;

                reservation = RialtoScheduler.IpcAllocateReservation();

                if (reservation == null) {
                    reservation = RialtoScheduler.AllocateReservation();
                }

                FreeReservation = reservation;
            }
        }

        // If this thread doesn't have a free OneShotReservation reserved
        // for its use, then try to grab one from the global free list.
        // If there aren't any there, then allocate a new one.
        void ICheckFreeConstraint()
        {
            if (FreeReservation == null) {
                OneShotReservation reservation;

                reservation = RialtoScheduler.AllocateReservation();

                FreeReservation = reservation;
            }
        }


        // Free the Reservations associated with a thread.
        // Used by thread cleanup.
        public void IFreeConstraints()
        {
            OneShotReservation reservation;

            //  Debug.Assert(!Processor.InterruptsDisabled());

            if (FreeReservation != null) {
                RialtoScheduler.IpcFreeReservation(FreeReservation);
            }

            while ((reservation = ReservationStack) != null) {
                ReservationStack = reservation.SurroundingReservation;
                RialtoScheduler.ReleaseReservation(reservation);
            }
        }

        // DI -- safe but optimize to  because don't need QueueType
        public ListNode Dequeue()
        {
            return Queue.ListRemove();
        }

        public static ListNode QueueFifo(RialtoThread thread, ListNode threadHead)
        {
            if (threadHead != null) {
                thread.Queue.InsertIntoList(threadHead.Previous);
            }
            else {
                threadHead = thread.Queue;
            }
            return threadHead;
        }

        // DI -- safe
        public static RialtoThread GetThreadFromListNode(ListNode listNode)
        {
            Debug.Assert(listNode == null || listNode.Data is RialtoThread);
            if (listNode == null) {
                return null;
            }
            if (listNode.Data is RialtoThread) {
                return (RialtoThread)listNode.Data;
            }
            else {
                return null;
            }
        }

        public static void WakeThreads()
        {
            RialtoThread    ptemp;
            Debug.Assert(Processor.InterruptsDisabled());
            while ((ptemp = RialtoThread.GetThreadFromListNode(sleepingThreads)) != null &&
                  ptemp.StartTime < RialtoScheduler.SchedulingTime + RialtoScheduler.LA_Time) {
                //TODO: LA_Time here?
                sleepingThreads = sleepingThreads.ListRemove();

                RialtoScheduler.MoveToStandby(ptemp, true); //NOTE: Sets LastNode to null, prevents IReadythread from also EnqueueingStandbyActivity
                ptemp.IReady();

                if (ptemp.PendingReservation != null) {
                    ptemp.ResolvePendingReservation();
                }
                Scheduler.LogWakeThread(ptemp.EnclosingThread);
            }

            if (ptemp != null)
                sleepTimeout = ptemp.StartTime;
            else
                sleepTimeout = DateTime.MaxValue;
        }


        /// <summary>
        ///
        /// </summary>
        /// <param name="thread"></param>
        /// <param name="EndPrevious"></param>
        /// <param name="timeConstraint"></param>
        /// <param name="timeNow">TODO: UNUSED</param>
        /// <returns></returns>
        public bool BeginConstraintBeforeWaitValidate(bool endPrevious,
                                                       ref TimeConstraint timeConstraint,
                                                       DateTime timeNow)
        {
            if (endPrevious &&      // check to have a valid constraint in the stack.
                (ReservationStack == null || ReservationStack.OriginalThread == null)) {
                return false;
            }

            if (timeConstraint.Estimate.Ticks < 0) {
                return false;
            }

            // By this time, something should be in FreeReservation
            // grab it now such to enable the EndPreviousConstraint to
            // save its reservation in the cache
            Debug.Assert(FreeReservation != null);
            Debug.Assert(FreeReservation != OneShotReservation.CurrentReservation);

            PendingReservation = AllocationReservation();

            return true;
        }

        public void AddExecutionTime(TimeSpan delta)
        {
            executionTime += delta;
        }

        public override ISchedulerTask PrepareDelayedTask(ISchedulerTask taskToEnd,
                                                          ref TimeConstraint timeConstraint,
                                                          DateTime timeNow)
        {
            // DI -- the Next calls are from TimedWaitAndBeginConstraint
            IpcCheckFreeConstraint();

            Debug.Assert(taskToEnd == null || taskToEnd == ReservationStack);
            bool endPrevious = (taskToEnd != null);

            if (!BeginConstraintBeforeWaitValidate(endPrevious, ref timeConstraint, timeNow)) {
                goto Exit;
            }

            OneShotReservation.BeginConstraintBeforeWait(this, endPrevious, timeConstraint, timeNow);
          Exit:
            return PendingReservation;
        }
    }
}
