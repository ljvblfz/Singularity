////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   LaxityProcessor.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Laxity
{
    /// <summary>
    /// </summary>
    public class LaxityProcessor : ISchedulerProcessor
    {
        private readonly Processor enclosingProcessor;
        public override Processor EnclosingProcessor
        {
            get { return enclosingProcessor; }
        }

        internal LaxityActivity CurrentActivity;
        internal TimeSpan SliceLeft;
        internal LaxityThread RunningThread;
        internal DateTime SchedulingTime;
        internal bool NeedToReschedule;
        internal bool Idle;

        public void Reschedule()
        {
            NeedToReschedule = true;
        }

        public LaxityProcessor(Processor processor)
        {
            enclosingProcessor = processor;
            SchedulingTime = SchedulerClock.BootTime;
        }

    }

}
