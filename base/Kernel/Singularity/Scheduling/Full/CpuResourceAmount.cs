////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\Scheduling\CpuResourceAmount.cs
//
//  Note:
//

using System;
using System.Diagnostics;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// The quantity by which amounts of CPU time are measured: cycles.
    /// </summary>
    public class CpuResourceAmount : IResourceAmount
    {
        /// <summary>
        /// Return a reference to the CpuResource provider
        /// </summary>
        public override IResource Resource
        {
            get { return CpuResource.Provider(); }
        }

        /// <summary>
        /// Create a CpuResourceAmount with the specified number of cycles
        /// </summary>
        /// <param name="cycles"></param>
        public CpuResourceAmount(long cycles)
        {
            this.cycles = cycles;
        }

        /// <summary>
        /// Return the number of cycles in the CpuResourceAmount.
        /// </summary>
        public long Cycles
        {
            get { return cycles; }
            set { cycles = value; }
        }

        // The actual number of cycles represented in the amount are stored in this private variable.
        private long cycles;

        public override IResourceAmount AddTo(IResourceAmount amount)
        {
            Debug.Assert(amount is CpuResourceAmount);
            cycles += ((CpuResourceAmount)amount).Cycles;
            return this;
        }
    }
}
