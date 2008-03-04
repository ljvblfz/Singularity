////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Processor.cpp
//
//  Note:
//

#include "hal.h"

#if SINGULARITY_KERNEL
#include "halkd.h"
#endif // SINGULARITY_KERNEL

/////////////////////////////////////////////////////////// Segment Selectors.
//
#define SEGMENT_SELECTOR(s) \
    (uint16)(offsetof(Struct_Microsoft_Singularity_CpuInfo,s) \
             - offsetof(Struct_Microsoft_Singularity_CpuInfo,GdtNull))

/////////////////////////////////////////////////////////// Processor Context.
//
Class_Microsoft_Singularity_Processor *
Struct_Microsoft_Singularity_X86_ProcessorContext::
m_GetProcessor(Struct_Microsoft_Singularity_X86_ProcessorContext *self)
{
    return self->_processor;
}

#if SINGULARITY_KERNEL
void
Struct_Microsoft_Singularity_X86_ProcessorContext::
m_UpdateAfterGC(Struct_Microsoft_Singularity_X86_ProcessorContext * self,
                Class_Microsoft_Singularity_Processor *processor)
{
    self->_processor = processor;
}
#endif // SINGULARITY_KERNEL

/////////////////////////////////////////////////////////// Processor Methods.
//
#if SINGULARITY_KERNEL

static int g_nIgnoredHardwareInterrupts = 0;

static __declspec(align(8)) Struct_Microsoft_Singularity_X86_IDTP g_idt;
static __declspec(align(8)) Struct_Microsoft_Singularity_X86_IDTE g_idtEntries[256];
void (__fastcall *c_exceptionHandler)(int exception,
                                      Struct_Microsoft_Singularity_X86_ThreadContext *context);
void (__fastcall *c_interruptHandler)(int exception,
                                      Struct_Microsoft_Singularity_X86_ThreadContext *context);

__declspec(naked)
void Class_Microsoft_Singularity_Processor::g_HaltUntilInterruptNative()
{
    __asm {
        hlt;
        ret;
    }
}

__declspec(naked)
void Class_Microsoft_Singularity_Processor::g_InitFpu(void)
{
    __asm {
        finit;
        mov eax, 0x37e;
        push eax;
        fldcw [esp];
        pop eax;
        ret;
    }
}

__declspec(naked)
uint32 Class_Microsoft_Singularity_Processor::g_ReadFpuStatus(void)
{
    __asm {
        xor eax,eax;
        push eax;
        fnstsw [esp];
        pop eax;
        ret;
    }
}

__declspec(naked)
void Class_Microsoft_Singularity_Processor::g_ClearFpuStatus(void)
{
    __asm {
        fnclex;
        ret;
    }
}

__declspec(naked)
Class_Microsoft_Singularity_Processor *
Class_Microsoft_Singularity_Processor::g_GetCurrentProcessor()
{
    __asm {
        mov eax, fs:[0]Struct_Microsoft_Singularity_X86_ProcessorContext._processor;
        ret;
    }
}

#endif // SINGULARITY_KERNEL

__declspec(naked)
uint64 Class_Microsoft_Singularity_Processor::g_ReadMsr(uint32 counter)
{
    __asm {
        // ECX = msr
        rdmsr;
        ret;
    }
}

void Class_Microsoft_Singularity_Processor::g_WriteMsr(uint32 msr, uint64 value)
{
    uint32 lo = *(((uint32*)&value) + 0);
    uint32 hi = *(((uint32*)&value) + 1);

    __asm {
        mov ecx, msr;
        mov eax, lo;
        mov edx, hi;
        wrmsr;
    }
}

void Class_Microsoft_Singularity_Processor::g_ReadCpuid(uint32 feature,
                                                        uint32 *p0,
                                                        uint32 *p1,
                                                        uint32 *p2,
                                                        uint32 *p3)
{
    uint32 v0;
    uint32 v1;
    uint32 v2;
    uint32 v3;

    __asm {
        mov eax, feature;
        cpuid;
        mov v0, eax;
        mov v1, ebx;
        mov v2, ecx;
        mov v3, edx;
    }
    *p0 = v0;
    *p1 = v1;
    *p2 = v2;
    *p3 = v3;
}

__declspec(naked)
uint64 Class_Microsoft_Singularity_Processor::g_ReadPmc(uint32 counter)
{
    __asm {
        // ECX = counter
        rdpmc;
        ret;
    }
}

__declspec(naked)
uint64 Class_Microsoft_Singularity_Processor::g_GetCycleCount()
{
    __asm {
        rdtsc;
        ret;
    }
}

#ifndef ZERO_RUNTIME
UIntPtr Class_Microsoft_Singularity_Processor::g_GetFrameEip(UIntPtr ebp)
{
    if (ebp < (UIntPtr)0x10000) {
        return 0;
    }
    return ((UIntPtr*)ebp)[1];
}

UIntPtr Class_Microsoft_Singularity_Processor::g_GetFrameEbp(UIntPtr ebp)
{
    if (ebp < (UIntPtr)0x10000) {
        return 0;
    }
    return ((UIntPtr*)ebp)[0];
}

__declspec(naked)
UIntPtr Class_Microsoft_Singularity_Processor::g_GetStackPointer()
{
    __asm {
        mov eax, esp;
        ret;
    }
}

__declspec(naked)
UIntPtr Class_Microsoft_Singularity_Processor::g_GetFramePointer()
{
    __asm {
        mov eax, ebp;
        ret;
    }
}

__declspec(naked)
Struct_Microsoft_Singularity_X86_ProcessorContext *
Class_Microsoft_Singularity_Processor::g_GetCurrentProcessorContext()
{
    __asm {
        mov eax, fs:[0]Struct_Microsoft_Singularity_X86_ProcessorContext.processorContext;
        ret;
    }
}
#endif // !ZERO_RUNTIME

__declspec(naked)
Struct_Microsoft_Singularity_X86_ThreadContext *
Class_Microsoft_Singularity_Processor::g_GetCurrentThreadContext()
{
    __asm {
        mov eax, fs:[0]Struct_Microsoft_Singularity_X86_ProcessorContext.threadContext;
        ret;
    }
}

__declspec(naked)
Class_System_Threading_Thread *
Class_Microsoft_Singularity_Processor::g_GetCurrentThread()
{
    __asm {
        mov edx, fs:[0]Struct_Microsoft_Singularity_X86_ProcessorContext.threadContext;
        mov eax, [edx]Struct_Microsoft_Singularity_X86_ThreadContext._thread;
        ret;
    }
#if 0
    Struct_Microsoft_Singularity_X86_ProcessorContext * proc;
    __asm {
        mov eax, fs:[0]Struct_Microsoft_Singularity_X86_ProcessorContext.processorContext;
        mov proc, eax;
    }
    return proc->threadContext->_thread;
#endif
}

#if SINGULARITY_KERNEL

__declspec(naked)
void Class_Microsoft_Singularity_Processor::
g_SetCurrentThreadContext(Struct_Microsoft_Singularity_X86_ThreadContext * context)
{
    __asm {
        mov fs:[0]Struct_Microsoft_Singularity_X86_ProcessorContext.threadContext, ecx;
        ret;
    }
}

#endif // SINGULARITY_KERNEL

#if SINGULARITY_KERNEL
#if DO_FXSAVE_TEST
#pragma warning(disable:4733) // We'll touch fs:[0] if we want to.
int Class_Microsoft_Singularity_Processor::g_TestFxsave()
{
    uint64 beg;
    uint64 end;
    int loops = 1000000;

    Struct_Microsoft_Singularity_X86_ThreadContext * context =
        g_GetCurrentThreadContext();
    Struct_Microsoft_Singularity_X86_MmxContext *fxregs = &context->mmx;
    int fs0;

    bool fEnabled = g_DisableInterrupts();

    beg = RDTSC();
    while (loops-- > 0) {
        __asm {
            mov eax, fxregs;
            fxsave [eax];
            fxrstor [eax];
        }
    }
    end = RDTSC();

    g_RestoreInterrupts(fEnabled);

    printf("value of fs:[0]: %08x\n", fs0);
    printf("elapsed: %d\n", (int)(end - beg));
    return ((int)((end - beg) / 1000000));
}
#endif DO_FS0_TEST

#if DO_FS0_TEST
#pragma warning(disable:4733) // We'll touch fs:[0] if we want to.
int Class_Microsoft_Singularity_Processor::g_TestFs0()
{
    uint64 beg;
    uint64 end;
    int loops = 1000000;
    void * stackLimit = (void *)0x800000;
    void * threadContext = &stackLimit;
    void * processorContextThread = &threadContext;
    int fs0;

    bool fEnabled = g_DisableInterrupts();

    __asm {
        mov eax, processorContextThread;
        mov fs:[0], eax;
    }

    beg = RDTSC();
    while (loops-- > 0) {
        __asm {
            mov eax, fs:[0];
            mov eax, [eax];
            add eax, 0x100;
            cmp eax, esp;
            mov fs0, eax;
        }
    }
    end = RDTSC();

    g_RestoreInterrupts(fEnabled);

    printf("value of fs:[0]: %08x\n", fs0);
    printf("elapsed: %d\n", (int)(end - beg));
    return ((int)((end - beg) / 1000000));
}
#endif DO_FS0_TEST

#if DO_CLI_STI_TEST
int Class_Microsoft_Singularity_Processor::g_TestCliSti()
{
    uint64 beg;
    uint64 end;
    int loops = 1000000;

    bool fEnabled = g_DisableInterrupts();

    g_nIgnoredHardwareInterrupts = 0;

    __asm sti;
    beg = RDTSC();
    while (loops-- > 0) {
        __asm {
            cli;    // 1
            sti;
            cli;    // 2
            sti;
            cli;    // 3
            sti;
            cli;    // 4
            sti;
            cli;    // 5
            sti;
            cli;    // 6
            sti;
            cli;    // 7
            sti;
            cli;    // 8
            sti;
            cli;    // 9
            sti;
            cli;    // 10
            sti;
        }
    }
    end = RDTSC();
    __asm cli;

    printf("Ignored %d hardware interrupts.\n", g_nIgnoredHardwareInterrupts);

    g_RestoreInterrupts(fEnabled);

    return ((int)((end - beg) / 1000000)) / 10;
}
#endif // DO_CLI_STI_TEST
#endif // SINGULARITY_KERNEL

#if SLOW_INTERRUPT_SAVE_RESTORE

static uint32 dstack[8];
static uint64 disabled = 0;
static uint64 restored = 0;

bool Class_Microsoft_Singularity_Processor::g_DisableInterrupts()
{
    uint32 eflags;

    __asm {
        pushfd;
        pop eax;
        mov eflags, eax;
        cli;
    }
#if 0
    if ((eflags & Struct_Microsoft_Singularity_X86_EFlags_IF) != 0) {
        uint32 _ebp;
        __asm {
            mov _ebp, ebp;
        }
        for (int i = 0; i < arrayof(dstack); i++) {
            if (_ebp != 0) {
                dstack[i] = ((uint32*)_ebp)[1];
                _ebp = ((uint32*)_ebp)[0];
            }
            else {
                dstack[i] = 0;
            }
        }
        disabled = g_GetCycleCount();
    }
#endif // 0
    return (eflags & Struct_Microsoft_Singularity_X86_EFlags_IF) != 0;
}

void Class_Microsoft_Singularity_Processor::g_RestoreInterrupts(bool enabled)
{
    if (enabled) {
#if 0
        restored = g_GetCycleCount();
        if (restored - disabled > 10000000000 && disabled != 0) {
            __asm int 3;
        }
#endif // 0
        __asm sti;
    }
}

#else // SLOW_INTERRUPT_SAVE_RESTORE

__declspec(naked)
bool Class_Microsoft_Singularity_Processor::g_DisableInterrupts()
{
    __asm {
        pushfd;
        pop eax;
        test eax, Struct_Microsoft_Singularity_X86_EFlags_IF;
        setnz al;
        cli;
        ret;
    }
}

__declspec(naked)
void Class_Microsoft_Singularity_Processor::g_RestoreInterrupts(bool enabled)
{
    __asm {
        test cl, cl;
        je done;
        sti;
      done:
        ret;
    }
}

#endif // SLOW_INTERRUPT_SAVE_RESTORE

__declspec(naked)
bool Class_Microsoft_Singularity_Processor::g_InterruptsDisabled()
{
    __asm {
        pushfd;
        pop eax;
        test eax, Struct_Microsoft_Singularity_X86_EFlags_IF;
        setz al;
        ret;
    }
}

__declspec(naked) void Class_Microsoft_Singularity_Processor::g_EnterRing3()
{
//    int uc3 = SEGMENT_SELECTOR(GdtUC) + 3;
//    int ud3 = SEGMENT_SELECTOR(GdtUD) + 3;
//    int uf3 = SEGMENT_SELECTOR(GdtPF) + 3; // for the moment, share UF and PF

// TODO: get rid of hexadecimal constants below

    // warning: preserve return values eax, edx (for returning from ABI)
    __asm {
        push edx
        mov ecx, esp
        mov edx, ring3
        _emit   0x0f;
        _emit   0x35;  //sysexit
      ring3:
        pop edx
        mov cx, ss
        mov ds, cx
        mov es, cx
        mov ecx, 0x38 + 3 // SEGMENT_SELECTOR(GdtPF) + 3
        mov fs, cx
        ret
    }
}

#if DONT_TRY_THIS_YET
__declspec(naked) bool Class_Microsoft_Singularity_Processor::g_AtKernelPrivilege()
{
    // The bottom two bits of the CS selector are the RPL
    // (requested privilege level) of the selector. If
    // this is zero, we're running at ring0. Otherwise,
    // we're less privileged.
    __asm {
        mov ax, cs;
        test ax, 3;
        setnz al; // or setz al;
        ret;
    }
}
#else
bool Class_Microsoft_Singularity_Processor::g_AtKernelPrivilege()
{
    uint16 _cs;

    __asm{
        mov _cs, cs
    }

    // The bottom two bits of the CS selector are the RPL
    // (requested privilege level) of the selector. If
    // this is zero, we're running at ring0. Otherwise,
    // we're less privileged.
    return (_cs & 0x3) == 0;
}
#endif

//////////////////////////////////////////////////////////////////////////////
//
#if SINGULARITY_KERNEL
void DumpContext(Struct_Microsoft_Singularity_X86_ThreadContext *context)
{
    printf("    thr=%8x, prv=%8x, nxt=%8x, ctx=%08x\n",
           context->_thread, context->prev, context->next, context);
    printf("    num=%8x, err=%8x, cr2=%8x\n",
           context->num, context->err, context->cr2);
    printf("    eax=%8x, ebx=%8x, ecx=%8x, edx=%8x\n",
           context->eax, context->ebx, context->ecx, context->edx);
    printf("    esp=%8x, ebp=%8x, esi=%8x, edi=%8x\n",
           context->esp, context->ebp, context->esi, context->edi);
    printf("    efl=%8x, eip=%8x, beg=%8x, lim=%8x\n",
           context->efl, context->eip, context->stackBegin, context->stackLimit);
    printf("    cs0=%8x\n", context->cs0);

#if 0
    uint32 _cs;
    uint32 _ss;
    uint32 _ds;
    uint32 _es;
    uint32 _fs;
    uint32 _gs;
    uint32 _esp;
    __asm {
        push cs;
        pop  _cs;
        push ss;
        pop  _ss;
        push ds;
        pop  _ds;
        push es;
        pop  _es;
        push fs;
        pop  _fs;
        push gs;
        pop  _gs;
        push esp;
        pop  _esp;
    }

    printf("    cs=%04x, ss=%04x, ds=%04x, es=%04x, fs=%04x, gs=%04x, esp=%08x\n",
           _cs, _ss, _ds, _es, _fs, _gs, _esp);
#endif
}

void LimitedDispatchException(int interrupt, Struct_Microsoft_Singularity_X86_ThreadContext *context)
{
#if 0
    Struct_Microsoft_Singularity_X86_ProcessorContext * proc;
    __asm {
        mov eax, fs:[0]Struct_Microsoft_Singularity_X86_ProcessorContext.processorContext;
        mov proc, eax;
    }
#endif
    if (context->num == 0x2E) {
        printf("SYSCALL!!\n");
        DumpContext(context);
        return;
    }
    if (context->num == 0x2F) {
        printf("SYSENTER!!\n");
        DumpContext(context);
        return;
    }
    if (context->num > 0x20) {
        g_nIgnoredHardwareInterrupts++;
        return;
    }

#if 1
    printf("-- Exception 0x%02x -------------------------------------------\n",
           interrupt);
    DumpContext(context);
#endif // 1

    printf("Entering debugger stub...\n");
    printf("[= Exception: %02x eip=%08x efl=%08x ==\n",
           interrupt, context->eip, context->efl);

    Class_Microsoft_Singularity_DebugStub::g_Trap(context, false);
}

void Class_Microsoft_Singularity_Processor::g_ClearIdtTable()
{
    printf("Clearing C# IDT Table.\n");
    c_exceptionHandler = LimitedDispatchException;
    c_interruptHandler = LimitedDispatchException;
}

void Class_Microsoft_Singularity_Processor::g_SetIdtTable()
{
    printf("Setting C# IDT Table.\n");
    c_exceptionHandler = Class_Microsoft_Singularity_Processor::g_DispatchException;
    c_interruptHandler = Class_Microsoft_Singularity_Processor::g_DispatchInterrupt;
}

extern "C" void __cdecl EdtEnter0(void);
extern "C" void __cdecl EdtEnter1(void);
extern "C" void __cdecl IdtEnter20(void);
extern "C" void __cdecl IdtEnter21(void);
extern "C" void __cdecl SysEnter(void);
extern void FakeSyscall();

void IdtInitialize()
{
    // Configure the simplest IDT Handler.
    Class_Microsoft_Singularity_Processor::g_ClearIdtTable();

    // Create the IDT Entry Table, first set the exception entries.
    uint32 entry = (uint32)EdtEnter0;
    uint32 offset = ((uint32)EdtEnter1) - ((uint32)EdtEnter0);

    for (int i = 0; i < 0x20; i++) {
#if DOUBLE_FAULT_HANDLER
        if (i == Struct_Microsoft_Singularity_X86_EVectors_DoubleFault) {
            // The double fault handler uses a task gate to set up stack.

            g_idtEntries[i].offset_0_15 = 0;
            g_idtEntries[i].offset_16_31 = 0;
            g_idtEntries[i].selector = SEGMENT_SELECTOR(GdtDF);
            g_idtEntries[i].access =
                (Struct_Microsoft_Singularity_X86_IDTE_PRESENT |
                 Struct_Microsoft_Singularity_X86_IDTE_DPL_RING0 |  // RING0 for hardware ints.
                 Struct_Microsoft_Singularity_X86_IDTE_TASK_GATE);


            uint32 * pi = (uint32 *)&g_idtEntries[i];
            printf("idt[0x%02x] = %08x %08x\n", i, pi[0], pi[1]);
        }
        else {
#endif
            g_idtEntries[i].offset_0_15 = (uint16)entry;
            g_idtEntries[i].selector = SEGMENT_SELECTOR(GdtPC);
            g_idtEntries[i].access =
                (Struct_Microsoft_Singularity_X86_IDTE_PRESENT |
                 Struct_Microsoft_Singularity_X86_IDTE_DPL_RING3 |  // RING0 for hardware ints.
                 Struct_Microsoft_Singularity_X86_IDTE_INT_GATE);
            g_idtEntries[i].offset_16_31 = (uint16)(entry >> 16);
#if DOUBLE_FAULT_HANDLER
        }
#endif
        entry += offset;
    }

    // Now set the interrupt entries.
    entry = (uint32)IdtEnter20;
    offset = ((uint32)IdtEnter21) - ((uint32)IdtEnter20);

    for (int i = 0x20; i < arrayof(g_idtEntries); i++) {
        g_idtEntries[i].offset_0_15 = (uint16)entry;
        g_idtEntries[i].selector = SEGMENT_SELECTOR(GdtPC);
        g_idtEntries[i].access =
            (Struct_Microsoft_Singularity_X86_IDTE_PRESENT |
             Struct_Microsoft_Singularity_X86_IDTE_DPL_RING3 |
             Struct_Microsoft_Singularity_X86_IDTE_INT_GATE);
        g_idtEntries[i].offset_16_31 = (uint16)(entry >> 16);
        entry += offset;
    }

    g_idt.limit = sizeof(g_idtEntries);
    g_idt.addr = (uintptr)g_idtEntries;
}

void IdtLoad()
{
    __asm {
        lidt g_idt.limit;
    }
}

void ProcessorInitialize(const Struct_Microsoft_Singularity_CpuInfo *pCpuInfo,
                         int   cpuId)
{
    Struct_Microsoft_Singularity_X86_ProcessorContext *proc
        = (Struct_Microsoft_Singularity_X86_ProcessorContext *)pCpuInfo->Fs32;

    proc->cpuId = cpuId;
    // Set up a default environment.
    proc->processorContext = proc;
    proc->threadContext = &proc->thirdContext;  // Insure valid context early in boot.
    proc->threadContext->_thread = (Class_System_Threading_Thread *)pCpuInfo->Gs32;
    proc->threadContext->stackLimit = 0;
    proc->threadContext->stackBegin = 0;

    // Make sure we have a viable exception stack for early debugging.
    // Note that this stack is only used for early debugging (i.e before Kernel.cs runs).
    proc->exceptionStackBegin
        = Struct_Microsoft_Singularity_BootInfo_KERNEL_STACK_BEGIN + 0x2000;
    proc->exceptionStackLimit
        = Struct_Microsoft_Singularity_BootInfo_KERNEL_STACK_BEGIN;

#if PAGING
    // XXX Set up MSRs for SYSENTER/SYSEXIT
    Class_Microsoft_Singularity_Processor::g_WriteMsr(0x174, SEGMENT_SELECTOR(GdtPC));
    Class_Microsoft_Singularity_Processor::g_WriteMsr(0x175, proc->exceptionStackBegin + 0x2000);
#if !PAGING
    Class_Microsoft_Singularity_Processor::g_WriteMsr(0x176, (UINT64)SysEnter);
#else
    Class_Microsoft_Singularity_Processor::g_WriteMsr(0x176, (UINT64)FakeSyscall);
#endif
#endif
}

void Class_Microsoft_Singularity_Processor::g_PrivateEnablePaging(uint32 pdpt)
{
    __asm {
        // use the pdpt argument directly as the cr3 value. This leaves
        // top-level write-through and cache-disable turned off.
        mov     eax, pdpt;
        mov     cr3, eax;

        // Turn on paging and write protection
        mov     eax, cr0;
        or      eax, Struct_Microsoft_Singularity_X86_CR0_PG + Struct_Microsoft_Singularity_X86_CR0_WP;
        mov     cr0, eax;

        jmp     reload_tlb;
        ALIGN   0x010;

reload_tlb:
    }
}

void Class_Microsoft_Singularity_Processor::g_PrivateChangeAddressSpace(uint32 pdpt)
{
    __asm {
        // use the pdpt argument directly as the cr3 value. This leaves
        // top-level write-through and cache-disable turned off.
        mov     eax, pdpt;
        mov     cr3, eax;

        jmp     reload_tlb;
        ALIGN   0x010;

reload_tlb:
    }
}

__declspec(naked)
void Class_Microsoft_Singularity_Processor::g_DisablePaging()
{
    __asm {
        // Turn off paging.
        mov     eax, cr0;
        and     eax, NOT (Struct_Microsoft_Singularity_X86_CR0_PG + Struct_Microsoft_Singularity_X86_CR0_WP);
        mov     cr0, eax;

        // Flush and reset the TLB.
        mov     eax,0;
        mov     cr3,eax;

        jmp     reload_tlb;
        ALIGN   0x010;

reload_tlb:
    }
}

void Class_Microsoft_Singularity_Processor::g_PrivateInvalidateTLBEntry(UIntPtr pageAddr)
{
    __asm {
        mov      eax, pageAddr;
        invlpg   [eax];
    }
}

__declspec(naked)
uint32 Class_Microsoft_Singularity_Processor::g_GetCr3()
{
    __asm {
        mov      eax, cr3;
        ret;
    }
}

// haryadi
void Class_Microsoft_Singularity_Processor::g_MpCallEntryPoint(UIntPtr entry)
{
    __asm {
        mov eax, entry;
        call eax;
    }
}

#endif // SINGULARITY_KERNEL

//
///////////////////////////////////////////////////////////////// End of File.
