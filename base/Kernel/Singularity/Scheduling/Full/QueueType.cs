////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   QueueType.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Microsoft.Singularity;

namespace Microsoft.Singularity.Scheduling
{
    public enum QueueType
    {
        GuaranteedQueue = 1,
        NonGuaranteedQueue = 2,
        IdleQueue = 3,
        UnfinishedQueue = 4,
        NoQueue = 5,
    };
}


