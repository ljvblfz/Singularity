////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   RobinProcessor.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Robin
{
    /// <summary>
    /// </summary>
    public class RobinProcessor : ISchedulerProcessor
    {
        private readonly Processor enclosingProcessor;
        public override Processor EnclosingProcessor
        {
            get { return enclosingProcessor; }
        }

        internal RobinActivity CurrentActivity;
        internal TimeSpan SliceLeft;
        internal RobinThread RunningThread;
        internal DateTime SchedulingTime;
        internal bool NeedToReschedule;
        internal bool Idle;

        public void Reschedule()
        {
            NeedToReschedule = true;
        }

        public RobinProcessor(Processor processor)
        {
            enclosingProcessor = processor;
            SchedulingTime = SchedulerClock.BootTime;
        }
    }
}
