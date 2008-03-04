////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\ISchedulerCpuReservation.cs
//
//  Note:
//

using System;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// Summary description for ISchedulerCpuReservation.
    /// </summary>
    public abstract class ISchedulerCpuReservation
    {
        public abstract CpuResourceReservation EnclosingCpuReservation
        {
            get; set;
        }

        public abstract CpuResourceAmount ReservedAmount
        {
            get;
        }

        public abstract TimeSpan ReservedPeriod
        {
            get;
        }

    }
}
