////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\Scheduling\ICpuScheduler.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Threading;
#if SIMULATOR
using Thread = Microsoft.Singularity.Scheduling.Thread;
#endif

namespace Microsoft.Singularity.Scheduling
{
    [CLSCompliant(false)]
    public abstract class ICpuScheduler
    {
        public abstract ISchedulerThread CreateSchedulerThread(Thread thread);
        public abstract ISchedulerProcessor CreateSchedulerProcessor(Processor processor);
        public abstract ISchedulerActivity CreateSchedulerActivity();
        public abstract ISchedulerCpuReservation ReserveCpu(ISchedulerActivity activity,
                                                            CpuResourceAmount amount,
                                                            TimeSpan period);
        public abstract bool NextThread(out Thread nextThread);
        public abstract bool ShouldReschedule();

        /// <summary>
        /// Note that as tasks now contain resource accounting, there is
        /// no need to pass out the resource usage from the scheduler.
        /// For consistency we'll still pass it out of the Task API at least
        /// until instantaneous usage is fixed.
        /// </summary>
        /// <returns>Returns true if admitted.</returns>
        public abstract bool BeginConstraint(Hashtable resourceEstimates,
                                             DateTime deadline,
                                             ISchedulerTask taskToEnd,
                                             out ISchedulerTask schedulerTask);
        public abstract void BeginDelayedConstraint(Hashtable resourceEstimates,
                                                    TimeSpan relativeDeadline,
                                                    ISchedulerTask taskToEnd,
                                                    out ISchedulerTask schedulerTask);
        public abstract bool EndConstraint(ISchedulerTask taskToEnd);
        public abstract void Initialize();
    }

}
