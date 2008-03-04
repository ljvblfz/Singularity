////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\IResourceAmount.cs
//
//  Note:
//

using System;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// The base class from which resource-specific resource amount classes are derived.
    /// </summary>
    public abstract class IResourceAmount
    {
        /// <summary>
        /// Get the resource object that this IResourceAmount object quantifies
        /// </summary>
        public abstract IResource Resource
        {
            get;
        }

        public abstract IResourceAmount AddTo(IResourceAmount amount);
    }
}
