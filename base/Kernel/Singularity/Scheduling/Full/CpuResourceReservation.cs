////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\Scheduling\CpuResourceReservation.cs
//
//  Note:
//

using System;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// An ongoing CPU resource reservation object.
    /// </summary>
    public class CpuResourceReservation : IResourceReservation
    {
        public ISchedulerCpuReservation schedulerReservation;

        /// <summary>
        /// Return a reference to the CpuResource provider
        /// </summary>
        public override IResource Resource
        {
            get { return CpuResource.Provider(); }
        }

        private CpuResourceAmount requestedAmount;
        private TimeSpan requestedPeriod;

        public CpuResourceReservation(CpuResourceAmount requestedAmount,
                                      TimeSpan requestedPeriod,
                                      ISchedulerCpuReservation schedulerReservation)
        {
            this.requestedAmount = requestedAmount;
            this.requestedPeriod = requestedPeriod;
            this.schedulerReservation = schedulerReservation;
        }

        /// <summary>
        /// The amount requested for an ongoing CPU reservation
        /// </summary>
        public CpuResourceAmount RequestedAmount
        {
            get { return requestedAmount; }
        }

        /// <summary>
        /// The period requested for an ongoing CPU reservation
        /// </summary>
        public TimeSpan RequestedPeriod
        {
            get { return requestedPeriod; }
        }

        /// <summary>
        /// The amount actually granted for an ongoing CPU reservation
        /// </summary>
        public CpuResourceAmount ActualAmount
        {
            get {
                return (schedulerReservation == null)
                    ? null : schedulerReservation.ReservedAmount;
                // XXX TBD May change as global schedule changes
            }
        }

        /// <summary>
        /// The period actually granted for an ongoing CPU reservation
        /// </summary>
        public TimeSpan ActualPeriod
        {
            get {
                return (schedulerReservation == null)
                    ? new TimeSpan(0) : schedulerReservation.ReservedPeriod;
                // XXX TBD May change as global schedule changes
            }
        }
    }
}
