////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Processor.cs
//
//  Note:
//

namespace Microsoft.Singularity
{
    using System;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    using System.Threading;

    using Microsoft.Singularity.X86;
    using Microsoft.Singularity.V1.Services;

    [CLSCompliant(false)]
    [NoCCtor]
    public class Processor
    {
        public static long CyclesPerSecond
        {
            [NoHeapAllocation]
            get { return ProcessService.GetCyclesPerSecond(); }
        }

        public static ulong CycleCount
        {
            [NoHeapAllocation]
            get { return GetCycleCount(); }
        }

        //////////////////////////////////////////////////// External Methods.
        //
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        public static extern ulong GetCycleCount();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern UIntPtr GetFrameEip(UIntPtr ebp);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern UIntPtr GetFrameEbp(UIntPtr ebp);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern UIntPtr GetStackPointer();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern UIntPtr GetFramePointer();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern ThreadContext * GetCurrentThreadContext();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern ProcessorContext * GetCurrentProcessorContext();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern Thread GetCurrentThread();

        //////////////////////////////////////////////////////////////////////////////
        //
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern void WriteMsr(uint offset,
                                           ulong value);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern ulong ReadMsr(uint offset);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(64)]
        [NoHeapAllocation]
        public static extern void ReadCpuid(uint feature,
                                            out uint v0,
                                            out uint v1,
                                            out uint v2,
                                            out uint v3);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern ulong ReadPmc(uint offset);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern void EnterRing3();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        public static extern bool AtKernelPrivilege();

        //////////////////////////////////////////////////////////////////////
        //
        //
        // These methods are currently marked external because they are used
        // by device drivers.  We need a tool to verify that device drivers
        // are in fact using them correctly!
        //
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern bool DisableInterrupts();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern void RestoreInterrupts(bool enabled);

        // Use this method for assertions only!
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern bool InterruptsDisabled();

        //////////////////////////////////////////////////////////////////////
        //
        //
        // These methods are public and safe to use from any where provided
        // there's at least 2 call frame on the stack.
        //
        [NoHeapAllocation]
        public static UIntPtr GetCallerEip()
        {
            UIntPtr currentFrame = GetFramePointer();
            UIntPtr callerFrame = GetFrameEbp(currentFrame);
            if (callerFrame == UIntPtr.Zero) {
                return UIntPtr.Zero;
            }
            UIntPtr callersCaller = GetFrameEip(callerFrame);
            return callersCaller;
        }

        /// <summary>
        /// Provides a mini stack trace starting from the caller of the caller
        /// of this method.
        /// </summary>
        [NoHeapAllocation]
        public static void GetStackEips(out UIntPtr pc1, out UIntPtr pc2, out UIntPtr pc3)
        {
            pc1 = UIntPtr.Zero;
            pc2 = UIntPtr.Zero;
            pc3 = UIntPtr.Zero;

            UIntPtr currentFrame = GetFramePointer();
            UIntPtr callerFrame = GetFrameEbp(currentFrame);
            if (callerFrame == UIntPtr.Zero) {
                return;
            }
            pc1 = GetFrameEip(callerFrame);
            callerFrame = GetFrameEbp(callerFrame);
            if (callerFrame == UIntPtr.Zero) {
                return;
            }
            pc2 = GetFrameEip(callerFrame);
            callerFrame = GetFrameEbp(callerFrame);
            if (callerFrame == UIntPtr.Zero) {
                return;
            }
            pc3 = GetFrameEip(callerFrame);
        }

        /// <summary>
        /// Provides the full stack trace starting from the caller of the caller
        /// of this method.
        /// </summary>
        /// <returns>Eip values in stack array from top to bottom</returns>
        [NoHeapAllocation]
        public static void GetStackEips(UIntPtr[] stack)
        {
            if (stack == null) {
                return;
            }
            UIntPtr currentFrame = GetFramePointer();
            UIntPtr callerFrame = GetFrameEbp(currentFrame);
            for (int index = 0; callerFrame != UIntPtr.Zero && index < stack.Length; index++) {
                stack[index] = GetFrameEip(callerFrame);
                callerFrame = GetFrameEbp(callerFrame);
            }
        }
    }
}
