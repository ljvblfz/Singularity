////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   MmxContext.cs
//
//  Note:
//

namespace Microsoft.Singularity.X86
{
    using System;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;

    [CLSCompliant(false)]
    [StructLayout(LayoutKind.Sequential)]
    [StructAlign(16)]
    //[AccessedByRuntime("")]
    internal struct UINT128
    {
        //[AccessedByRuntime("")] 
        public ulong    lo;
        //[AccessedByRuntime("")]
        public ulong    hi;
    }

    [CLSCompliant(false)]
    [StructLayout(LayoutKind.Sequential)]
    [StructAlign(16)]
    [AccessedByRuntime("referenced from c++")]
    internal struct MmxContext
    {
        [AccessedByRuntime("referenced from c++")]
        public ushort   fcw;
        [AccessedByRuntime("referenced from c++")] 
        public ushort   fsw;
        [AccessedByRuntime("referenced from c++")]
        public ushort   ftw;
        [AccessedByRuntime("referenced from c++")]
        public ushort   fop;
        [AccessedByRuntime("referenced from c++")]
        public uint     eip;
        [AccessedByRuntime("referenced from c++")] 
        public uint     cs;

        [AccessedByRuntime("referenced from c++")]
        public uint     dp;
        [AccessedByRuntime("referenced from c++")] 
        public uint     ds;
        [AccessedByRuntime("referenced from c++")] 
        public uint     mxcsr;
        [AccessedByRuntime("referenced from c++")] 
        public uint     mxcsrmask;

        [AccessedByRuntime("referenced from c++")]
        public UINT128  st0;
        [AccessedByRuntime("referenced from c++")] 
        public UINT128  st1;
        [AccessedByRuntime("referenced from c++")]
        public UINT128  st2;
        [AccessedByRuntime("referenced from c++")] 
        public UINT128  st3;
        [AccessedByRuntime("referenced from c++")] 
        public UINT128  st4;
        [AccessedByRuntime("referenced from c++")]
        public UINT128  st5;
        [AccessedByRuntime("referenced from c++")] 
        public UINT128  st6;
        [AccessedByRuntime("referenced from c++")]
        public UINT128  st7;

        [AccessedByRuntime("referenced from c++")] 
        public UINT128  xmm0;
        [AccessedByRuntime("referenced from c++")]
        public UINT128  xmm1;
        [AccessedByRuntime("referenced from c++")] 
        public UINT128  xmm2;
        [AccessedByRuntime("referenced from c++")]
        public UINT128  xmm3;
        [AccessedByRuntime("referenced from c++")]
        public UINT128  xmm4;
        [AccessedByRuntime("referenced from c++")] 
        public UINT128  xmm5;
        [AccessedByRuntime("referenced from c++")] 
        public UINT128  xmm6;
        [AccessedByRuntime("referenced from c++")]
        public UINT128  xmm7;

        public UINT128  reserved2;
        public UINT128  reserved3;
        public UINT128  reserved4;
        public UINT128  reserved5;
        public UINT128  reserved6;
        public UINT128  reserved7;
        public UINT128  reserved8;
        public UINT128  reserved9;
        public UINT128  reservedA;
        public UINT128  reservedB;
        public UINT128  reservedC;
        public UINT128  reservedD;
        public UINT128  reservedE;
        public UINT128  reservedF;
    }
}

