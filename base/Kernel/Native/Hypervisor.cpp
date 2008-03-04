////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   hyper.cpp
//
//  Note:
//      Hypervisor interaction
//

#include "hal.h"

#if SINGULARITY_KERNEL

uint16 Class_Microsoft_Singularity_Hypervisor::g_DispatchRepHypercall(uint16 callCode, 
                                                                      uint16 *reps, uint16 start,
                                                                      UIntPtr inputBuffer, UIntPtr outputBuffer)
{
    uint32 hi = ((uint32)*reps) | (((uint32)start) << 16);
    uint32 lo = (uint32) callCode;

    uint32 result, complete;

    uint32 dest = Struct_Microsoft_Singularity_BootInfo_HYPERCALL_PAGE;

    __asm {
        mov edx, hi;
        mov eax, lo;
        xor ebx, ebx;
        mov ecx, inputBuffer;
        xor edi, edi;
        mov esi, outputBuffer;

        call dest;

        mov result, eax;
        mov complete, edx;
    }

    *reps = (complete&0x3F);
    return (result&0xff);
}

uint16 Class_Microsoft_Singularity_Hypervisor::g_DispatchHypercall(uint16 callCode,
                                                                   UIntPtr inputBuffer, UIntPtr outputBuffer)
{
    uint32 lo = (uint32) callCode;

    uint32 result;

    uint32 dest = Struct_Microsoft_Singularity_BootInfo_HYPERCALL_PAGE;

    __asm {
        xor edx, edx;
        mov eax, lo;
        xor ebx, ebx;
        mov ecx, inputBuffer;
        xor edi, edi;
        mov esi, outputBuffer;

        call dest;

        mov result, eax;
    }

    return (result&0xff);
}

#endif // SINGULARITY_KERNEL
