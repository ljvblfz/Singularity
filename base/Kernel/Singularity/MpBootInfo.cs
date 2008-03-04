///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File: MpBootInfo.cs
//
//  Note:
//    This structure holds values needed to bring the application processors
//    into protected mode.
//

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Singularity.Memory;

namespace Microsoft.Singularity
{
    [StructLayout(LayoutKind.Sequential, Pack=4)]
    [CLSCompliant(false)]
    public struct MpBootInfo
    {
#if SINGULARITY_MP
        [AccessedByRuntime("referenced in c++")]
        public const uint MAX_CPU =
#  if MAX_CPU4
                                    4;
#  elif MAX_CPU3
                                    3;
#  elif MAX_CPU2
                                    2;
#  else
#  error "MAX_CPUx needs conversion definition."
#  endif  // MAX_CPUX
#else
        [AccessedByRuntime("referenced in c++")]
        public const uint MAX_CPU = 1;
#endif // SINGULARITY_MP

        [AccessedByRuntime("referenced in c++")]
        public const uint Signature = 0x4d504249; // "MPBI"

        // Settings for next processor to enter protected mode
        [AccessedByRuntime("referenced in c++")]
        public uint     signature;
        [AccessedByRuntime("referenced in c++")]
        public UIntPtr  KernelStackBegin;
        [AccessedByRuntime("referenced in c++")]
        public UIntPtr  KernelStack;
        [AccessedByRuntime("referenced in c++")]
        public UIntPtr  KernelStackLimit;
        [AccessedByRuntime("referenced in c++")]
        public volatile int TargetCpu;

        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(16)]
        internal static unsafe extern MpBootInfo * HalGetMpBootInfo();

        [AccessedByRuntime("referenced in c++")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(16)]
        internal static unsafe extern void HalReleaseMpStartupLock();

        public static unsafe bool PrepareForCpuStart(int targetCpu)
        {
            UIntPtr size = MemoryManager.PagePad(
                new UIntPtr(BootInfo.KERNEL_STACK_LIMIT - BootInfo.KERNEL_STACK_BEGIN)
                );

            MpBootInfo* mbi = HalGetMpBootInfo();
            mbi->KernelStackBegin = MemoryManager.KernelAllocate(
                MemoryManager.PagesFromBytes(size), null, 0, System.GCs.PageType.Stack);

            if (mbi->KernelStackBegin == UIntPtr.Zero)
            {
                mbi->KernelStackLimit = UIntPtr.Zero;
                mbi->KernelStack      = UIntPtr.Zero;
                mbi->signature        = 0;
                return false;
            }

            mbi->KernelStackLimit = mbi->KernelStackBegin + size;
            mbi->KernelStack      = mbi->KernelStackLimit - (BootInfo.KERNEL_STACK_LIMIT - BootInfo.KERNEL_STACK);
            mbi->signature = Signature;

            mbi->TargetCpu = targetCpu;
            HalReleaseMpStartupLock();

            return true;
        }

        // NB attribute is necessary to get definition of MpBootInfo as
        // a struct by Bartok for all builds (including those that do not
        // use MpBootInfo).
        [AccessedByRuntime("referenced in c++")]
        public static unsafe MpBootInfo GetMpBootInfo()
        {
            MpBootInfo *ptr = HalGetMpBootInfo();
            return *ptr;
        }
    }
}
