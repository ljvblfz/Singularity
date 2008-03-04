////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Thread.cpp
//
//  Note:
//

#include "hal.h"

////////////////////////////////////////////////////////////// Thread Context.
//
void
Struct_Microsoft_Singularity_X86_ThreadContext::
m_UpdateAfterGC(Struct_Microsoft_Singularity_X86_ThreadContext * self,
                Class_System_Threading_Thread *thread)
{
    self->_thread = thread;
}

Class_System_Threading_Thread *
Struct_Microsoft_Singularity_X86_ThreadContext::
m_GetThread(Struct_Microsoft_Singularity_X86_ThreadContext * self)
{
    return self->_thread;
}

#if SINGULARITY_KERNEL
void
Struct_Microsoft_Singularity_X86_ThreadContext::
m_Initialize(Struct_Microsoft_Singularity_X86_ThreadContext * self,
             int threadIndex,
             UIntPtr stack,
			 uint32 cr3)
{
    uintptr *esp = (uintptr*)stack;
    *--esp = 0;
    *--esp = 0;
    self->ebp = (uintptr)esp;
    self->esp = (uintptr)esp;
    self->eip = (uintptr)Class_System_Threading_Thread::g_ThreadStub;
    self->cs0 = (uintptr)(offsetof(Struct_Microsoft_Singularity_CpuInfo,GdtPC) -
                          offsetof(Struct_Microsoft_Singularity_CpuInfo,GdtNull));
    self->ecx = threadIndex;
	self->cr3 = cr3;
    self->efl = Struct_Microsoft_Singularity_X86_EFlags_IF | Struct_Microsoft_Singularity_X86_EFlags_IOPL; // TODO: get rid of IOPL
    self->mmx.fcw = 0x037f;
    self->mmx.ftw = 0;
    // self->mmx.mxcsr = 0x1f80;
}

void
Struct_Microsoft_Singularity_X86_ThreadContext::
m_InitializeIdle(Struct_Microsoft_Singularity_X86_ThreadContext * self,
                 int threadIndex,
                 UIntPtr stack,
				 uint32 cr3)
{
    uintptr *esp = (uintptr*)stack;
    *--esp = 0;
    *--esp = 0;
    self->ebp = (uintptr)esp;
    self->esp = (uintptr)esp;
    self->eip = (uintptr)Class_System_Threading_Thread::g_ThreadIdleStub;
    self->cs0 = (uintptr)(offsetof(Struct_Microsoft_Singularity_CpuInfo,GdtPC) -
                          offsetof(Struct_Microsoft_Singularity_CpuInfo,GdtNull));
    self->ecx = threadIndex;
	self->cr3 = cr3;
    self->efl = Struct_Microsoft_Singularity_X86_EFlags_IF | Struct_Microsoft_Singularity_X86_EFlags_IOPL; // TODO: get rid of IOPL
    self->mmx.fcw = 0x037f;
    self->mmx.ftw = 0;
    // self->mmx.mxcsr = 0x1f80;
}
#endif // SINGULARITY_KERNEL

//
///////////////////////////////////////////////////////////////// End of File.
