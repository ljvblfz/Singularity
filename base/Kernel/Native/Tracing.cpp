////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Tracing.cpp
//
//  Note:
//

#include "hal.h"

#if SINGULARITY_KERNEL
static int64 tscOffsets[8];
#endif

//////////////////////////////////////////////////////////////// Image Loader.
//

void
Class_Microsoft_Singularity_Tracing::
g_Initialize()
{
#if SINGULARITY_KERNEL
    c_txtBegin = (uint8 *)Struct_Microsoft_Singularity_BootInfo_KERNEL_LOGTXT_BEGIN;
    c_txtLimit = (uint8 *)Struct_Microsoft_Singularity_BootInfo_KERNEL_LOGTXT_LIMIT;
    c_txtHead = c_txtBegin;
    c_ptxtHead = &c_txtHead;

    int cnt = (Struct_Microsoft_Singularity_BootInfo_KERNEL_LOGREC_LIMIT -
               Struct_Microsoft_Singularity_BootInfo_KERNEL_LOGREC_BEGIN)
        / sizeof(Struct_Microsoft_Singularity_Tracing_LogEntry);

    c_logBegin   = (Struct_Microsoft_Singularity_Tracing_LogEntry *)
        Struct_Microsoft_Singularity_BootInfo_KERNEL_LOGREC_BEGIN;
    c_logLimit   = c_logBegin + cnt;
    c_logHead    = c_logBegin;
    c_plogHead   = &c_logHead;
    c_tscOffsets = tscOffsets;
#elif SINGULARITY_PROCESS
    Struct_Microsoft_Singularity_Tracing_LogEntry * logBegin;
    Struct_Microsoft_Singularity_Tracing_LogEntry * logLimit;
    Struct_Microsoft_Singularity_Tracing_LogEntry ** plogHead;
    uint8 * txtBegin;
    uint8 * txtLimit;
    uint8 ** ptxtHead;

    Struct_Microsoft_Singularity_V1_Services_ProcessService::
        g_GetTracingHeaders((Struct_Microsoft_Singularity_V1_Services_LogEntry **)&logBegin,
                            (Struct_Microsoft_Singularity_V1_Services_LogEntry **)&logLimit,
                            (Struct_Microsoft_Singularity_V1_Services_LogEntry ***)&plogHead,
                            &txtBegin,
                            &txtLimit,
                            &ptxtHead);

    c_logBegin = logBegin;
    c_logLimit = logLimit;
    c_plogHead = plogHead;

    c_txtBegin = txtBegin;
    c_txtLimit = txtLimit;
    c_ptxtHead = ptxtHead;
#else
#error "File should be compiled with SINGULARITY_KERNEL or SINGULARITY_PROCESS"
#endif
}

void
Class_Microsoft_Singularity_Tracing::
g_Finalize()
{
}

#if SINGULARITY_KERNEL
void
Class_Microsoft_Singularity_Tracing::
g_SetTscOffset(int64 theTscOffset)
{
    Struct_Microsoft_Singularity_X86_ProcessorContext *processorContext =
        Class_Microsoft_Singularity_Processor::g_GetCurrentProcessorContext();
    int cpuId = processorContext->cpuId;
    if (cpuId < 0 || cpuId >= sizeof(tscOffsets)/sizeof(tscOffsets[0])) {
        __asm int 3
    }
    tscOffsets[cpuId] = theTscOffset;
}
#endif // SINGULARITY_KERNEL

uint8 * CompareExchange(uint8 **dest, uint8 *exch, uint8 *comp)
{
    uint8 *val;
    __asm {
        mov ecx, dest;
        mov edx, exch;
        mov eax, comp;
        lock cmpxchg [ecx], edx;
        mov val, eax;
    }
    return val;
}

Struct_Microsoft_Singularity_Tracing_LogEntry *
CompareExchange(Struct_Microsoft_Singularity_Tracing_LogEntry **dest,
                Struct_Microsoft_Singularity_Tracing_LogEntry *exch,
                Struct_Microsoft_Singularity_Tracing_LogEntry *comp)
{
    Struct_Microsoft_Singularity_Tracing_LogEntry * val;
    __asm {
        mov ecx, dest;
        mov edx, exch;
        mov eax, comp;
        lock cmpxchg [ecx], edx;
        mov val, eax;
    }
    return val;
}

Struct_Microsoft_Singularity_Tracing_LogEntry *
Class_Microsoft_Singularity_Tracing::
g_CreateLog(uint8 severity, UIntPtr eip, int chars, uint8 **buf)
{
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    Struct_Microsoft_Singularity_Tracing_LogEntry * logWas;
    Struct_Microsoft_Singularity_Tracing_LogEntry * logBec;

    do {
        log = *c_plogHead;
        logWas = log;
        logBec = logWas + 1;
        if (logBec >= c_logLimit) {
            logBec = c_logBegin;
        }
    } while (logWas != CompareExchange(c_plogHead, logBec, logWas));

    uint8 *str;
    uint8 *txtWas;
    uint8 *txtBec;

    do {
        str = *c_ptxtHead;
        txtWas = str;
        txtBec = str + chars;

        if (txtBec > c_txtLimit) {
            str = c_txtBegin;
            txtBec = str + chars;
        }
    } while (txtWas != CompareExchange(c_ptxtHead, txtBec, txtWas));

#if SINGULARITY_KERNEL || !PAGING
    bool enabled = Class_Microsoft_Singularity_Processor::g_DisableInterrupts();
#endif

    // We need acquiring a log entry to be
    // wait-free since this routine can be called from the
    // kernel or a userland application.  We cannot simply
    // disable interrupts as there may be multiple hardware
    // threads and it might result in a userland thread blocking
    // a kernel thread.  Callers may be context switched acquiring
    // the log entry.  This means there is finite chance that
    // the log entries do not have monotonically increasing tsc
    // values.  This is sometimes visible in the !log output.
    UINT64 tsc = RDTSC();

    Struct_Microsoft_Singularity_X86_ThreadContext *threadContext =
        Class_Microsoft_Singularity_Processor::g_GetCurrentThreadContext();
    Struct_Microsoft_Singularity_X86_ProcessorContext *processorContext =
        Class_Microsoft_Singularity_Processor::g_GetCurrentProcessorContext();

    log->processId = threadContext->processId;

#if SINGULARITY_KERNEL
    log->threadId = threadContext->threadIndex;
#else
    log->threadId = threadContext->kernelThreadIndex;
#endif

    log->eip        = (uintptr)eip;     //(uintptr)g_GetCallerEip(3);
    log->text       = str;
    log->severity   = (uint8)severity;
    log->strings    = 0;
    log->tag        = 0;
    log->cycleCount = tsc;
    log->cpuId      = processorContext->cpuId;

    *str++ = (uint8)log->cycleCount;
    *str = '\0';

#if SINGULARITY_KERNEL || !PAGING
    Class_Microsoft_Singularity_Processor::g_RestoreInterrupts(enabled);
#endif

    *buf = str;

    return log;
}

uint8 * Class_Microsoft_Singularity_Tracing::
g_AddText(uint8 *dst, Class_System_String *arg)
{
    if (arg != NULL) {
        bartok_char *src = &arg->m_firstChar;
        bartok_char *end = src + arg->m_stringLength;

        while (src < end) {
            *dst++ = (uint8)*src++;
        }
    }
    *dst++ = '\0';

    return dst;
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    log = g_CreateLog(severity, _eip, 2, &text);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    text = g_AddText(text, msg);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      UIntPtr arg0)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->arg0 = (uintptr)arg0;
    text = g_AddText(text, msg);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      UIntPtr arg0, UIntPtr arg1)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->arg0 = (uintptr)arg0;
    log->arg1 = (uintptr)arg1;
    text = g_AddText(text, msg);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      UIntPtr arg0, UIntPtr arg1, UIntPtr arg2)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->arg0 = (uintptr)arg0;
    log->arg1 = (uintptr)arg1;
    log->arg2 = (uintptr)arg2;
    text = g_AddText(text, msg);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      UIntPtr arg0, UIntPtr arg1, UIntPtr arg2,
      UIntPtr arg3)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->arg0 = (uintptr)arg0;
    log->arg1 = (uintptr)arg1;
    log->arg2 = (uintptr)arg2;
    log->arg3 = (uintptr)arg3;
    text = g_AddText(text, msg);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      UIntPtr arg0, UIntPtr arg1, UIntPtr arg2,
      UIntPtr arg3, UIntPtr arg4)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->arg0 = (uintptr)arg0;
    log->arg1 = (uintptr)arg1;
    log->arg2 = (uintptr)arg2;
    log->arg3 = (uintptr)arg3;
    log->arg4 = (uintptr)arg4;
    text = g_AddText(text, msg);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      UIntPtr arg0, UIntPtr arg1, UIntPtr arg2,
      UIntPtr arg3, UIntPtr arg4, UIntPtr arg5)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->arg0 = (uintptr)arg0;
    log->arg1 = (uintptr)arg1;
    log->arg2 = (uintptr)arg2;
    log->arg3 = (uintptr)arg3;
    log->arg4 = (uintptr)arg4;
    log->arg5 = (uintptr)arg5;
    text = g_AddText(text, msg);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      Class_System_String *arg0)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1 +
        (arg0 != NULL ? arg0->m_stringLength : 0) + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->strings = Struct_Microsoft_Singularity_Tracing_Strings_String0;
    text = g_AddText(text, msg);
    text = g_AddText(text, arg0);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      Class_System_String *arg0,
      UIntPtr arg1)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1 +
        (arg0 != NULL ? arg0->m_stringLength : 0) + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->strings = Struct_Microsoft_Singularity_Tracing_Strings_String0;
    log->arg1 = (uintptr)arg1;
    text = g_AddText(text, msg);
    text = g_AddText(text, arg0);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      Class_System_String *arg0,
      UIntPtr arg1, UIntPtr arg2)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1 +
        (arg0 != NULL ? arg0->m_stringLength : 0) + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->strings = Struct_Microsoft_Singularity_Tracing_Strings_String0;
    log->arg1 = (uintptr)arg1;
    log->arg2 = (uintptr)arg2;
    text = g_AddText(text, msg);
    text = g_AddText(text, arg0);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      Class_System_String *arg0,
      UIntPtr arg1, UIntPtr arg2, UIntPtr arg3)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + msg->m_stringLength + 1 +
        (arg0 != NULL ? arg0->m_stringLength : 0) + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->strings = Struct_Microsoft_Singularity_Tracing_Strings_String0;
    log->arg1 = (uintptr)arg1;
    log->arg2 = (uintptr)arg2;
    log->arg3 = (uintptr)arg3;
    text = g_AddText(text, msg);
    text = g_AddText(text, arg0);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      Class_System_String *msg,
      Class_System_String *arg0,
      Class_System_String *arg1)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;

    int chars = 1 + msg->m_stringLength + 1
        + (arg0 != NULL ? arg0->m_stringLength : 0) + 1
        + (arg1 != NULL ? arg0->m_stringLength : 0) + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->strings = (Struct_Microsoft_Singularity_Tracing_Strings_String0 |
                    Struct_Microsoft_Singularity_Tracing_Strings_String1);
    text = g_AddText(text, msg);
    text = g_AddText(text, arg0);
    text = g_AddText(text, arg1);
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      char * msg)
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    char *txt = msg;
    while (*txt) {
        txt++;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + txt - msg + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    while (*msg != '\0') {
        *text++ = *msg++;
    }
    *text++ = '\0';
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      char * msg,
      Class_System_String *arg0,
      UIntPtr arg1
      )
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    char *txt = msg;
    while (*txt) {
        txt++;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + txt - msg + 1 + (arg0 != NULL ? arg0->m_stringLength : 0) + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->strings = Struct_Microsoft_Singularity_Tracing_Strings_String0;
    while (*msg != '\0') {
        *text++ = *msg++;
    }
    *text++ = '\0';
    text = g_AddText(text, arg0);
    log->arg1 = (uintptr)arg1;
}

void Class_Microsoft_Singularity_Tracing::
g_Log(uint8 severity,
      char * msg,
      Class_System_String *arg0,
      UIntPtr arg1,
      UIntPtr arg2
      )
{
    UIntPtr _eip;
    __asm {
        mov eax, [ebp+4];
        mov _eip, eax;
    }
    char *txt = msg;
    while (*txt) {
        txt++;
    }
    Struct_Microsoft_Singularity_Tracing_LogEntry * log;
    uint8 *text;
    int chars = 1 + txt - msg + 1 + (arg0 != NULL ? arg0->m_stringLength : 0) + 1;
    log = g_CreateLog(severity, _eip, chars, &text);
    log->strings = Struct_Microsoft_Singularity_Tracing_Strings_String0;
    while (*msg != '\0') {
        *text++ = *msg++;
    }
    *text++ = '\0';
    text = g_AddText(text, arg0);
    log->arg1 = (uintptr)arg1;
    log->arg2 = (uintptr)arg2;
}

//
///////////////////////////////////////////////////////////////// End of File.
