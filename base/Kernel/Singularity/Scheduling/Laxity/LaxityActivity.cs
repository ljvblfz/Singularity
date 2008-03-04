////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   LaxityActivity.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Laxity
{
    /// <summary>
    /// The resource container class includes a recurring reservation
    /// (k out of every n), a list of runnable threads, unfinished constraints
    /// (i.e. non-guaranteed or non-feasible), as well as pointers to handle
    /// its role in lists for the non-blocked activity list.
    /// It also has references for the last node to execute it,
    /// a node assigned to it (which is a list via SameActivityNext), and a node
    /// assigned to it under the new scheduling plan.  It also has some bookkeeping.
    /// </summary>
    public class LaxityActivity : ISchedulerActivity
    {
        Activity enclosingActivity;

        public RecurringReservation MyRecurringCpuReservation;

        public LaxityThread     RunnableThreads;// list of runnable threads, if any

#if false
        public OneShotReservation  UnfinishedConstraints;
#endif

        public LaxityActivity    Next;     // Next and previous activities
        public LaxityActivity  Previous;

        public int          ReferenceCount;
        public int        CountNodes;

        public LaxityActivity()
        {
            //Below is InitializeActivity();
            ReferenceCount = 1;
            RunnableThreads = null;

//            MyRecurringCpuReservation = new RecurringReservation();
//            MyRecurringCpuReservation.EnclosingCpuReservation = new CpuResourceReservation(MyRecurringCpuReservation);

            bool iflag = Processor.DisableInterrupts();
            LaxityScheduler.EnqueueActivity(this); // add in last position in Laxity!
            Processor.RestoreInterrupts(iflag);
        }

        public void ReleaseActivity()
        {
            bool iflag = Processor.DisableInterrupts();
            LaxityScheduler.DequeueActivity(this); // update Laxity queue, if required.
            Processor.RestoreInterrupts(iflag);
            Scheduler.LogDeleteActivity();
        }

        //public-normal API
        //TODO: Call this from finalize?
        public override void ReleaseReference()
        {
            // DI replaced by AtomicDec(&activity->ReferenceCount);
            Debug.Assert(ReferenceCount > 0);
            int newrefcnt = Interlocked.Decrement(ref ReferenceCount);

            if (newrefcnt == CountNodes) {
                if (CountNodes > 0) {
                    return;
                }
                bool iflag = Processor.DisableInterrupts();
                LaxityScheduler.DequeueActivity(this); // update Laxity queue, if required.
                Processor.RestoreInterrupts(iflag);
            }
            Scheduler.LogDeleteActivity();
        }

        // find the runnable thread of an activity:
        public LaxityThread GetRunnableThread()
        {
            // dnarayan: we don't schedule tasks from resource containers anymore,
            // but directly from the global least-laxity heap
#if false
            OneShotReservation reservation = UnfinishedConstraints;
            Debug.Assert(reservation == null || reservation.AssociatedActivity == null);
            // avoid stack overflow : GetRunnableThread & GetRunnableThread
            while (reservation != null) {
                // search in the circular list
                LaxityThread thread = reservation.GetRunnableThread();
                if (thread != null) {
                    return thread;
                }
                reservation = reservation.Next;
                if (reservation == UnfinishedConstraints) {
                    break;
                }
            }
#endif
            LaxityThread runThread = RunnableThreads;
            while (runThread != null && runThread.ActiveProcessor != null && runThread != RunnableThreads.Previous) {
                runThread = runThread.Next;
            }
            if (runThread == null || runThread.ActiveProcessor != null) {
                return null;
            }
            else {
                return runThread;
            }
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
