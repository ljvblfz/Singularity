////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Hal.cpp
//
//  Note:
//

#include "hal.h"

#if SINGULARITY_KERNEL
#include "halkd.h"
#include "printf.cpp"
#endif // SINGULARITY_KERNEL

//////////////////////////////////////////////////////////////////////////////

extern "C" int _fltused = 0x9875;

//////////////////////////////////////////////////////////////////////////////
// debugging code.    Put it here for now.

#if SINGULARITY_KERNEL

void Halt()
{
    uintptr _ebp;
    __asm mov _ebp, ebp;

    for (int i = 0; i < 30 && _ebp >= 0x4000; i++) {

        uintptr next = ((uintptr *)_ebp)[0];
        uintptr code = ((uintptr *)_ebp)[1];

        printf("    %p: %p %p\n", _ebp, next, code);
        _ebp = next;
    }
    printf("---- Halting. --------------------------------------------------------");

    __asm int 3;
}

void Cls()
{
    KdPutChar('\n');
    for (uint16 n = 0; n < 79; n++) {
        KdPutChar('-');
    }
    KdPutChar('\n');
}

void __cdecl PutChar(char cOut)
{
    KdPutChar(cOut);
}

void fail_assert(const char *expr)
{
    printf("%s\n", expr);
    printf("----- Frame --- EBP ---- Code ----------------------------- Assert Failure\n");
    Halt();
    __asm int 3;
}

#elif SINGULARITY_PROCESS

void fail_assert(Class_System_String *message)
{
    Struct_Microsoft_Singularity_V1_Services_DebugService::g_Print((bartok_char*)&message->m_firstChar,
                                                                   message->m_stringLength);
    //Struct_Microsoft_Singularity_V1_Services_DebugService::g_Break();
    Class_Microsoft_Singularity_DebugStub::g_Break();
}
#endif // SINGULARITY_KERNEL

//////////////////////////////////////////////////////////////////////////////
//
extern "C" int __cdecl _cinit(void);
extern "C" int __cdecl _cfini(void);

#if SINGULARITY_KERNEL

void Class_Microsoft_Singularity_Kernel::g_Kill(int32 action)
{
    ((Struct_Microsoft_Singularity_BootInfo *)g_pBootInfo)->KillAction = action;

    void (__cdecl *pfKill)(void) = (void (__cdecl *)(void))g_pBootInfo->Kill32;
    printf("About to call pfKill(%p) with %08x [g_pBootInfo=%p]\n",
           pfKill, g_pBootInfo->KillAction, g_pBootInfo);
    pfKill();
    Halt();
}

extern wchar_t _LinkDate[];

bartok_char *
Class_Microsoft_Singularity_Kernel::g_HalGetLinkDate()
{
    return (bartok_char *)_LinkDate;
}

// Hoisted this out of checkHinges because VC appears to have changed its name
// mangling strategy for structs in functions.

struct CheckedHingeEntry { uint32 i; char *a; char *b; };

void checkHinges()
{
    // Check for a broken image produce when Bartok compiles through an assembler.
    int busted = false;
    extern CheckedHingeEntry checkedHingeTable[];
    for (CheckedHingeEntry *che = checkedHingeTable; che->a; che++) {
        if (che->a+1 != che->b) {
            printf("%06d: 0x%08x 0x%08x\n", che->i, che->a+1, che->b);
            busted = true;
        }
    }
    if (busted) {
        printf("----- Frame --- EBP ---- Code ------------------------------ Busted Hinges\n");
        Halt();
    }
}

extern "C" static void __cdecl
HalBspEnter(Struct_Microsoft_Singularity_BootInfo *bi)
{
    g_pBootInfo = bi;

    kdprintf("Singularity HAL [%ls]\n", _LinkDate);
    _cinit();

    kdprintf("DebugPort: %04x\n", bi->DebugBasePort);
    KdInitialize(bi);

    Cls();
    printf("Singularity Hardware Abstraction Layer [%ls]\n", _LinkDate);

    printf("\n");

    IdtInitialize();
    IdtLoad();
    ProcessorInitialize(&bi->Cpu0, 0);
    checkHinges();

    Class_Microsoft_Singularity_Tracing::g_Initialize();
    Class_Microsoft_Singularity_Tracing::g_Log(0);
    Class_Microsoft_Singularity_Tracing::g_Log(1);
    Class_Microsoft_Singularity_Tracing::g_Log(2);
    Class_Microsoft_Singularity_Tracing::g_Log(3);

    printf("----------------------------------------------------------------------\n");
    printf("Calling Kernel.Main:\n");
    Class_Microsoft_Singularity_Kernel::g_Main();

    // We should not rely on or use any C++ finalizers.
    // _cfini();
}

extern "C" static void __cdecl
HalApEnter(const Struct_Microsoft_Singularity_BootInfo *bi, int cpu)
{
    IdtLoad();

    const Struct_Microsoft_Singularity_CpuInfo *cpuInfo = &bi->Cpu0 + cpu;
    ProcessorInitialize(cpuInfo, cpu);

    Class_Microsoft_Singularity_Kernel::g_MpMain(cpu);
    Class_Microsoft_Singularity_Processor::g_DisableInterrupts();
    for (int i = 0; i != i + 1; i++) {
        uint16* p = (uint16*)(0xb8000 + (cpu - 1) * 2 * 8);
        for (int r = 0; r < 8; r++) {
            uint16 t = (uint16)(i >> (28 - r * 4));
            t &= 0xf;
            if (t > 9) {
                t += 0x1f00 + 'a' - 10;
            }
            else {
                t += 0x1f00 + '0';
            }
            *p++ = t;
            if (Class_Microsoft_Singularity_DebugStub::g_PollForBreak() == true) {
                __asm int 3;
            }
            for (int i = 0; i < 50000; i++) {
                __asm nop;
            }
        }
    }
}

extern "C" void __cdecl
Hal(Struct_Microsoft_Singularity_BootInfo *bi, int cpu)
{
    if (cpu == 0) {
        HalBspEnter(bi);
    }
    else {
        HalApEnter(bi, cpu);
    }
}

#elif SINGULARITY_PROCESS

BOOL KdDebuggerNotPresent;
extern Class_System_RuntimeType * brtmainClass;
extern int (*brtmain)(ClassVector_Class_System_String *args);
extern int brtmainReturnsInt;

// Note: CallMain's return value is only meaningful if brtmainReturnsInt is true.
// Example:
//   int ret = CallMain(args);
//   if (!MainReturnsInt()) ret = 0;
__declspec(naked) int Class_Microsoft_Singularity_AppRuntime::
g_CallMain(ClassVector_Class_System_String *args)
{
    // To avoid creating an unmanaged stack frame, jmp directly to the main function:
    __asm {
        mov eax, brtmain;
        jmp eax;
    }
}

bool Class_Microsoft_Singularity_AppRuntime::
g_MainReturnsInt()
{
    return (brtmainReturnsInt != 0);
}

void Class_Microsoft_Singularity_AppRuntime::
g_SetDebuggerPresence(bool debuggerPresent)
{
    KdDebuggerNotPresent = !debuggerPresent;
}

extern "C" int32 __fastcall RuntimeEntryPoint(int threadIndex)
{
    int32 ret = 0;

    Struct_Microsoft_Singularity_X86_ThreadContext * context =
        Class_Microsoft_Singularity_Processor::g_GetCurrentThreadContext();

    if (!Struct_Microsoft_Singularity_X86_ThreadContext::m_IsInKernelMode(
        context)) {
        // fail assertion in uninitialized process mode:
        __asm int 3
    }

    Struct_Microsoft_Singularity_X86_ThreadContext::m_SetProcessMode(context);

    if (threadIndex == -1) {
        _cinit();
        Class_Microsoft_Singularity_Tracing::g_Initialize();
        Class_Microsoft_Singularity_Tracing::g_Log(0, "RuntimeEntryPoint:Main");
        Class_Microsoft_Singularity_Monitoring::g_Initialize();

        ret = Class_Microsoft_Singularity_AppRuntime::g_AppStart(brtmainClass);
    }
    else {
        Class_Microsoft_Singularity_Tracing::g_Log(0, "RuntimeEntryPoint:Thread");
        Class_System_Threading_Thread::g_ThreadStub(threadIndex);
    }

    Struct_Microsoft_Singularity_X86_ThreadContext::m_SetKernelMode(context);

    return ret;
}

#endif // SINGULARITY_PROCESS

//////////////////////////////////////////////////////////////////////////////

// Need to put the following marker variables into the .CRT section.
// The .CRT section contains arrays of function pointers.
// The compiler creates functions and adds pointers to this section
// for things like C++ global constructors.
//
// The XIA, XCA etc are group names with in the section.
// The compiler sorts the contributions by the group name.
// For example, .CRT$XCA followed by .CRT$XCB, ... .CRT$XCZ.
// The marker variables below let us get pointers
// to the beginning/end of the arrays of function pointers.
//
// For example, standard groups are
//  XCA used here, for begin marker
//  XCC "compiler" inits
//  XCL "library" inits
//  XCU "user" inits
//  XCZ used here, for end marker
//

typedef void (__cdecl *_PVFV)(void);
// typedef int  (__cdecl *_PIFV)(void);
typedef _PVFV _PIFV;

#pragma comment(linker, "/merge:.CRT=.DATA")

#pragma data_seg(".CRT$XIA", "DATA")
extern "C" _PIFV __xi_a[] = { NULL };                    // C initializers.

#pragma data_seg(".CRT$XIZ", "DATA")
extern "C" _PIFV __xi_z[] = { NULL };

#pragma data_seg(".CRT$XCA", "DATA")
extern "C" _PVFV __xc_a[] = { NULL };                    // C++ initializers.

#pragma data_seg(".CRT$XCZ", "DATA")
extern "C" _PVFV __xc_z[] = { NULL };

#pragma data_seg(".CRT$XPA", "DATA")
extern "C" _PVFV __xp_a[] = { NULL };                    // C pre-terminators.

#pragma data_seg(".CRT$XPZ", "DATA")
extern "C" _PVFV __xp_z[] = { NULL };

#pragma data_seg(".CRT$XTA", "DATA")
extern "C" _PVFV __xt_a[] = { NULL };                    // C terminators.

#pragma data_seg(".CRT$XTZ", "DATA")
extern "C" _PVFV __xt_z[] = { NULL };

#pragma data_seg()

// Walk an array of function pointers, call non-NULL ones.
void __cdecl _initterm(_PVFV *pfbegin, _PVFV *pfend)
{
    for (; pfbegin < pfend; pfbegin++) {
        if (*pfbegin != NULL) {
            (**pfbegin)();
        }
    }
}

// Call all of the C++ static constructors.
//
int __cdecl _cinit(void)
{
    // do C initializations
    _initterm( __xi_a, __xi_z );

    // do C++ initializations
    _initterm( __xc_a, __xc_z );
    return 0;
}

int  __cdecl _cfini(void)
{
    // do C initializations
    _initterm( __xp_a, __xp_z );

    // do C++ terminations
    _initterm( __xt_a, __xt_z );
    return 0;
}
