//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Microsoft.Singularity
{
    /// <remarks> Per-Processor configuration state. </remarks>
    [StructLayout(LayoutKind.Sequential, Pack=4)]
    [CLSCompliant(false)]
    public struct CpuInfo
    {
        // Gdt Pointer
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTP   GdtPtr;

        // Base GDT
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtNull;
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtRS;  // Real mode stack
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtRC;  // Real mode code
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtPC;  // Protected mode code (ring 0) : 1 of 4 for SYSENTER
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtPD;  // Protected mode data (ring 0) : 2 of 4 for SYSENTER
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtUC;  // Protected mode code (ring 3) : 3 of 4 for SYSENTER
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtUD;  // Protected mode data (ring 3) : 4 of 4 for SYSENTER
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtPF;  // FS Register
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtPG;  // GS Register
        [AccessedByRuntime("referenced in c++")]
        internal X86.GDTE   GdtTSS; // TSS

        [AccessedByRuntime("referenced in c++")]
        internal uint       GdtEnd;

        // FS and GS hold processor context and thread respectively
        [AccessedByRuntime("referenced in c++")]
        internal UIntPtr    Fs32;
        [AccessedByRuntime("referenced in c++")]
        internal UIntPtr    Gs32;

        // Only valid for processors started after bootstrap
        [AccessedByRuntime("referenced in c++")]
        internal UIntPtr    KernelStackBegin;
        [AccessedByRuntime("referenced in c++")]
        internal UIntPtr    KernelStack;
        [AccessedByRuntime("referenced in c++")]
        internal UIntPtr    KernelStackLimit;

        // Id number {0, 1, ..., Procs-1}
        [AccessedByRuntime("referenced in c++")]
        internal uint       ProcessorId;
    }
}
