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

namespace Microsoft.Singularity.Scheduling.Laxity
{
    /// <summary>
    /// Summary description for RecurringReservation.
    /// </summary>
    public class RecurringReservation : ISchedulerCpuReservation
    {
        CpuResourceReservation enclosingCpuReservation;

        public RecurringReservation()
        {
        }

#region ISchedulerCpuReservation Members

        public override CpuResourceReservation EnclosingCpuReservation
        {
            get { return enclosingCpuReservation; }
            set { enclosingCpuReservation = value; }
        }

#endregion

        public TimeSpan Slice;
        public TimeSpan Period;

        public override CpuResourceAmount ReservedAmount
        {
            get { return CpuResource.Provider().TimeToCpu(Slice); }
        }

        public override TimeSpan ReservedPeriod
        {
            get { return Period; }
        }
    }
}
