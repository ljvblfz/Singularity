////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\ISchedulerThread.cs
//
//  Note:
//

using System;
using System.Threading;
#if SIMULATOR
using Thread = Microsoft.Singularity.Scheduling.Thread;
#endif

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// Summary description for ISchedulerThread.
    /// </summary>
    [CLSCompliant(false)]
    public abstract class ISchedulerThread
    {
        public abstract Thread EnclosingThread { get; }

        public abstract void Start();
        public abstract void SetActivity(ISchedulerActivity activity);
        public abstract void Cleanup();
        public abstract TimeSpan ExecutionTime { get; }
        /// <summary>
        /// Tells the scheduler that this thread should wait until awoken, or until the timeout.
        /// </summary>
        /// <param name="timeOut">A timeout of DateTime.MaxValue means no timeout.  Otherwise the time to wake up.</param>
        public abstract void SetStateWaiting(DateTime timeOut);
        public abstract void Wake();
        public abstract ISchedulerTask PrepareDelayedTask(ISchedulerTask taskToEnd,
                                                          ref TimeConstraint timeConstraint,
                                                          DateTime timeNow);
    }
}
