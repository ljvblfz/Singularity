////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   RialtoActivity.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Rialto
{
    /// <summary>
    /// The resource container class includes a recurring reservation
    /// (k out of every n), a list of runnable threads, unfinished constraints
    /// (i.e. non-guaranteed or non-feasible), as well as pointers to handle
    /// its role in lists for the non-blocked activity list and Standby
    /// activity list.  It also has references for the last node to execute it,
    /// a node assigned to it (which is a list via SameActivityNext), and a node
    /// assigned to it under the new scheduling plan.  It also has some bookkeeping.
    /// </summary>
    public class RialtoActivity : ISchedulerActivity
    {
        private Activity enclosingActivity;

        /// <summary>
        /// When null, there is no active recurring reservation.
        /// </summary>
        internal RecurringReservation MyRecurringCpuReservation;

        public RialtoThread     RunnableThreads;
        // list of runnable threads, if any

        public GraphNode     LastNode;
        // last node used to execute the activity
        // non-null only for incomplete activities
        // List of Constraints: not accepted || with the estimate consumed
        public OneShotReservation  UnfinishedConstraints;

        public RialtoActivity      Next;     // Next and previous activities
        public RialtoActivity      Previous;

        // An activity is incomplete if it didn't use its previous CPU
        // slice completely. These activities are linked in a separate list.
        public RialtoActivity  NextStandbyActivity;
        // Next and previous 'incomplete' activities
        public RialtoActivity  PreviousStandbyActivity;
        // NOT a circular list!

        public int  ReferenceCount;
        public int  CountNodes;
        public int  Id;
        public static int nextRCId = 1;

        public RialtoActivity()
        {
            //Below is InitializeActivity();
            ReferenceCount = 1;
            RunnableThreads = null;

            LastNode = null;
            NextStandbyActivity = null;
            PreviousStandbyActivity = null;

            //Commenting out because a null reservation means no reservation.
            //MyRecurringCpuReservation = new RecurringReservation();
            //MyRecurringCpuReservation.EnclosingCpuReservation = new CpuResourceReservation(MyRecurringCpuReservation);

            bool iflag = Processor.DisableInterrupts();
            Id = Interlocked.Increment(ref nextRCId);
            RialtoScheduler.EnqueueActivity(this); // add in last position in RoundRobin!
            Processor.RestoreInterrupts(iflag);
        }

        //public-normal API
        //TODO: Call this from finalize?
        /// <summary>
        /// ReleaseReference is called when:
        /// - A thread with this as resource container exits
        /// - The thread creating the resource container releases it voluntarily
        /// - A node assigned to this resource container's CPU reservation is removed
        /// - When a constraint which became a resource container constraint exits
        ///
        /// When the only references left are from the nodes assigned, a CPU resource
        /// reservation for no time is requested.  When there are no additional
        /// nodes left, the resource container is dequeued.
        /// </summary>
        public override void ReleaseReference()
        {
            int newrefcnt;

            // DI replaced by AtomicDec(&activity->ReferenceCount);
            Debug.Assert(ReferenceCount > 0);
            newrefcnt = Interlocked.Decrement(ref ReferenceCount);

            if (newrefcnt == CountNodes) {
                if (CountNodes > 0) {
                    //ISchedulerCpuReservation reservation = ((CpuResourceReservation)EnclosingActivity.GetResourceReservation(CpuResource.Provider().ResourceString)).schedulerReservation;
                    Debug.Assert(MyRecurringCpuReservation != null);
                    if (MyRecurringCpuReservation.Slice.Ticks > 0) {
                        CpuResourceAmount amount = CpuResource.Provider().TimeToCpu(new TimeSpan(0));
                        TimeSpan period = MyRecurringCpuReservation.Period;
                        CpuResource.Provider().ReserveCpu(EnclosingActivity, amount, period); //release is a reservation for 0!
                        Scheduler.LogRecurringCpuReservation(); //TODO: SIM-huh?
                    }
                    //system.SetNextThreadContext(GetThread(0)); //TODO: What does nextThread mean here?
                    //TODO: Shouldn't we be just setting it to null and letting nextThread take care of it?
                    return;
                }
                bool iflag = Processor.DisableInterrupts();
                RialtoScheduler.DequeueActivity(this); // update RoundRobin queue, if required.
                Processor.RestoreInterrupts(iflag);
            }
            Scheduler.LogDeleteActivity();
        }

        // find the runnable thread of an activity:
        public RialtoThread GetRunnableThread()
        {
            OneShotReservation reservation = UnfinishedConstraints;
            Debug.Assert(reservation == null || reservation.AssociatedActivity == null);
            // avoid stack overflow : GetRunnableThread & GetRunnableThread

            while (reservation != null) {
                // search in the circular list
                RialtoThread thread = reservation.GetRunnableThread();
                if (thread != null) {
                    return thread;
                }
                reservation = reservation.Next;
                if (reservation == UnfinishedConstraints) {
                    break;
                }
            }
            return RunnableThreads;
        }

#region ISchedulerActivity Members
        public override Activity EnclosingActivity
        {
            get { return enclosingActivity; }
            set { enclosingActivity = value; }
        }

        public CpuResourceReservation CpuResourceReservation
        {
            get { return MyRecurringCpuReservation.EnclosingCpuReservation; }
        }
#endregion
    }

}
