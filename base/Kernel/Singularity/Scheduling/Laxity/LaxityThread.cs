////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   LaxityThread.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Singularity;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Laxity
{
    /// <summary>
    /// The kernel structure for the thread.  Contains an associated activity, a
    /// stack of reservations executing the activity, a reference for a free
    /// reservation (rather than allocating a new one all the time), a pending
    /// reservation (used when new constraints are being created), a reference to the
    /// thread it�s waiting on (for use in scheduling inheritance), the basic thread
    /// queue and sleep queue pointers, lists of people waiting on mutexes and cv�s
    /// owned by this thread, bookkeeping for the type of waiting that I�m doing, and
    /// general bookkeeping.
    /// </summary>
    public class LaxityThread : ISchedulerThread
    {
        private static ListNode     SleepingThreads = null;             // list of sleeping threads
        private static DateTime     SleepTimeout = DateTime.MaxValue;   // Next thread wakeup time

        private TimeSpan     executionTime;
        public override TimeSpan ExecutionTime
        {
            get { return executionTime; }
        }

        public LaxityActivity      AssociatedActivity;    // Thread Activity
        //public LaxityThread.ThreadState       State;      // RUNNING, WAITING, SLEEPING

        public OneShotReservation   ReservationStack;// Stack of Reservations, one per active Constraint
        public OneShotReservation   FreeReservation;
        public OneShotReservation   PendingReservation;     // PendingConstraint if any
        // DI -- corresponds to MMID Tid
        //TODO: Replace with TASK public int                Tid;        // Id to mark node reservations made for this Constraint Stack
        // DI :: NOTE the Owner field in Kernel is used only for Inheritance control
        //      need to REMOVE it -- this will help to locate easy all these functions and
        //      comment them out
        //public int                Epoch;       // inc-ed at every kernel unlock

        public DateTime     StartTime;  // only while on the sleep Q

        public LaxityThread         Next;       // Next and previous threads
        public LaxityThread         Previous;

        // DI -- maybe don't use it, see vtlb.c
        // UINT QueueType;            // Type of queue thread is on, if any.

        // DI -- ??!! this might replace Next and Previous from above
        public ListNode Queue;            // List of threads in a queue.
        // DI -- need it as a thread may wait on a condition (i.e. use Queue)
        // and wait for the timeout (Condition_TimedWait)
        public ListNode SleepQueue;       // Another queue link, for sleeping only.

        public int  ReferenceCount;
        public readonly Thread enclosingThread;

        public LaxityProcessor ActiveProcessor; //Set only when running on processor -- no other processor may take the thread to run.

        //public interface
        public LaxityThread(Thread thread)
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
            //State = LaxityThread.ThreadState.ThreadWaiting;
        }

#region ISchedulerThread Members

        public override Thread EnclosingThread
        {
            get { return enclosingThread; }
        }

        //public interface
        public override void SetActivity(ISchedulerActivity iActivityNew)
        {
            DebugStub.Print("LaxityThread.SetActivity()\n");
            Debug.Assert(iActivityNew != null);
            Debug.Assert(iActivityNew is LaxityActivity);
            Debug.Assert(((LaxityActivity)iActivityNew).ReferenceCount > 0);
            LaxityActivity activityNew = (LaxityActivity)iActivityNew;
            LaxityActivity activityOld = AssociatedActivity;


            if (activityOld == activityNew)
                return;

            bool iflag = Processor.DisableInterrupts();

            if (activityOld != null) {
                //if (State != LaxityThread.ThreadState.ThreadWaiting)
                if (enclosingThread.ThreadState == System.Threading.ThreadState.Running)
                    LaxityScheduler.DequeueRunThread(this);
                LaxityScheduler.ActivityReleaseProtected(activityOld);
            }

            if (activityNew != null) {
                //if (State != LaxityThread.ThreadState.ThreadWaiting)
                if (enclosingThread.ThreadState == System.Threading.ThreadState.Running) {
                    AssociatedActivity = activityNew;
                    LaxityScheduler.EnqueueRunThread(this, false);
                }
                LaxityScheduler.ActivityObjAddRef(activityNew);
            }
            else if (this == LaxityScheduler.GetCurrentThread()) {
                //TODO: Double Check! //State == LaxityThread.ThreadState.ThreadRunning)
                if (this == LaxityScheduler.GetCurrentThread() && activityOld != null) {
                    LaxityScheduler.UpdateSchedulingStatus();
                }
            }
            AssociatedActivity = activityNew;
            DebugStub.Print("Exiting LaxityThread.SetActivity()\n");
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
            DebugStub.Print("Cleaning LaxityThread\n");

            SetStateWaiting(DateTime.MaxValue);
            DebugStub.Print("At this point thread is dequeued.\n");

            while ((reservation = ReservationStack) != null) {
                ReservationStack = reservation.Next; // !!!! don't remove Caller's constraints !!!
                reservation.DequeueReservation();
            }

            LaxityScheduler.ActivityObjRelease(AssociatedActivity);

            //TODO: In the "real" system, cleanup is called by someone who will pick next thread, right?
        }

#endregion

        public static DateTime GetSleepTimeout()
        {
            if (SleepingThreads != null) {
                return SleepTimeout;
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
            Processor.RestoreInterrupts(iflag);
        }

        public void InternalSetStateWaiting(DateTime timeOut)
        {
            Debug.Assert(Processor.InterruptsDisabled());
            // DI -- don't need activity state update
            //This to handle sleeps and timed waits.
            if (timeOut != DateTime.MaxValue) {
                if (timeOut - SystemClock.GetKernelTime() > LaxityScheduler.MinSlice) {
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

            LaxityScheduler.DequeueRunThread(this); // from RunnableThreads
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

            LaxityScheduler.EnqueueRunThread(this, true);
        }

        // DI -- this is called only from PutThreadOnSleepQueue
        // DI -- merge them ??
        static ListNode InsertThreadOnSleepQueue(LaxityThread thread, ListNode listThreadHead)
        {
            LaxityThread threadHead, threadTail, tempThread;

            if (listThreadHead == null) {
                // Queue empty.
                return thread.SleepQueue;
            }

            threadHead = GetThreadFromListNode(listThreadHead);
            threadTail = GetThreadFromListNode(threadHead.SleepQueue.Previous);

            if (thread.StartTime >= threadTail.StartTime) {
                thread.SleepQueue.InsertIntoList(threadTail.SleepQueue);
                // DI -- if merge: update SleepTimeout here
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
            SleepingThreads = InsertThreadOnSleepQueue(this,SleepingThreads);
            SleepTimeout = (GetThreadFromListNode(SleepingThreads)).StartTime;
        }

        //        public void Sleep(DateTime time)
        //        {
        //            if (time - SystemClock.GetKernelTime() > Node.MinSlice)
        //            {
        //
        //                Scheduler.LogSleepAdd();
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

            if (SleepingThreads == listThread) {
                if ((SleepingThreads = listNewHead) != null) {
                    SleepTimeout =(GetThreadFromListNode(SleepingThreads)).StartTime;
                }
                else {
                    SleepTimeout = DateTime.MaxValue;
                }
            }
        }

        private void ResolvePendingReservation()
        {
            Debug.Assert(Processor.InterruptsDisabled());
            Debug.Assert(PendingReservation != null);
            bool admitted = OneShotReservation.ResolveConstraint(this);
            if (ReservationStack.EnclosingTask != null) {
                // Not clear if this will work in real system or not.  Here for simulator mainly.
                ReservationStack.EnclosingTask.UpdateSchedulingState(admitted, ReservationStack.Deadline, ReservationStack.ResourcesGranted);
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

                reservation = LaxityScheduler.IpcAllocateReservation();

                if (reservation == null) {
                    reservation = LaxityScheduler.AllocateReservation();
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
                FreeReservation = LaxityScheduler.AllocateReservation();
            }
        }


        // Free the Reservations associated with a thread.
        // Used by thread cleanup.
        public void IFreeConstraints()
        {
            OneShotReservation reservation;

            //  Debug.Assert(!Processor.InterruptsDisabled());

            if (FreeReservation != null) {
                LaxityScheduler.IpcFreeReservation(FreeReservation);
            }
            while ((reservation = ReservationStack) != null) {
                ReservationStack = reservation.SurroundingReservation;
                LaxityScheduler.ReleaseReservation(reservation);
            }
        }

        // DI -- safe but optimize to  because don't need QueueType
        public ListNode Dequeue()
        {
            return Queue.ListRemove();
        }

        public static ListNode QueueFifo(LaxityThread thread, ListNode threadHead)
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
        public static LaxityThread GetThreadFromListNode(ListNode listNode)
        {
            Debug.Assert(listNode == null || listNode.Data is LaxityThread);
            if (listNode == null) {
                return null;
            }
            if (listNode.Data is LaxityThread) {
                return (LaxityThread)listNode.Data;
            }
            else {
                return null;
            }
        }

        public static void WakeThreads()
        {
            LaxityThread    ptemp;
            Debug.Assert(Processor.InterruptsDisabled());
            while ((ptemp = LaxityThread.GetThreadFromListNode(SleepingThreads)) != null &&
                  ptemp.StartTime < LaxityScheduler.SchedulingTime + LaxityScheduler.LA_Time) {
                //TODO: LA_Time here?
                SleepingThreads = SleepingThreads.ListRemove();

                ptemp.IReady();

                if (ptemp.PendingReservation != null) {
                    ptemp.ResolvePendingReservation();
                }
                Scheduler.LogWakeThread(ptemp.EnclosingThread);
            }

            if (ptemp != null) {
                SleepTimeout = ptemp.StartTime;
            }
            else {
                SleepTimeout = DateTime.MaxValue;
            }
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
            //  grab it now such to enable the EndPreviousConstraint to
            //  save its reservation in the cache
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