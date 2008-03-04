////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Idt.cs
//
//  Note:
//

namespace Microsoft.Singularity.X86
{
    using System;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;

    // A lidt pointer to the tables
    [CLSCompliant(false)]
    [AccessedByRuntime("referenced from c++")]
    internal struct IDTP
    {
        [AccessedByRuntime("referenced from c++")] 
        internal ushort      pad;
        [AccessedByRuntime("referenced from c++")] 
        internal ushort      limit;
        [AccessedByRuntime("referenced from c++")] 
        internal uint        addr;
    };

    [CLSCompliant(false)]
    [AccessedByRuntime("referenced from c++")]
    internal struct IDTE
    {
        // An entry in the Interrupt Descriptor Table
        [AccessedByRuntime("referenced from c++")] 
        internal ushort     offset_0_15;
        [AccessedByRuntime("referenced from c++")] 
        internal ushort     selector;
        [AccessedByRuntime("referenced from c++")] 
        internal byte       zeros;
        [AccessedByRuntime("referenced from c++")] 
        internal byte       access;
        [AccessedByRuntime("referenced from c++")] 
        internal ushort     offset_16_31;

        ///////////////////////////////////////// Interrupt Descriptor Tables.
        //
        [AccessedByRuntime("referenced from c++")]
        internal const uint PRESENT      = 0x80;
        [AccessedByRuntime("referenced from c++")] 
        internal const uint DPL_RING3    = 0x60;
        [AccessedByRuntime("referenced from c++")]
        internal const uint DPL_RING2    = 0x40;
        [AccessedByRuntime("referenced from c++")]
        internal const uint DPL_RING1    = 0x20;
        [AccessedByRuntime("referenced from c++")] 
        internal const uint DPL_RING0    = 0x00;
        [AccessedByRuntime("referenced from c++")]
        internal const uint TASK_GATE    = 0x05;
        [AccessedByRuntime("referenced from c++")]
        internal const uint CALL_GATE    = 0x0c;
        [AccessedByRuntime("referenced from c++")] 
        internal const uint INT_GATE     = 0x0e;
        [AccessedByRuntime("referenced from c++")]
        internal const uint TRAP_GATE    = 0x0f;
    }
}
