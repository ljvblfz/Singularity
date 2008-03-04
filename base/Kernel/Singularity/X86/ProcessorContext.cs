////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ProcessorContext.cs
//
//  Note:
//

namespace Microsoft.Singularity.X86
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Threading;

    using Microsoft.Singularity.X86;

    [NoCCtor]
    [CLSCompliant(false)]
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessorContext
    {
        [AccessedByRuntime("referenced from c++")] private UIntPtr Reserved0;

        // The remaining fields are private to the kernel.
        [AccessedByRuntime("referenced from c++")] internal unsafe ThreadContext *threadContext;
        [AccessedByRuntime("referenced from c++")] internal unsafe ProcessorContext *processorContext;
        [AccessedByRuntime("referenced from c++")] private unsafe Processor *_processor; // Only changed by garbage collector.

        [AccessedByRuntime("referenced from c++")] internal UIntPtr exception;

        [AccessedByRuntime("referenced from c++")] internal UIntPtr schedulerStackBegin;
        [AccessedByRuntime("referenced from c++")] internal UIntPtr schedulerStackLimit;
        [AccessedByRuntime("referenced from c++")] internal UIntPtr interruptStackBegin;
        [AccessedByRuntime("referenced from c++")] internal UIntPtr interruptStackLimit;
        [AccessedByRuntime("referenced from c++")] internal UIntPtr interruptStackPreLimit;
        [AccessedByRuntime("referenced from c++")] internal UIntPtr exceptionStackBegin;
        [AccessedByRuntime("referenced from c++")] internal UIntPtr exceptionStackLimit;
        [AccessedByRuntime("referenced from c++")] internal UIntPtr exceptionStackPreLimit;

        [AccessedByRuntime("referenced from c++")] internal ThreadContext exceptionContext;
        [AccessedByRuntime("referenced from c++")] internal ThreadContext thirdContext;

        [AccessedByRuntime("referenced from c++")] internal int cpuId;
        [AccessedByRuntime("referenced from c++")] internal volatile int ipiFreeze;
        [AccessedByRuntime("referenced from c++")] internal unsafe ProcessorContext* nextProcessorContext; // singly-linked circular list node for MpExecution use

        //////////////////////////////////////////////// Methods & Properties.
        //
        internal Processor processor {
            [NoHeapAllocation]
            get { return GetProcessor(); }
        }

        //////////////////////////////////////////////////// External Methods.
        //
        [AccessedByRuntime("output to header: defined in c++")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        private extern Processor GetProcessor();

        [AccessedByRuntime("output to header: defined in c++")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal extern void UpdateAfterGC(Processor processor);
    }
}
