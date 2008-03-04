////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity - Singularity ABI
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   StackService.cs
//
//  Note:
//

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Singularity.V1.Services
{
    public struct StackService
    {
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1088)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void WalkStack();

        [NoHeapAllocation]
        public static void GetUsageStatistics(
            out ulong gets,
            out ulong returns)
        {
            unsafe {
                fixed (ulong * getsPtr = &gets, returnsPtr = &returns) {
                    GetUsageStatisticsImpl(getsPtr, returnsPtr);
                }
            }
        }

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1170)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe void GetUsageStatisticsImpl(
            ulong * gets,
            ulong * returns);
    }
}
