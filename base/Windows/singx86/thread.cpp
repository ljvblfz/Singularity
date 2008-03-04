/////////////////////////////////////////////////////////////////////////////
//
//  thread.cpp - Extension to select a Singularity thread.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
#include "singx86.h"

struct GDTE
{
    USHORT     limit;
    USHORT     base0_15;
    BYTE       base16_23;
    BYTE       access;
    BYTE       granularity;
    BYTE       base24_31;
};

static void Usage()
{
    ExtOut("Usage:\n"
           "    !thread {options} thread_addr\n"
           "Options:\n"
           "    -p show stack prior to interrupt or exception (if any)\n"
           "    -t show top stack (i.e. interrupt or exception (if any))\n"
           "Notes:\n"
           "    There are potentially three contexts associated with a thread:\n"
           "    the scheduler context, the interrupt context, and the exception\n"
           "    context.  The -p flag is only useful if an exception or interrupt\n"
           "    has occurred.  Using -p -p always gets the scheduler context.");
}

static PCSTR ParseArguments(PCSTR args,
                            OUT PLONG plPriorStack,
                            OUT PLONG plTopStack)
{
    *plPriorStack = 0;
    *plTopStack = 0;
    while (*args != '\0') {
        while (*args == ' ' || *args == '\t') {
            args++;
        }
        if (*args != '-' && *args != '/') {
            break;
        }
        args++;
        switch (*args++) {
            case 'p':
              *plPriorStack = *plPriorStack + 1;
              break;
            case 't':
              *plTopStack = 1;
              break;
            default:
              Usage();
              return NULL;
        }
    }
    return args;
}

EXT_DECL(thread) // Defines: PDEBUG_CLIENT Client, PCSTR args
{
    EXT_ENTER();    // Defines: HRESULT status = S_OK;

    Thread thread;
    ThreadContext tc;

    LONG priorStack = 0;
    LONG topStack = 0;
    ULONG64 address = 0;
    PVOID extendedRegs = NULL;
    PVOID floatingRegs = NULL;

    if ((args = ParseArguments(args, &priorStack, &topStack)) == NULL) {
        status = S_FALSE;
        goto Exit;
    }

    status = ExtEvalU64(&args, &address);
    if (status != S_OK) {
        ULONG indexGdtr = 0;
        ULONG indexGdtl = 0;
        ULONG indexFs = 0;
        EXT_CHECK(g_ExtRegisters->GetIndexByName("gdtr", &indexGdtr));
        EXT_CHECK(g_ExtRegisters->GetIndexByName("gdtl", &indexGdtl));
        EXT_CHECK(g_ExtRegisters->GetIndexByName("fs", &indexFs));

        ExtVerb("gdtr: %04x\n", indexGdtr);
        ExtVerb("gdtl: %04x\n", indexGdtl);
        ExtVerb("fs: %04x\n", indexFs);

        ULONG64 gdtr;
        ULONG64 gdtl;
        ULONG64 fs;

        DEBUG_VALUE v;
        EXT_CHECK(g_ExtRegisters->GetValue(indexGdtr, &v));
        ExtVerb("gdtr: %04x -> %p\n", indexGdtr, v.I64);
        gdtr = v.I64;
        EXT_CHECK(g_ExtRegisters->GetValue(indexGdtl, &v));
        gdtl = v.I64;
        ExtVerb("gdtl: %04x -> %p\n", indexGdtl, v.I64);
        EXT_CHECK(g_ExtRegisters->GetValue(indexFs, &v));
        fs = v.I64;
        ExtVerb("fs: %04x -> %p\n", indexFs, v.I64);

        GDTE fse;
        ZeroMemory(&fse, sizeof(fse));
        EXT_CHECK(g_ExtData->ReadVirtual(gdtr + fs, &fse, sizeof(fse), NULL));
        ULONG64 fs0 = ((ULONG64)fse.base0_15 +
                       ((ULONG64)fse.base16_23 << 16) |
                       ((ULONG64)fse.base24_31 << 24));
        ExtVerb("fs[0] = %p\n", fs0);

        ProcessorContext pc;
        EXT_CHECK(ProcessorContextStruct.Read(fs0, &pc));

        ExtVerb("  threadContext:        %p\n", pc.threadContext);
        ExtVerb("  _processor:           %p\n", pc._processor);
        ExtVerb("  exception:            %p\n", pc.exception);
        ExtVerb("  cpuId:                %p\n", pc.cpuId);
        ExtOut("%d: Stacks: Int: %p..%p, Exc: %p..%p, Sch: %p..%p\n",
               (int)pc.cpuId,
               pc.interruptStackLimit, pc.interruptStackBegin - 1,
               pc.exceptionStackLimit, pc.exceptionStackBegin - 1,
               pc.schedulerStackLimit, pc.schedulerStackBegin - 1);

        ThreadContext tc;
        EXT_CHECK(ThreadContextStruct.Read(pc.threadContext, &tc));

        address = tc._thread;
    }

    if (address == 0) {
        ExtErr("Null is invalid thread address.\n");
        return S_FALSE;
    }

    EXT_CHECK(ThreadStruct.Read(address, &thread));

    ULONG64 caddress[3];
    int currentStack = 0;
    caddress[0] = address + ThreadFields[9].offset;

    if (thread.context.next) {
        caddress[1] = thread.context.next;
        currentStack++;
        EXT_CHECK(ThreadContextStruct.Read(caddress[1], &tc));
        if (tc.next) {
            caddress[2] = tc.next;
            EXT_CHECK(ThreadContextStruct.Read(caddress[2], &tc));
            currentStack++;
        }
    }

    if (topStack == 0) {
        currentStack = 0;
    }
    else {
        if (priorStack != 0) {
            currentStack -= priorStack;
            if (currentStack < 0) {
                currentStack = 0;
            }
        }
    }

    EXT_CHECK(ThreadContextStruct.Read(caddress[currentStack], &tc));
    EXT_CHECK(ThreadContextStruct.RawAccess(ThreadContextFields[0].offset, &floatingRegs));
    EXT_CHECK(ThreadContextStruct.RawAccess(ThreadContextFields[8].offset, &extendedRegs));

    ExtOut("  %p { tid=%03x pid=%03x eip=%p, ebp=%p, esp=%p %02x }\n",
           address,
           (ULONG)(tc.threadIndex == 0xffff ? 0xfff : tc.threadIndex),
           (ULONG)(tc.processId == 0xffff ? 0xfff : tc.processId),
           tc.eip,
           tc.ebp,
           tc.esp,
           (ULONG)tc.gcStates);

    CONTEXT context;

    EXT_CHECK(g_ExtSymbols->GetScope(NULL, NULL, &context, sizeof(context)));
    context.Eip = (ULONG)tc.eip;
    context.Esp = (ULONG)tc.esp;
    context.Ebp = (ULONG)tc.ebp;
    context.Eax = (ULONG)tc.eax;
    context.Ebx = (ULONG)tc.ebx;
    context.Ecx = (ULONG)tc.ecx;
    context.Edx = (ULONG)tc.edx;
    context.Esi = (ULONG)tc.esi;
    context.Edi = (ULONG)tc.edi;
    context.EFlags = (ULONG)tc.efl;

    // CONTEXT_FLOATING_POINT
    context.FloatSave.ControlWord = (ULONG)tc.mmx.fcw;
    context.FloatSave.StatusWord = (ULONG)tc.mmx.fsw;
    context.FloatSave.TagWord = (ULONG)tc.mmx.ftw;
    context.FloatSave.ErrorOffset = (ULONG)tc.mmx.cs;
    context.FloatSave.ErrorSelector = (ULONG)tc.mmx.eip;
    context.FloatSave.DataOffset = (ULONG)tc.mmx.ds;
    context.FloatSave.DataSelector = (ULONG)tc.mmx.dp;

    memcpy((PBYTE)context.FloatSave.RegisterArea+0, (PBYTE)floatingRegs + 0x00, 10);
    memcpy((PBYTE)context.FloatSave.RegisterArea+10, (PBYTE)floatingRegs + 0x10, 10);
    memcpy((PBYTE)context.FloatSave.RegisterArea+20, (PBYTE)floatingRegs + 0x20, 10);
    memcpy((PBYTE)context.FloatSave.RegisterArea+30, (PBYTE)floatingRegs + 0x30, 10);
    memcpy((PBYTE)context.FloatSave.RegisterArea+40, (PBYTE)floatingRegs + 0x40, 10);
    memcpy((PBYTE)context.FloatSave.RegisterArea+50, (PBYTE)floatingRegs + 0x50, 10);
    memcpy((PBYTE)context.FloatSave.RegisterArea+60, (PBYTE)floatingRegs + 0x60, 10);
    memcpy((PBYTE)context.FloatSave.RegisterArea+70, (PBYTE)floatingRegs + 0x70, 10);
    memcpy(context.ExtendedRegisters, extendedRegs, 512);

    EXT_CHECK(g_ExtSymbols->SetScope(0, NULL, &context, sizeof(context)));
    EXT_LEAVE();    // Macro includes: return status;
}
