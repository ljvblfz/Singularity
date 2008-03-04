////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   RecurringReservation.cs
//
//  Note:
//

using System;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Rialto
{
    /// <summary>
    /// Summary description for RecurringReservation.
    /// </summary>
    public class RecurringReservation : ISchedulerCpuReservation
    {
        private CpuResourceReservation enclosingCpuReservation;

        internal TimeSpan   SliceLeft;
        // CPU time left from the last run
        internal GraphNode       AssignedNodes;
        // nodes assigned to the activity
        internal GraphNode       TempAssignedNodes;
        // same as above in the new scheduling plan


        internal RecurringReservation()
        {
            SliceLeft = new TimeSpan(0);
            AssignedNodes = null;
            TempAssignedNodes = null;
        }

        internal TimeSpan Slice;
        internal TimeSpan Period;

        public override CpuResourceAmount ReservedAmount
        {
            get { return CpuResource.Provider().TimeToCpu(Slice); }
        }

        public override TimeSpan ReservedPeriod
        {
            get { return Period; }
        }

#region ISchedulerCpuReservation Members
        public override CpuResourceReservation EnclosingCpuReservation
        {
            get { return enclosingCpuReservation; }
            set { enclosingCpuReservation = value; }
        }
#endregion
    }
}
