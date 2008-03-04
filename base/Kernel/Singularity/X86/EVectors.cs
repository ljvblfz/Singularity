////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   EVectors.cs
//
//  Note:
//

namespace Microsoft.Singularity.X86
{
    using System;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;

    [CLSCompliant(false)]
    [AccessedByRuntime("referenced from C++")]
    internal struct EVectors
    {
        // Interrupt Vector Assignments
        // 0..31 Intel defined
        [AccessedByRuntime("referenced from C++")]
        internal const uint DivideError                 = 0;
        [AccessedByRuntime("referenced from C++")]
        internal const uint SingleStep                  = 1;
        [AccessedByRuntime("referenced from C++")]
        internal const uint Nmi                         = 2;
        [AccessedByRuntime("referenced from C++")]
        internal const uint Breakpoint                  = 3;
        [AccessedByRuntime("referenced from C++")]
        internal const uint OverflowException           = 4;
        [AccessedByRuntime("referenced from C++")]
        internal const uint BoundRangeException         = 5;
        [AccessedByRuntime("referenced from C++")]
        internal const uint IllegalInstruction          = 6;
        [AccessedByRuntime("referenced from C++")]
        internal const uint CoprocessorNotAvailable     = 7;
        [AccessedByRuntime("referenced from C++")]
        internal const uint DoubleFault                 = 8;
        [AccessedByRuntime("referenced from C++")]
        internal const uint CoprocessorSegmentOverrun   = 9;
        [AccessedByRuntime("referenced from C++")]
        internal const uint InvalidTss                  = 10;
        [AccessedByRuntime("referenced from C++")]
        internal const uint SegmentNotPresent           = 11;
        [AccessedByRuntime("referenced from C++")]
        internal const uint StackSegmentFault           = 12;
        [AccessedByRuntime("referenced from C++")]
        internal const uint GeneralProtectionFault      = 13;
        [AccessedByRuntime("referenced from C++")]
        internal const uint PageFault                   = 14;
        [AccessedByRuntime("referenced from C++")]
        internal const uint IntelReserved               = 14;
        [AccessedByRuntime("referenced from C++")]
        internal const uint FpuMathFault                = 16;
        [AccessedByRuntime("referenced from C++")]
        internal const uint AlignmentCheck              = 17;
        [AccessedByRuntime("referenced from C++")]
        internal const uint MachineCheck                = 18;
        [AccessedByRuntime("referenced from C++")]
        internal const uint SseMathFault                = 19;

        // Reserved, but used by Singularity
        [AccessedByRuntime("referenced from C++")]
        internal const uint FirstChanceException        = 29;
        [AccessedByRuntime("referenced from C++")]
        internal const uint SecondChanceException       = 30;
        [AccessedByRuntime("referenced from C++")]
        internal const uint DebuggerBreakRequest        = 31;

        // 32..255 User defined
        [AccessedByRuntime("referenced from C++")]
        internal const uint BaseUserException           = 32;

        // haryadi defined
        [AccessedByRuntime("referenced from C++")]
        internal const uint PingPongInt                 = 33;
        [AccessedByRuntime("referenced from C++")]
        internal const uint ApImage                     = 34;

        // haryadi: currently, this is 1 entry for 1 ABI.
        // In the future, 1 entry should represents the whole ABI??
        [AccessedByRuntime("referenced from C++")]
        internal const uint AbiCall                     = 35;

        [AccessedByRuntime("referenced from C++")]
        internal const uint HaltApProcessors            = 36;
    }
}
