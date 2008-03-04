//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  DebugStub.cpp: runtime support for debugging
//
//
#include "hal.h"

#if SINGULARITY_PROCESS
#if PAGING
int ccc;
extern "C" void __cdecl _pushStackMark();
__declspec(naked) void Class_Microsoft_Singularity_DebugStub::g_Foo()
{
    __asm
    {
        ret
    }
//    ccc = *((int *)_pushStackMark);
}
#endif
void Class_Microsoft_Singularity_DebugStub::g_Break()
{
    __asm int 3;
}
#endif
