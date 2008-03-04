////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   OneShotReservation.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Microsoft.Singularity;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Robin
{
    /// <summary>
    /// OneShotReservations are created from tasks for the calling thread.
    /// It has fields to track the thread calling and the thread waiting on,
    /// the activity associated when it becomes an activity reservation, its
    /// surrounding reservation, its place on the free reservation list,
    /// general bookkeeping, bookkeeping for possible stolen time, and
    /// references for the general reservation list.
    /// </summary>
    public class OneShotReservation : ISchedulerTask
    {

        static OneShotReservation  guaranteedReservations = null;
        public static OneShotReservation GuaranteedReservations
        {
            get { return guaranteedReservations; }
        }

        static OneShotReservation  idleReservations = null;
        public static OneShotReservation IdleReservations
        {
            get { return idleReservations; }
        }

        static OneShotReservation  currentReservation = null;
        public static OneShotReservation CurrentReservation
        {
            get { return currentReservation; }
            //set { currentReservation = value; }
        }

        public Hashtable resourcesGranted = new Hashtable();
        public override Hashtable ResourcesGranted
        {
            get {
                resourcesGranted[CpuResource.Provider().ResourceString] = CpuResource.Provider().TimeToCpu(InitialEstimate);
                return resourcesGranted;
            }
        }


        private Task enclosingTask;
        public override Task EnclosingTask
        {
            get { return enclosingTask; }
            set { enclosingTask = value; }
        }

        public OneShotReservation      SurroundingReservation;
        public OneShotReservation      FreeListNext;
        public RobinThread             ReservTask;

        TimeSpan     constraintExecution;
        public TimeSpan ConstraintExecution
        {
            get { return constraintExecution; }
        }

        TimeSpan     initialThreadExecution;

        public DateTime     Start;
        public DateTime     Deadline;
        public TimeSpan     Estimate;
        public TimeSpan     RelativeDeadline;

        public TimeSpan     InitialEstimate; // test only
        public QueueType    MyQueueType;
        public bool         Guaranteed;
        public bool         Valid;
        public bool         Satisfied;

        // Reservations can be made for Threads XOR Activities!
        public RobinThread         OriginalThread; // always the constraint thread -- non-null until the reservation is complete
        //public RobinThread           ActiveThread;   // the above, or the thread blocking it
        //public int                ActiveThreadEpoch;
        public RobinActivity      AssociatedActivity;        // the two fields above must be null!


        public OneShotReservation      Next;
        public OneShotReservation      Previous;

        public TimeSpan     InheritedEstimate;  // Stolen => Inherited, no longer critical
        public int          ResolutionEpoch;    // Stolen => Resolution, since not stealing.

        public int          ReferenceCount;

        public OneShotReservation()
        {
        }

        public void Clear()
        {
            //ActiveThread = null;
            //ActiveThreadEpoch = 0;
            constraintExecution = new TimeSpan(0); //Move to enclosing class
            initialThreadExecution = new TimeSpan(0); //Move to enclosing class
            Deadline = new DateTime(0);
            RelativeDeadline = new TimeSpan(0);
            Estimate = new TimeSpan(0);
            FreeListNext = null;
            Guaranteed = false;
            InitialEstimate = new TimeSpan(0);
            Next = null;
            OriginalThread = null;
            AssociatedActivity = null;
            Previous = null;
            MyQueueType = QueueType.NoQueue;
            ReferenceCount = 0;
            ReservTask = null;
            Satisfied = false;
            Start = new DateTime(0);
            SurroundingReservation = null;
            ResolutionEpoch = 0;
            InheritedEstimate = new TimeSpan(0);
            Valid = false;
            if (enclosingTask != null) {
                enclosingTask.ClearSchedulerTask();
                enclosingTask = null;
            }

        }

        // find the runnable thread of a reservation:
        public RobinThread GetRunnableThread()
        {
//            RobinThread thread;
//
            if (AssociatedActivity != null) {
                // if an activity reservation
                return AssociatedActivity.GetRunnableThread();
            }

            Debug.Assert(OriginalThread != null);

//            if (  (ActiveThread == OriginalThread) &&
//                (ActiveThread.EnclosingThread.ThreadState == System.Threading.ThreadState.Running))
//                return ActiveThread;
//
//            thread = OriginalThread; // search for active thread starting w/ original thread
//
//            if ((ActiveThread != null) &&
//                (ActiveThread.Epoch == ActiveThreadEpoch) ) {
//                if (ActiveThread.EnclosingThread.ThreadState == System.Threading.ThreadState.Running) {
//#if DEBUG_MUTEX
//                  DebugStub.WriteLine("Mutex inheritance (Epoch ok): Orig {0} Active {1} GO ON",
//                      thread.Id, ActiveThread.Id);
//#endif
//                    return ActiveThread;
//                }
//            }
            //CKillian -- can't do this because of the array of blockedOn.
            //    else if (ActiveThread.EnclosingThread.IsSleeping()) {
            //        // more efficient: search for active thread starting w/ current active thread
            //        thread = ActiveThread;
            //    }
            //    else {
            //        Debug.Assert(false);
            //    }
            //}

            //A "smarter" thing to do would be to have GetRunnableBeneficiary check for
            //      the ActiveProcessor case.  But as it stands that's info in the scheduler only
            //      the "good" news is that if we return null based on the active processor case
            //      we at least know someone is current running on a processor who would inherit
            //      time from us.
            Thread temp = /*Active*/OriginalThread.EnclosingThread.GetRunnableBeneficiary();
            if (temp == null || ((RobinThread)temp.SchedulerThread).ActiveProcessor != null) {
                return null;
            }
            else {
                return (RobinThread)temp.SchedulerThread;
            }

        }

        public void StopThreadExecution(RobinThread thread)
        {
            Debug.Assert(thread.ExecutionTime >= initialThreadExecution);
            constraintExecution += thread.ExecutionTime - initialThreadExecution;
            //Between two reservations which are atomically stopped/started, some
            //      time is charged against the default parent resource container.  This
            //      is because I changed the way we account for tasks to do the general
            //      rule we wanted.  In particular, I update the scheduling status just
            //      before resolving a new reservation.  Not sure what the right fix is,
            //      so I'm leaving it.  The assertion below will reveal the "error."  The
            //      old code to track CPU usage is still active under constraintExecution,
            //      but isn't presently exposed to the applications.  Currently UpdateSchedulingStatus()
            //      updates the CPU resources used for the whole task stack.
            //Debug.Assert(CpuResource.Provider().TimeToCpu(constraintExecution).Cycles == ((CpuResourceAmount)enclosingTask.resourcesUsed[CpuResource.Provider().ResourceString]).Cycles);
            //EnclosingTask.AddResourceAmountUsed(CpuResource.Provider().ResourceString, CpuResource.Provider().TimeToCpu(constraintExecution));
        }

        public void StartThreadExecution(RobinThread thread)
        {
            initialThreadExecution = thread.ExecutionTime;
        }

        public void StartThreadExecution(RobinThread thread, TimeSpan delta)
        {
            initialThreadExecution = thread.ExecutionTime + delta;
        }

        public static void BeginConstraintBeforeWait(RobinThread thread,
                                                            bool endPrevious,
                                                            TimeConstraint timeConstraint,
                                                            DateTime timeNow)
        {
            OneShotReservation reservation;

            Debug.Assert(thread == RobinScheduler.GetCurrentThread());
            Debug.Assert(!Processor.InterruptsDisabled());
            if (endPrevious) {
                bool iflag = Processor.DisableInterrupts();
                EndPreviousConstraint(thread, timeNow);
                Processor.RestoreInterrupts(iflag);
            }

            RobinScheduler.UpdateSchedulingStatus();

            reservation = thread.PendingReservation;

            reservation.StartThreadExecution(thread, (timeNow - RobinScheduler.SchedulingTime));
            reservation.constraintExecution = new TimeSpan(0);
            reservation.Start = timeConstraint.Start;
            reservation.Deadline = timeConstraint.Deadline;
            reservation.RelativeDeadline = timeConstraint.RelativeDeadline;
            reservation.Estimate = timeConstraint.Estimate;
            reservation.Valid = reservation.Guaranteed = true;
            reservation.MyQueueType = QueueType.NoQueue;
            reservation.ReservTask = thread;
            reservation.OriginalThread = /*reservation.ActiveThread =*/ thread;
            thread.AddRef();
            //thread.AddRef();
            //reservation.ActiveThreadEpoch = thread.Epoch;
            reservation.AssociatedActivity = null;
            reservation.Next = reservation.Previous = null;

        }


        // DI -- this is a new function used in both Begin and EndConstraint
        public static bool EndPreviousConstraint(RobinThread thread, DateTime timeNow)
        {
            OneShotReservation reservation;
            OneShotReservation reservationNext;
            bool            success;
            QueueType       tempQueue; //Value assigned but never used.

            Debug.Assert(Processor.InterruptsDisabled());

            reservation = thread.ReservationStack;

            success = (timeNow <= reservation.Deadline);
            RobinScheduler.UpdateSchedulingStatus();
            reservation.StopThreadExecution(thread);

            Debug.Assert(reservation.ConstraintExecution.Ticks >= 0);
//            if (timeTaken.Ticks != 0) {
//                timeTaken = reservation.ConstraintExecution;
//                Debug.Assert( timeTaken.Ticks >= 0);
//            }

            thread.ReservationStack = reservation.SurroundingReservation;
            reservation.SurroundingReservation = null;

            if (currentReservation == reservation) {
                // note that it might be the case that
                // a reservation for the task id exist on the guaranteed Q but the
                // the actual (executed) constraint is in the UnfinishedQ
                currentReservation = null; // ????????????
                RobinScheduler.Reschedule();
            }
            Debug.Assert(reservation.OriginalThread == thread);
            Debug.Assert(reservation.Start <= timeNow);

            if (thread.ReservationStack == null) {
                if (reservation.Estimate >= RobinScheduler.MinSlice) {
                    // the constraint used less than estimated:
                    Debug.Assert(reservation.MyQueueType == QueueType.GuaranteedQueue);
#if false
                    Debug.Assert((reservation.ActiveThread == null) ||
                                 (reservation.ActiveThread.Epoch != reservation.ActiveThreadEpoch) ||
                                 (reservation.ActiveThread == reservation.OriginalThread));
                    reservation.ActiveThread.ReleaseProtected();
#endif
                    reservation.OriginalThread.ReleaseProtected();
                    reservation.AssociatedActivity = reservation.OriginalThread.AssociatedActivity; // and make it an activity reserv
                    reservation.OriginalThread = null;
                    //reservation.ActiveThread = null;
                    reservation.ReservTask = null;
                    RobinScheduler.ActivityObjAddRef(reservation.AssociatedActivity);
                    // leave it on whatever Q it happens to be on
                }
                else {
                    reservation.DequeueReservation(); // from whatever Q it happens to be on
                }
            }
            else {
                tempQueue = reservation.MyQueueType;
                reservationNext = thread.ReservationStack;
                reservationNext.Estimate += reservation.Estimate;
                reservationNext.constraintExecution += reservation.constraintExecution;
                reservationNext.StartThreadExecution(thread);
//                Debug.Assert( ((reservation.MyQueueType == QueueType.GuaranteedQueue) &&
//                    (reservationNext.MyQueueType == QueueType.NoQueue)) ||
//                    ((reservation.MyQueueType == QueueType.UnfinishedQueue) &&
//                    ((reservationNext.Estimate.Ticks <= 0) || (reservationNext.MyQueueType != QueueType.UnfinishedQueue ))));
                reservation.Estimate = new TimeSpan(0);
                Debug.Assert(reservation.Next != null);
                reservation.DequeueReservation();
                ReplaceOnEarliestDeadlineFirst(reservationNext, timeNow);

            }
            // fprintf(stdout,"EndPrevConstr 0x%x %d\n", reservation, reservation.ReferenceCount);
            reservation.ReleaseReservationProtected();

            Scheduler.LogEndConstraint();

            if (success == false) {
                Scheduler.LogSchedulerLate();
            }
            return success;
        }

        public static bool ResolveConstraint(RobinThread thread)
        {
            TimeSpan timeLeft, ownNodesTimeToSteal = new TimeSpan(1);
            DateTime start, deadline;
            TimeSpan timeInherited;
            OneShotReservation pendingReservation = thread.PendingReservation;
            OneShotReservation reservationPrevious;
            DateTime timeNow = SystemClock.GetKernelTime();
            bool ok = false;

            Debug.Assert(Processor.InterruptsDisabled());
            Debug.Assert(pendingReservation != null);

            Scheduler.LogResolveConstraint();

            thread.PendingReservation = null;   // just clean the place

            Debug.Assert(pendingReservation.Start.Ticks >= 0);
            start = pendingReservation.Start;
            if (start < timeNow) {
                start = timeNow;
            }
            Debug.Assert(pendingReservation.Deadline.Ticks >= 0);
            deadline = pendingReservation.Deadline;
            if (deadline.Ticks == 0) {
                deadline = timeNow + pendingReservation.RelativeDeadline;
            }

            if (thread.ReservationStack != null) {
                OneShotReservation reservation = thread.ReservationStack;
                while (!reservation.Valid && ((reservation = reservation.SurroundingReservation)!= null)) {
                    // nothing.
                }
                if (reservation!= null) {
                    deadline = RobinScheduler.minTime(deadline, reservation.Deadline);
                }
            }

            // update data ure
            pendingReservation.Start = start;
            pendingReservation.Deadline = deadline;
            //TODO: Should be for SIM ONLY!
            Scheduler.LogReservationId(pendingReservation);           // SIM only
            timeInherited = new TimeSpan(0);
            timeLeft = new TimeSpan(0);
            ok = true;
            pendingReservation.InitialEstimate = pendingReservation.Estimate;
            reservationPrevious = thread.ReservationStack;
            pendingReservation.SurroundingReservation= thread.ReservationStack;
            thread.ReservationStack = pendingReservation;
            RobinScheduler.AddRefReservation(pendingReservation);
            if (reservationPrevious != null) {
                reservationPrevious.constraintExecution
                    += pendingReservation.initialThreadExecution
                    - reservationPrevious.initialThreadExecution;
            }
            //
            // Reschedule()  called if currently we are executing on a pendingReservation
            //                              and the EarliestDeadlineFirst top changed
            //              or   the current pendingReservation goes into the idle list
            //                  solve in SetStateWaiting
            //
            pendingReservation.EnqueueReservation(timeNow);

            return ok;
        }

        void EnqueueReservation(DateTime timeNow)
        {
            OneShotReservation temp;

            Debug.Assert((AssociatedActivity != null) ||
                (OriginalThread != null));

            // Infeasible or (Estimate == 0) Reservations:
            if (Estimate <= RobinScheduler.AFewSlice) {
                RobinActivity activity;
                Debug.Assert(OriginalThread != null);
                Debug.Assert(AssociatedActivity == null);
                activity = OriginalThread.AssociatedActivity;
                MyQueueType = QueueType.UnfinishedQueue;
                if (activity.UnfinishedConstraints == null) {
                    Next = Previous = this;
                    activity.UnfinishedConstraints = this;
                }
                else {
                    // no order enforced
                    Next = activity.UnfinishedConstraints;
                    Previous = activity.UnfinishedConstraints.Previous;
                    activity.UnfinishedConstraints.Previous.Next = this;
                    activity.UnfinishedConstraints.Previous = this;
                    activity.UnfinishedConstraints = this; // optional
                }
                //  DebugStub.WriteLine("Add: EnqueueReserve  1 0x{0:x} {1}", this, ReferenceCount);
                RobinScheduler.AddRefReservation(this);
                return;
            }
            //  Debug.Assert(Valid);

            // Idle Reservations:
            if (Start > timeNow + RobinScheduler.AFewSlice) {
                // take thread off activity RoundRobin queue and simulate a sleep
                //    i.e. don't set a timer interrupt

                OriginalThread.InternalSetStateWaiting(DateTime.MaxValue);

#if DEBUG_START_RESERV
                DebugStub.WriteLine("Put reservation {0}:{1} in IdleQueue\n", OriginalThread,
                    ReservationId);
#endif
                Debug.Assert(OriginalThread != null);
                MyQueueType = QueueType.IdleQueue;

                if (idleReservations == null) {
                    Next = Previous = this;
                    idleReservations = this;
                }
                else {
                    temp = idleReservations;
                    if (Start < temp.Start) {
                        idleReservations = this;
                    }
                    else {
                        do {
                            temp = temp.Next;
                        } while (temp != idleReservations &&
                                 Start >= temp.Start); // >= is compulsory to keep it 'FIFO'
                    } // insert before 'temp'
                    Next = temp;
                    Previous = temp.Previous;
                    temp.Previous.Next = this;
                    temp.Previous = this;
                }
                //  fprintf(stdout,"Add: EnqueueReserve  2 0x%x %d\n", this, ReferenceCount);
                RobinScheduler.AddRefReservation(this);
                return;
            }

            // Guaranteed OneShotReservation:
            MyQueueType = QueueType.GuaranteedQueue;
            if (GuaranteedReservations == null) {
                Next = Previous = this;
                guaranteedReservations = this;
            }
            else {
                temp = GuaranteedReservations;
                if (Deadline < GuaranteedReservations.Deadline) {
                    guaranteedReservations = this;
                }
                else {
                    do {
                        temp = temp.Next;
                    } while (temp != guaranteedReservations &&
                             Deadline > temp.Deadline); // > is compulsory to keep it 'FIFO'
                } // insert before 'temp'
                Next = temp;
                Previous = temp.Previous;
                temp.Previous.Next = this;
                temp.Previous = this;
            }
            RobinScheduler.AddRefReservation(this);
#if DEBUG_RESERV
            PrintQueueReserv(guaranteedReservations, timeNow); //TODO: PrintQueueReserv
#endif
            return;
        }


        static void ReplaceOnEarliestDeadlineFirst(OneShotReservation reservation,
                                                   DateTime timeNow)
        {
            while (reservation != null) {
                if (reservation.Estimate.Ticks > 0) {
                    if (reservation.MyQueueType == QueueType.NoQueue) {
                        reservation.EnqueueReservation(timeNow);
                    }
                    else {
                        Debug.Assert(reservation.MyQueueType == QueueType.GuaranteedQueue);
                    }
                    break;
                }
                if (reservation.MyQueueType == QueueType.NoQueue) {
                    reservation.EnqueueReservation(timeNow);
                }
                else {
                    Debug.Assert(reservation.MyQueueType == QueueType.UnfinishedQueue);
                }
                reservation = reservation.SurroundingReservation;
            }
        }

        public static bool SatisfyAcceptedConstraint(DateTime timeNow)
        {
            OneShotReservation reservation;

            if (currentReservation != null) {
                currentReservation.Estimate -= timeNow - RobinScheduler.SchedulingTime;
                if (currentReservation.Estimate.Ticks <= 0) {
                    RobinThread tempThread  = currentReservation.OriginalThread;
                    currentReservation.DequeueReservation();
                    if (tempThread != null) {
                        // activity reservations vanish here
                        //Debug.Assert(currentReservation.ActiveThread == RobinScheduler.GetCurrentThread()); // only thread reserv are added to
                        currentReservation.EnqueueReservation(RobinScheduler.SchedulingTime); // the UnfinishedConstraints list of the OriginalThread's Activ
                        OneShotReservation.ReplaceOnEarliestDeadlineFirst(currentReservation.SurroundingReservation, RobinScheduler.SchedulingTime);
                    }
                }
            }

            reservation = guaranteedReservations;
            while (reservation != null) {
                reservation = reservation.Next;
                if (reservation == guaranteedReservations) {
                    break;
                }
            }
            reservation = idleReservations;
            while (reservation != null) {
                reservation = reservation.Next;
                if (reservation == idleReservations) {
                    break;
                }
            }

            return true;
        }

        public static void UpdateCurrentReservation()
        {
            if ((currentReservation != null) &&
                (currentReservation.Estimate <= RobinScheduler.AFewSlice) ) {
                // slice used for a reservation (CK: & estimate now empty )
                RobinThread tempThread  = currentReservation.OriginalThread;
                currentReservation.DequeueReservation();
                if (tempThread != null) {
                    // activity reservations vanish here
                    currentReservation.EnqueueReservation(RobinScheduler.SchedulingTime); // the UnfinishedConstraints list of the OriginalThread's Activ
                    ReplaceOnEarliestDeadlineFirst(currentReservation.SurroundingReservation, RobinScheduler.SchedulingTime);
                }
            }

        }

        public static void FreshenReservationQueues()
        {
            OneShotReservation tempReservation;

            //  Move 'idle' reservations to the 'active' reservations list, if necessary.
            //  A reservation is 'idle' until its Start time is reached.
            //  The 'active' reservation list is ordered 'Earliest Deadline Firts'.
            //  The 'idle' queue is ordered Earliest Start First.
            while ((tempReservation = IdleReservations) != null) {
                if (RobinScheduler.SchedulingTime + RobinScheduler.AFewSlice < tempReservation.Start) {
                    break;
                }
                tempReservation.DequeueReservation(); // Remove OneShotReservation from the Idle Queue
                tempReservation.EnqueueReservation(RobinScheduler.SchedulingTime); // Earliest Deadline First in the (Non)Guaranteed Queue
                RobinScheduler.EnqueueRunThread(tempReservation.OriginalThread, true);
#if DEBUG_START_RESERV
                DebugStub.WriteLine("Get reservation {0}:{1} from IdleQueue", tempReservation.OriginalThread,
                    tempReservation.ReservationId);
#endif

            }

            // Remove the active reservations from the guaranteed and nonguaranteed queues
            // if their Deadline is <= CurrentTime.
            while ((tempReservation = guaranteedReservations) != null) {
                if (RobinScheduler.SchedulingTime + RobinScheduler.AFewSlice < tempReservation.Deadline) {
                    break;
                }

                //      fprintf(stdout,"Add: Reschedule Guaranteed Deadline 0x%x %d\n", tempReservation, tempReservation.ReferenceCount);
                RobinScheduler.AddRefReservation(tempReservation);
                tempReservation.DequeueReservation(); // Remove OneShotReservation from the Guaranteed Queue
                if ((tempReservation.OriginalThread != null) && // if a thread reservation
                    (tempReservation.OriginalThread.AssociatedActivity != null)) {
                    // If a thread reservation.
                    tempReservation.Estimate = new TimeSpan(0);
                    tempReservation.EnqueueReservation(RobinScheduler.SchedulingTime); // add it to the Pending End Constraint List
                }
                //  fprintf(stdout,"Reschedule Guaranteed Deadline off 0x%x %d\n", tempReservation, tempReservation.ReferenceCount);
                tempReservation.ReleaseReservationProtected();
            }

        }

        int ReleaseReservationProtected()
        {
            return RobinScheduler.ReleaseReservationProtected(this);
        }

        public void DequeueReservation() //TODO: Should this be a resource container function?
        {
            Debug.Assert(Processor.InterruptsDisabled());
            OneShotReservation next;

            Debug.Assert((AssociatedActivity != null) ||
                (OriginalThread != null));

            if (Next != this) {
                //If there's another reservation, remove us from the queue
                Next.Previous = Previous;
                Previous.Next = Next;
                next = Next;
                Debug.Assert(next.Deadline.Ticks > 0);
            }
            else {
                //Otherwise we are only reservation.
                Debug.Assert(Previous == this);
                next = null;
            }

            if (this == GuaranteedReservations) {
                //Take care when we're the head of the list.
                guaranteedReservations = next;
#if DEBUG_RESERV
                DebugStub.WriteLine("Dequeue {0}: {1}", ReservationId, Estimate);
                //      Debug.Assert(Criticality != CRITICAL || !Valid || Estimate ==0);
                PrintQueueReserv(GuaranteedReservations, SystemClock.GetKernelTime());
#endif
            }
            else if (this == IdleReservations) {
                //Or maybe we're the head of this list (but definitely not both).
                idleReservations = next;
            }
            else if ((OriginalThread != null) &&
                (this == OriginalThread.AssociatedActivity.UnfinishedConstraints) ) {
                //Or else we might be the head of the unfinished constraints list for the activity associated with our original thread.
                OriginalThread.AssociatedActivity.UnfinishedConstraints = next;
            } // else { we're OK! }

            Next = Previous = null;
            //  DebugStub.WriteLine("DequeueReservation 0x{0:x} {1}", this, ReferenceCount);
            MyQueueType = QueueType.NoQueue;
            ReleaseReservationProtected();
        }

        public static void FindRunnableReservation(ref RobinThread currentThread)
        {
            currentReservation = guaranteedReservations;
            while (currentReservation != null) {
                if ((currentThread = currentReservation.GetRunnableThread()) != null) {
                    break;
                }
                currentReservation = currentReservation.Next;
                if (currentReservation == guaranteedReservations) {
                    break;
                }
            }
        }

        static void InheritAndReplaceOnEarliestDeadlineFirst(OneShotReservation reservation,
                                                             TimeSpan timeInherited,
                                                             DateTime timeNow)
        {
            OneShotReservation current = null;

            while (reservation != null) {
                if (reservation.Estimate.Ticks > 0) {
                    if (current == null) {
                        Debug.Assert(reservation.MyQueueType != QueueType.NoQueue);
                        current = reservation;
                    }
                    else {
                        Debug.Assert(reservation.MyQueueType == QueueType.NoQueue);
                    }
                }
                else if (reservation.MyQueueType != QueueType.NoQueue) {
                    Debug.Assert (current != null || reservation.MyQueueType == QueueType.UnfinishedQueue);
                    reservation.DequeueReservation();
                }
                Debug.Assert(timeInherited >= reservation.InheritedEstimate);
                timeInherited -= reservation.InheritedEstimate;
                Debug.Assert(reservation.Estimate >= reservation.InheritedEstimate);
                reservation.Estimate -= reservation.InheritedEstimate;
                reservation = reservation.SurroundingReservation;
            }
            if (current != null) {
                if (current == CurrentReservation) {
                    current.Estimate -= timeNow - RobinScheduler.SchedulingTime;
                    currentReservation = null;
                    RobinScheduler.Reschedule();
                }
                current.DequeueReservation();
            }
        }

        public static void InheritOnEarliestDeadlineFirst(OneShotReservation reservation,
                                                          TimeSpan timeToInherit)
        {
            while (reservation != null && timeToInherit.Ticks > 0) {
                Debug.Assert(timeToInherit >= reservation.InheritedEstimate);
                reservation.Estimate -= reservation.InheritedEstimate;
                timeToInherit -= reservation.InheritedEstimate;
                reservation = reservation.SurroundingReservation;
            }
        }

        public static void ClearCurrentReservation()
        {
            currentReservation = null;
        }
    }
}
