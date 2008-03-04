////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\IResourceReservation.cs
//
//  Note:
//

using System;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// A resource-independent ongoing resource reservation object.
    /// Resource reservation objects for actual resources are derived from this base class.
    /// </summary>
    public abstract class IResourceReservation
    {
        /// <summary>
        /// Get the resource object that this IResourceReservation object quantifies
        /// </summary>
        public abstract IResource Resource
        {
            get;
        }
    }
}
