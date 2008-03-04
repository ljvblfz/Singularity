////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   KPTest.sg
//
//  Note:   Tests the kernel-process boundary.
//
//  Feel free to add more commands to this file.

using System;
using Microsoft.SingSharp;
using Microsoft.Singularity;
using Microsoft.Singularity.Stress;
using Microsoft.Singularity.Memory;
using Microsoft.Singularity.Channels;
using Microsoft.Singularity.V1.Services;
using System.Runtime.CompilerServices;

namespace Microsoft.Singularity.Stress
{
    public class StressDirect
    {
        [OutsideGCDomain]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe uint KPTest(SharedHeapService.Allocation* sharedArgs, int i);
    }
}
