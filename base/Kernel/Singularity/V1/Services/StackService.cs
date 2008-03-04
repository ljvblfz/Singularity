////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity - Singularity ABI
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File: StackService.cs
//
//  Note:
//

using System;
using System.Runtime.CompilerServices;
using Microsoft.Singularity.Memory;

namespace Microsoft.Singularity.V1.Services
{
    public struct StackService
    {
        [ExternalEntryPoint]
        public static void WalkStack()
        {
            Stacks.WalkStack(Processor.GetFramePointer());
        }

        [ExternalEntryPoint]
        [NoHeapAllocation]
        [CLSCompliant(false)]
        public static unsafe void GetUsageStatisticsImpl(ulong *gets, ulong *returns)
        {
            GetUsageStatistics(out *gets, out *returns);
        }

        [NoHeapAllocation]
        [CLSCompliant(false)]
        public static void GetUsageStatistics(out ulong gets, out ulong returns)
        {
            gets = (ulong)Stacks.GetCount;
            returns = (ulong)Stacks.ReturnCount;
        }

        [AccessedByRuntime("referenced from halstack.asm")]
        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void LinkSharedStack();

        [AccessedByRuntime("referenced from halstack.asm")]
        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void UnlinkSharedStack();

        [ExternalEntryPoint]
        [CLSCompliant(false)]
        public static unsafe UIntPtr LinkNewStackSegment(
            UIntPtr size,
            uint *arg2,
            uint args,
            UIntPtr esp,
            UIntPtr begin,
            UIntPtr limit)
        {
            Microsoft.Singularity.X86.ThreadContext* context = Processor.GetCurrentThreadContext();
            return Microsoft.Singularity.Memory.Stacks.GetStackSegmentAndCopy(
                size, ref *context, arg2, args, esp, begin, limit);
        }

        [ExternalEntryPoint]
        [CLSCompliant(false)]
        //[NoHeapAllocation]
        public static unsafe void ReturnStackSegmentRaw(
            UIntPtr begin,
            UIntPtr limit)
        {
            Microsoft.Singularity.X86.ThreadContext* context = Processor.GetCurrentThreadContext();
            Microsoft.Singularity.Memory.Stacks.ReturnStackSegmentRaw(
                ref *context, begin, limit);
        }
    }
}
