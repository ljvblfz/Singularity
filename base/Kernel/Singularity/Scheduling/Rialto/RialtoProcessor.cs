////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   RialtoProcessor.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Rialto
{
    /// <summary>
    /// </summary>
    public class RialtoProcessor : ISchedulerProcessor
    {
        private readonly Processor enclosingProcessor;

        public RialtoProcessor(Processor processor)
        {
            enclosingProcessor = processor;
        }

        public override Processor EnclosingProcessor
        {
            get { return enclosingProcessor; }
        }
    }
}
