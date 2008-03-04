////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Gdt.cs
//
//  Note:
//

namespace Microsoft.Singularity.X86
{
    using System;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;

    // A lgdt pointer to the tables
    //
    [CLSCompliant(false)]
    [AccessedByRuntime("referenced from c++")]
    internal struct GDTP
    {
        [AccessedByRuntime("referenced from c++")]
        internal ushort     pad;
        [AccessedByRuntime("referenced from c++")]
        internal ushort     limit;
        [AccessedByRuntime("referenced from c++")]
        internal uint       addr;
    };

    /////////////////////////////////////////////// Segment Descriptor Tables.
    //
    // An entry in the Global Descriptor Table
    //
    [AccessedByRuntime("referenced from c++")]
    [CLSCompliant(false)]
    internal struct GDTE
    {
        [AccessedByRuntime("referenced from c++")]
        internal ushort     limit;
        [AccessedByRuntime("referenced from c++")]
        internal ushort     base0_15;
        [AccessedByRuntime("referenced from c++")]
        internal byte       base16_23;
        [AccessedByRuntime("referenced from c++")]
        internal byte       access;
        [AccessedByRuntime("referenced from c++")]
        internal byte       granularity;
        [AccessedByRuntime("referenced from c++")]
        internal byte       base24_31;

        // granularity bits
        [AccessedByRuntime("referenced from c++")]
        internal const uint PAGES       = 0x80;
        [AccessedByRuntime("referenced from c++")]
        internal const uint IS32BIT     = 0x40;
        [AccessedByRuntime("referenced from c++")]
        internal const uint IS64BIT    = 0x20;
        [AccessedByRuntime("referenced from c++")]
        internal const uint LIMIT20     = 0x0f;

        // access bits
        [AccessedByRuntime("referenced from c++")]
        internal const uint PRESENT     = 0x80;
        [AccessedByRuntime("referenced from c++")]
        internal const uint RING3       = 0x60;
        [AccessedByRuntime("referenced from c++")]
        internal const uint RING2       = 0x40;
        [AccessedByRuntime("referenced from c++")]
        internal const uint RING1       = 0x20;
        [AccessedByRuntime("referenced from c++")]
        internal const uint RING0       = 0x00;
        [AccessedByRuntime("referenced from c++")]
        internal const uint USER        = 0x10;
        [AccessedByRuntime("referenced from c++")]
        internal const uint CODE        = 0x08;
        [AccessedByRuntime("referenced from c++")]
        internal const uint CONFORMING  = 0x04;
        [AccessedByRuntime("referenced from c++")]
        internal const uint EXPANDDOWN  = 0x04; // Data
        [AccessedByRuntime("referenced from c++")]
        internal const uint READABLE    = 0x02;
        [AccessedByRuntime("referenced from c++")]
        internal const uint WRITEABLE   = 0x02; // Data
        [AccessedByRuntime("referenced from c++")]
        internal const uint ACCESSED    = 0x01;

        // Non-user types:
        [AccessedByRuntime("referenced from c++")]
        internal const uint Tss16Free   = 0x01;
        [AccessedByRuntime("referenced from c++")]
        internal const uint Ldt         = 0x02;
        [AccessedByRuntime("referenced from c++")]
        internal const uint Tss16Busy   = 0x03;
        [AccessedByRuntime("referenced from c++")]
        internal const uint CallGate16  = 0x04;
        [AccessedByRuntime("referenced from c++")]
        internal const uint TaskGate    = 0x05;
        [AccessedByRuntime("referenced from c++")]
        internal const uint IntGate16   = 0x06;
        [AccessedByRuntime("referenced from c++")]
        internal const uint TrapGate16  = 0x07;
        [AccessedByRuntime("referenced from c++")]
        internal const uint Reserved8   = 0x08;
        [AccessedByRuntime("referenced from c++")]
        internal const uint Tss32Free   = 0x09;
        [AccessedByRuntime("referenced from c++")]
        internal const uint Reserved10  = 0x0a;
        [AccessedByRuntime("referenced from c++")]
        internal const uint Tss32Busy   = 0x0b;
        [AccessedByRuntime("referenced from c++")]
        internal const uint CallGate32  = 0x0c;
        [AccessedByRuntime("referenced from c++")]
        internal const uint Reserved13  = 0x0d;
        [AccessedByRuntime("referenced from c++")]
        internal const uint IntGate32   = 0x0e;
        [AccessedByRuntime("referenced from c++")]
        internal const uint TrapGate32  = 0x0f;
    }
}

