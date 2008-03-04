////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\ISchedulerActivity.cs
//
//  Note:
//

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// Summary description for ISchedulerActivity.
    /// </summary>
    public abstract class ISchedulerActivity
    {
        public abstract Activity EnclosingActivity { set; get; }
        //CpuResourceReservation CpuResourceReservation { get; }
        /// <summary>
        /// ReleaseReference is used by the thread creating the
        /// resource container to release its reference to it
        /// voluntarily so that the resource container knows the
        /// only things added to it can occur as a result of
        /// threads currently assigned to the resource container.
        /// </summary>
        public abstract void ReleaseReference();
    }
}
