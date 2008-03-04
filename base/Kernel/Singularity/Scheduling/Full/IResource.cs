////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\IResource.cs
//
//  Note:
//

using System;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// A resource-independent Resource object.  Actual resources are derived from this base class.
    /// </summary>
    public abstract class IResource
    {
        /// <summary>
        /// Activity contains a hashtable of resource reservations.  A
        /// reservation for a given resource object will be stored and retrieved
        /// based on the ResourceString returned via that resource.  This
        /// flexibility will allow more than just on type, since some resource
        /// types may have distinct providers for different resources (e.g.
        /// multiple hard disks, network cards, etc), whereas multiple CPU's will
        /// likely only have one provider.
        /// </summary>
        public abstract string ResourceString { get; }
    }
}
