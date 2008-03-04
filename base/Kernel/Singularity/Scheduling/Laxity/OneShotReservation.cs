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

namespace Microsoft.Singularity.Scheduling.Laxity
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
        // override the global LaxityQueueType enum with our own
        public enum LaxityQueueType
        {
            NoQueue = 5,
            GuaranteedQueue = 1,
            // NonGuaranteedQueue = 2,
            IdleQueue = 3,
            // UnfinishedQueue = 4
        };

        // heap keyed on laxity holding all guaranteed one-shot reservations
        // that currently have at least one runnable thread.
        private static Heap  guaranteedReservations = new Heap();
        public static OneShotReservation TopGuaranteedReservation
        {
            get { return (OneShotReservation)guaranteedReservations.Min; }
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
            get
            {
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
        public LaxityThread             ReservTask;

        TimeSpan     constraintExecution;
        public TimeSpan ConstraintExecution
        {
            get { return constraintExecution; }
        }

        TimeSpan     initialThreadExecution;

        public DateTime Start, Deadline;
        public TimeSpan Estimate, RelativeDeadline;

        public DateTime Laxity
        {
            // dnarayan
            // 1. This is actually (laxity + timeNow). It has the same effect as laxity
            // for comparing across tasks at any given instant, and the additional
            // advantage that it doesn't change over time, only when resources are consumed.
            // Another invariant is that with this definition, "laxity" of an individual task
            // never increases.

            // 2. The assumption is that Estimate always holds (in time units) the total
            // remaining amount of resource (i.e. reserved - used). Currently this is just
            // the CPU-remaining estimate

            // 3. If we use more than our allocation, our laxity starts increasing, i.e.
            // we get punished. This is a feature. However, it's still possible that we
            // have overused one resource but have low laxity due to being behind on another.
            // We might want to punish tasks for that as well?

            get { return Deadline - Estimate; }
        }

        public TimeSpan     InitialEstimate; // test only
        public LaxityQueueType  MyLaxityQueueType;
        public bool         Guaranteed;
        public bool         Valid;
        public bool         Satisfied;

        // Reservations can be made for Threads XOR Activities!
        public LaxityThread         OriginalThread; // always the constraint thread -- non-null until the reservation is complete
        //public LaxityThread           ActiveThread;   // the above, or the thread blocking it
        //public int                ActiveThreadEpoch;
        public LaxityActivity      AssociatedActivity;        // the two fields above must be null!


        public OneShotReservation      Next;
        public OneShotReservation      Previous;

        public TimeSpan     InheritedEstimate;  // Stolen => Inherited, no longer critical
        public int          ResolutionEpoch;    // Stolen => Resolution, since not stealing.

        public int              ReferenceCount;

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
            MyLaxityQueueType = LaxityQueueType.NoQueue;
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
        public LaxityThread GetRunnableThread()
        {
//            LaxityThread thread;
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
            if (temp == null || ((LaxityThread)temp.SchedulerThread).ActiveProcessor != null) {
                return null;
            }
            else {
                return (LaxityThread)temp.SchedulerThread;
            }
        }

        public void StopThreadExecution(LaxityThread thread)
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

        public void StartThreadExecution(LaxityThread thread)
        {
            initialThreadExecution = thread.ExecutionTime;
        }

        public void StartThreadExecution(LaxityThread thread, TimeSpan delta)
        {
            initialThreadExecution = thread.ExecutionTime + delta;
        }

        public static void BeginConstraintBeforeWait(LaxityThread thread, bool endPrevious,
            TimeConstraint timeConstraint, DateTime timeNow)
        {
            OneShotReservation reservation;

            Debug.Assert(thread == LaxityScheduler.GetCurrentThread());
            Debug.Assert(!Processor.InterruptsDisabled());
            if (endPrevious) {
                bool iflag = Processor.DisableInterrupts();
                EndPreviousConstraint(thread, timeNow);
                Processor.RestoreInterrupts(iflag);
            }

            LaxityScheduler.UpdateSchedulingStatus();

            reservation = thread.PendingReservation;

            reservation.StartThreadExecution(thread, (timeNow - LaxityScheduler.SchedulingTime));
            reservation.constraintExecution = new TimeSpan(0);
            reservation.Start = timeConstraint.Start;
            reservation.Deadline = timeConstraint.Deadline;
            reservation.RelativeDeadline = timeConstraint.RelativeDeadline;
            reservation.Estimate = timeConstraint.Estimate;
            reservation.Valid = reservation.Guaranteed = true;
            reservation.MyLaxityQueueType = LaxityQueueType.NoQueue;
            reservation.ReservTask = thread;
            reservation.OriginalThread = /*reservation.ActiveThread =*/ thread;
            thread.AddRef();
            //thread.AddRef();
            //reservation.ActiveThreadEpoch = thread.Epoch;
            reservation.AssociatedActivity = null;
            reservation.Next = reservation.Previous = null;

        }

        // DI -- this is a new function used in both Begin and EndConstraint
        public static bool EndPreviousConstraint(LaxityThread thread, DateTime timeNow)
        {
            OneShotReservation reservation, reservationNext;
            bool            success;
            LaxityQueueType     tempQueue; //Value assigned but never used.

            Debug.Assert(Processor.InterruptsDisabled());

            reservation = thread.ReservationStack;

            success = (timeNow <= reservation.Deadline);
            LaxityScheduler.UpdateSchedulingStatus();
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
                LaxityScheduler.Reschedule();
            }
            Debug.Assert(reservation.OriginalThread == thread);
            Debug.Assert(reservation.Start <= timeNow);

            if (thread.ReservationStack == null) {
                if (reservation.Estimate >= LaxityScheduler.MinSlice) {
                    // the constraint used less than estimated:
                    Debug.Assert(reservation.MyLaxityQueueType == LaxityQueueType.GuaranteedQueue);
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
                    LaxityScheduler.ActivityObjAddRef(reservation.AssociatedActivity);
                    // leave it on whatever Q it happens to be on
                }
                else {
                    reservation.DequeueReservation(); // from whatever Q it happens to be on
                }
            }
            else {
                tempQueue = reservation.MyLaxityQueueType;
                reservationNext = thread.ReservationStack;
                reservationNext.Estimate += reservation.Estimate;
                reservationNext.constraintExecution += reservation.constraintExecution;
                reservationNext.StartThreadExecution(thread);
//                Debug.Assert( ((reservation.MyLaxityQueueType == LaxityQueueType.GuaranteedQueue) &&
//                    (reservationNext.MyLaxityQueueType == LaxityQueueType.NoQueue)) ||
//                    ((reservation.MyLaxityQueueType == LaxityQueueType.UnfinishedQueue) &&
//                    ((reservationNext.Estimate.Ticks <= 0) || (reservationNext.MyLaxityQueueType != LaxityQueueType.UnfinishedQueue ))));
                reservation.Estimate = new TimeSpan(0);
                Debug.Assert(reservation.Next != null);
                reservation.DequeueReservation();
                ReplaceOnLaxityHeap(reservationNext, timeNow);

            }
            // fprintf(stdout,"EndPrevConstr 0x%x %d\n", reservation, reservation.ReferenceCount);
            reservation.ReleaseReservationProtected();

            Scheduler.LogEndConstraint();

            if (success == false) {
                Scheduler.LogSchedulerLate();
            }
            return success;
        }


        public static bool ResolveConstraint(LaxityThread thread)
        {
            TimeSpan timeLeft, ownNodesTimeToSteal = new TimeSpan(1);
            DateTime start, deadline;
            TimeSpan timeInherited;
            OneShotReservation pendingReservation = thread.PendingReservation, reservationPrevious;
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
                    // no body.
                }

                if (reservation!= null) {
                    deadline = LaxityScheduler.minTime(deadline, reservation.Deadline);
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
            LaxityScheduler.AddRefReservation(pendingReservation);
            if (reservationPrevious != null) {
                reservationPrevious.constraintExecution += pendingReservation.initialThreadExecution -
                    reservationPrevious.initialThreadExecution;
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

            // dnarayan: for simplicity, we schedule all tasks using global
            // least-laxity, nothing out of individual resource containers.
#if false
            // Infeasible or (Estimate == 0) Reservations:
            if (Estimate <= LaxityScheduler.AFewSlice) {
                LaxityActivity activity;
                Debug.Assert(OriginalThread != null);
                Debug.Assert(AssociatedActivity == null);
                activity = OriginalThread.AssociatedActivity;
                MyLaxityQueueType = LaxityQueueType.UnfinishedQueue;
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
                LaxityScheduler.AddRefReservation(this);
                return;
            }
            //  Debug.Assert(Valid);
#endif
            // Idle Reservations:
            if (Start > timeNow + LaxityScheduler.AFewSlice) {
                // take thread off activity Laxity queue and simulate a sleep
                //    i.e. don't set a timer interrupt

                OriginalThread.InternalSetStateWaiting(DateTime.MaxValue);

#if DEBUG_START_RESERV
                DebugStub.WriteLine("Put reservation {0}:{1} in IdleQueue\n", OriginalThread,
                    ReservationId);
#endif
                Debug.Assert(OriginalThread != null);
                MyLaxityQueueType = LaxityQueueType.IdleQueue;

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
                LaxityScheduler.AddRefReservation(this);
                return;
            }

            // Guaranteed OneShotReservation:
            MyLaxityQueueType = LaxityQueueType.GuaranteedQueue;
            if (GetRunnableThread() != null) {
                Debug.Assert(guaranteedReservations.Insert(this, Laxity));
            }

            LaxityScheduler.AddRefReservation(this);
#if DEBUG_RESERV
            PrintQueueReserv(guaranteedReservations, timeNow); //TODO: PrintQueueReserv
#endif
            return;
        }

        // XXX dnarayan this is a replacement for ReplaceOnEarliestDeadlineFirst, but I
        // don't really understand what it's supposed to do, so for now it does nothing (!)

        static void ReplaceOnLaxityHeap(OneShotReservation reservation, DateTime timeNow)
        {
#if false
            while (reservation != null) {
                if (reservation.Estimate.Ticks > 0) {
                    if (reservation.MyLaxityQueueType == LaxityQueueType.NoQueue) {
                        reservation.EnqueueReservation(timeNow);
                    }
                    else {
                        Debug.Assert(reservation.MyLaxityQueueType == LaxityQueueType.GuaranteedQueue);
                    }
                    break;
                }
                Debug.Assert(reservation.MyLaxityQueueType == LaxityQueueType.NoQueue);
                reservation.EnqueueReservation(timeNow);

                reservation = reservation.SurroundingReservation;
            }
#endif
        }

#if false
        public static bool SatisfyAcceptedConstraint(DateTime timeNow)
        {
            OneShotReservation    reservation;

            if (currentReservation != null) {
                currentReservation.Estimate -= timeNow - LaxityScheduler.SchedulingTime;
                if (currentReservation.Estimate.Ticks <= 0) {
                    LaxityThread tempThread  = currentReservation.OriginalThread;
                    currentReservation.DequeueReservation();
                    if (tempThread != null) {
                        // activity reservations vanish here
                        //Debug.Assert(currentReservation.ActiveThread == LaxityScheduler.GetCurrentThread()); // only thread reserv are added to
                        currentReservation.EnqueueReservation(LaxityScheduler.SchedulingTime); // the UnfinishedConstraints list of the OriginalThread's Activ
                        OneShotReservation.ReplaceOnEarliestDeadlineFirst(currentReservation.SurroundingReservation, LaxityScheduler.SchedulingTime);
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
#endif

        public static void UpdateCurrentReservation()
        {
            if ((currentReservation != null) &&
                (currentReservation.Estimate <= LaxityScheduler.AFewSlice) ) {
                // slice used for a reservation (CK: & estimate now empty )
                LaxityThread tempThread  = currentReservation.OriginalThread;
                currentReservation.DequeueReservation();
                if (tempThread != null) {
                    // activity reservations vanish here
                    currentReservation.EnqueueReservation(LaxityScheduler.SchedulingTime); // the UnfinishedConstraints list of the OriginalThread's Activ
                    ReplaceOnLaxityHeap(currentReservation.SurroundingReservation, LaxityScheduler.SchedulingTime);
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
                if (LaxityScheduler.SchedulingTime + LaxityScheduler.AFewSlice < tempReservation.Start) {
                    break;
                }
                tempReservation.DequeueReservation(); // Remove OneShotReservation from the Idle Queue
                tempReservation.EnqueueReservation(LaxityScheduler.SchedulingTime); // Earliest Deadline First in the (Non)Guaranteed Queue
                LaxityScheduler.EnqueueRunThread(tempReservation.OriginalThread, true);
#if DEBUG_START_RESERV
                DebugStub.WriteLine("Get reservation {0}:{1} from IdleQueue", tempReservation.OriginalThread,
                    tempReservation.ReservationId);
#endif

            }

            // dnarayan: we don't remove deadline-passed tasks from the guaranteed queue,
            // however they should get penalized by having increasing laxity. We might want
            // to reconsider this decision.
#if false
            // Remove the active reservations from the guaranteed and nonguaranteed queues
            // if their Deadline is <= CurrentTime.
            while ((tempReservation = guaranteedReservations) != null) {
                if (LaxityScheduler.SchedulingTime + LaxityScheduler.AFewSlice < tempReservation.Deadline) {
                    break;
                }
                //      fprintf(stdout,"Add: Reschedule Guaranteed Deadline 0x%x %d\n", tempReservation, tempReservation.ReferenceCount);
                LaxityScheduler.AddRefReservation(tempReservation);
                tempReservation.DequeueReservation(); // Remove OneShotReservation from the Guaranteed Queue
                if ((tempReservation.OriginalThread != null) && // if a thread reservation
                    (tempReservation.OriginalThread.AssociatedActivity != null)) {
                    // If a thread reservation.
                    tempReservation.Estimate = new TimeSpan(0);
                    tempReservation.EnqueueReservation(LaxityScheduler.SchedulingTime); // add it to the Pending End Constraint List
                }
                //  fprintf(stdout,"Reschedule Guaranteed Deadline off 0x%x %d\n", tempReservation, tempReservation.ReferenceCount);
                tempReservation.ReleaseReservationProtected();
            }
#endif
        }

        int ReleaseReservationProtected()
        {
            return LaxityScheduler.ReleaseReservationProtected(this);
        }

        public void DequeueReservation() //TODO: Should this be a resource container function?
        {
            Debug.Assert(Processor.InterruptsDisabled());
            Debug.Assert((AssociatedActivity != null) ||
                (OriginalThread != null));

            if (MyLaxityQueueType == LaxityQueueType.GuaranteedQueue) {
                Debug.Assert(guaranteedReservations.Delete(this));
            }
            else {
                Debug.Assert(MyLaxityQueueType == LaxityQueueType.IdleQueue);

                if (Next != this) {
                    //If there's another reservation, remove us from the queue
                    Next.Previous = Previous;
                    Previous.Next = Next;
                    Debug.Assert(Next.Deadline.Ticks > 0);
                }
                else {
                    //Otherwise we are only reservation.
                    Debug.Assert(Previous == this);
                    Next = null;
                }

                if (this == IdleReservations) {
                    // if we were head of the queue
                    idleReservations = Next;
                }

                Next = Previous = null;
                //  DebugStub.WriteLine("DequeueReservation 0x{0:x} {1}", this, ReferenceCount);
                MyLaxityQueueType = LaxityQueueType.NoQueue;
                ReleaseReservationProtected();
            }
        }

        public static void FindRunnableReservation(ref LaxityThread currentThread)
        {
            // dnarayan: this is a bit hokey. What we really want is to maintain the
            // invariant that the heap only contains tasks with runnable threads. However
            // this involves hooking all the places where threads get added/removed as
            // task beneficiaries.

            ArrayList nonRunnableTasks = new ArrayList();
            currentThread = null;
            while (((currentReservation = (OneShotReservation) guaranteedReservations.Min)
                    != null) && (currentThread == null)) {
                currentThread = currentReservation.GetRunnableThread();
                if (currentThread == null) {
                    Debug.Assert(guaranteedReservations.Delete(currentReservation));
                    nonRunnableTasks.Add(currentReservation);
                }
            }

            // Now put all the non-runnable tasks back in the heap (!)
            foreach (OneShotReservation resv in nonRunnableTasks) {
                Debug.Assert(guaranteedReservations.Insert(resv, resv.Laxity));
            }
        }

        // dnarayan: never called
#if false
        public static void InheritAndReplaceOnEarliestDeadlineFirst(OneShotReservation reservation,
                                                                    TimeSpan timeInherited,
                                                                    DateTime timeNow)
        {
            OneShotReservation current = null;

            while (reservation != null) {
                if (reservation.Estimate.Ticks > 0) {
                    if (current == null) {
                        Debug.Assert(reservation.MyLaxityQueueType != LaxityQueueType.NoQueue);
                        current = reservation;
                    }
                    else {
                        Debug.Assert(reservation.MyLaxityQueueType == LaxityQueueType.NoQueue);
                    }
                }
                else if (reservation.MyLaxityQueueType != LaxityQueueType.NoQueue) {
                    Debug.Assert (current != null || reservation.MyLaxityQueueType == LaxityQueueType.UnfinishedQueue);
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
                    current.Estimate -= timeNow - LaxityScheduler.SchedulingTime;
                    currentReservation = null;
                    LaxityScheduler.Reschedule();
                }
                current.DequeueReservation();
            }
        }
#endif
        public static void InheritOnEarliestDeadlineFirst(OneShotReservation reservation, TimeSpan timeToInherit)
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
