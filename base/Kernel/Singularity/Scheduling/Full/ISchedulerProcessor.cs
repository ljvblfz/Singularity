////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\ISchedulerProcessor.cs
//
//  Note:
//

using System;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// Summary description for ISchedulerProcessor.
    /// </summary>
    [CLSCompliant(false)]
    public abstract class ISchedulerProcessor
    {
        public abstract Processor EnclosingProcessor { get; }
    }
}
