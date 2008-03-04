/////////////////////////////////////////////////////////////////////////////
//
//  structs.cpp - extensions to dump well-known structs.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#include "singx86.h"
#include <strsafe.h>

//////////////////////////////////////////////////////////////////////////////
//
FieldType ThreadContextFields[] = {
    FIELD(ThreadContext, mmx.st0),                  // Accessed directly as Field[0]
    FIELD(ThreadContext, mmx.st1),                  // Accessed directly as Field[1]
    FIELD(ThreadContext, mmx.st2),                  // Accessed directly as Field[2]
    FIELD(ThreadContext, mmx.st3),                  // Accessed directly as Field[3]
    FIELD(ThreadContext, mmx.st4),                  // Accessed directly as Field[4]
    FIELD(ThreadContext, mmx.st5),                  // Accessed directly as Field[5]
    FIELD(ThreadContext, mmx.st6),                  // Accessed directly as Field[6]
    FIELD(ThreadContext, mmx.st7),                  // Accessed directly as Field[7]
    FIELD(ThreadContext, mmx.fcw),                  // Accessed directly as Field[8]
    FIELD(ThreadContext, mmx.fsw),
    FIELD(ThreadContext, mmx.ftw),
    FIELD(ThreadContext, mmx.fop),
    FIELD(ThreadContext, mmx.eip),
    FIELD(ThreadContext, mmx.cs),
    FIELD(ThreadContext, mmx.dp),
    FIELD(ThreadContext, mmx.ds),
    FIELD(ThreadContext, mmx.mxcsr),
    FIELD(ThreadContext, mmx.mxcsrmask),
    FIELD(ThreadContext, regs),
    FIELD(ThreadContext, prev),
    FIELD(ThreadContext, next),
    FIELD(ThreadContext, eip),
    FIELD(ThreadContext, efl),
    FIELD(ThreadContext, num),
    FIELD(ThreadContext, err),
    FIELD(ThreadContext, cr2),
    FIELD(ThreadContext, eax),
    FIELD(ThreadContext, ebx),
    FIELD(ThreadContext, ecx),
    FIELD(ThreadContext, edx),
    FIELD(ThreadContext, esp),
    FIELD(ThreadContext, ebp),
    FIELD(ThreadContext, esi),
    FIELD(ThreadContext, edi),
    FIELD(ThreadContext, stackBegin),
    FIELD(ThreadContext, stackLimit),
    FIELD(ThreadContext, processId),
    FIELD(ThreadContext, uncaughtFlag),
    FIELD(ThreadContext, suspendAlert),
    FIELD(ThreadContext, _thread),
    FIELD(ThreadContext, processThread),
    FIELD(ThreadContext, stackMarkers),
    FIELD(ThreadContext, processMarkers),
    FIELD(ThreadContext, threadIndex),
    FIELD(ThreadContext, processThreadIndex),
    FIELD(ThreadContext, gcStates),
    FIELDEND(),
};

StructType ThreadContextStruct
= StructType("nt!ThreadContext",
             sizeof(ThreadContext), ThreadContextFields);

FieldType ThreadFields[] = {
    FIELD(Thread, context.mmx.st0),                  // Accessed directly as Field[0]
    FIELD(Thread, context.mmx.st1),                  // Accessed directly as Field[1]
    FIELD(Thread, context.mmx.st2),                  // Accessed directly as Field[2]
    FIELD(Thread, context.mmx.st3),                  // Accessed directly as Field[3]
    FIELD(Thread, context.mmx.st4),                  // Accessed directly as Field[4]
    FIELD(Thread, context.mmx.st5),                  // Accessed directly as Field[5]
    FIELD(Thread, context.mmx.st6),                  // Accessed directly as Field[6]
    FIELD(Thread, context.mmx.st7),                  // Accessed directly as Field[7]
    FIELD(Thread, context.mmx.fcw),                  // Accessed directly as Field[8]
    FIELD(Thread, context),                          // Accessed directly as Field[9]
    FIELD(Thread, context.mmx.fsw),
    FIELD(Thread, context.mmx.ftw),
    FIELD(Thread, context.mmx.fop),
    FIELD(Thread, context.mmx.eip),
    FIELD(Thread, context.mmx.cs),
    FIELD(Thread, context.mmx.dp),
    FIELD(Thread, context.mmx.ds),
    FIELD(Thread, context.mmx.mxcsr),
    FIELD(Thread, context.mmx.mxcsrmask),
    FIELD(Thread, blocked),
    FIELD(Thread, blockedOn),
    FIELD(Thread, blockedOnCount),
    FIELD(Thread, blockedUntil),
    FIELD(Thread, process),
    FIELD(Thread, schedulerEntry),
    FIELD(Thread, context.regs),
    FIELD(Thread, context.prev),
    FIELD(Thread, context.next),
    FIELD(Thread, context.eip),
    FIELD(Thread, context.efl),
    FIELD(Thread, context.num),
    FIELD(Thread, context.err),
    FIELD(Thread, context.cr2),
    FIELD(Thread, context.eax),
    FIELD(Thread, context.ebx),
    FIELD(Thread, context.ecx),
    FIELD(Thread, context.edx),
    FIELD(Thread, context.esp),
    FIELD(Thread, context.ebp),
    FIELD(Thread, context.esi),
    FIELD(Thread, context.edi),
    FIELD(Thread, context.stackBegin),
    FIELD(Thread, context.stackLimit),
    FIELD(Thread, context.processId),
    FIELD(Thread, context.uncaughtFlag),
    FIELD(Thread, context.suspendAlert),
    FIELD(Thread, context._thread),
    FIELD(Thread, context.processThread),
    FIELD(Thread, context.stackMarkers),
    FIELD(Thread, context.processMarkers),
    FIELD(Thread, context.threadIndex),
    FIELD(Thread, context.processThreadIndex),
    FIELD(Thread, context.gcStates),
    FIELDEND(),
};

StructType ThreadStruct
= StructType("nt!Thread",
             sizeof(Thread), ThreadFields);

FieldType ThreadEntryFields[] = {
    FIELD(ThreadEntry, queue),
    FIELDEND(),
};

StructType ThreadEntryStruct
= StructType("nt!ThreadEntry",
             sizeof(ThreadEntry), ThreadEntryFields);

FieldType StringFields[] = {
    FIELD(String, m_firstChar),                         // Accessed directly as Field[0]
    FIELD(String, m_stringLength),
    FIELDEND(),
};

StructType StringStruct = StructType("nt!String",
                                     sizeof(String), StringFields);

FieldType LogEntryFields[] = {
    FIELD(LogEntry, _cycleCount),
    FIELD(LogEntry, _eip),
    FIELD(LogEntry, _cpuId),
    FIELD(LogEntry, _threadId),
    FIELD(LogEntry, _processId),
    FIELD(LogEntry, _tag),
    FIELD(LogEntry, _severity),
    FIELD(LogEntry, _strings),
    FIELD(LogEntry, _text),
    FIELD(LogEntry, _arg0),
    FIELD(LogEntry, _arg1),
    FIELD(LogEntry, _arg2),
    FIELD(LogEntry, _arg3),
    FIELD(LogEntry, _arg4),
    FIELD(LogEntry, _arg5),
    FIELDEND(),
};

StructType LogEntryStruct = StructType("nt!Struct_Microsoft_Singularity_Tracing_LogEntry",
                                       sizeof(LogEntry), LogEntryFields);

FieldType ProcessorContextFields[] = {
    FIELD(ProcessorContext, threadContext),
    FIELD(ProcessorContext, threadContext),
    FIELD(ProcessorContext, _processor),
    FIELD(ProcessorContext, exception),
    FIELD(ProcessorContext, schedulerStackBegin),
    FIELD(ProcessorContext, schedulerStackLimit),
    FIELD(ProcessorContext, interruptStackBegin),
    FIELD(ProcessorContext, interruptStackLimit),
    FIELD(ProcessorContext, exceptionStackBegin),
    FIELD(ProcessorContext, exceptionStackLimit),
    FIELD(ProcessorContext, cpuId),
    FIELD(ProcessorContext, nextProcessorContext),
    FIELDEND(),
};

StructType ProcessorContextStruct
= StructType("nt!ProcessorContext",
             sizeof(ProcessorContext), ProcessorContextFields);

FieldType ProcessorFields[] = {
    FIELD(Processor, context),
    FIELD(Processor, nextTimerInterrupt),
    FIELD(Processor, pic),
    FIELD(Processor, timer),
    FIELD(Processor, clock),
    FIELD(Processor, timerInterrupt),
    FIELD(Processor, clockInterrupt),
    FIELD(Processor, inInterruptContext),
    FIELD(Processor, halted),
    FIELD(Processor, NumExceptions),
    FIELD(Processor, NumInterrupts),
    FIELD(Processor, NumContextSwitches),
    FIELD(Processor, interruptCounts),
    FIELDEND(),
};

StructType ProcessorStruct
= StructType("nt!Processor",
             sizeof(Processor), ProcessorFields);

/////////////////////////////////////////////////////////// KnownStructOutput.
//
const char* KnownStructs[] = {"String",
                              "Thread",
                              "System_String",
                              "System_Threading_Thread",
#if 0
                              "ProcessorContext",
                              "Microsoft_Singularity_X86_ProcessorContext"
#endif
};

HRESULT OnGetKnownStructNames(PDEBUG_CLIENT client,
                              PSTR buffer,
                              PULONG bufferSize)
{
    EXT_ENTER();    // Defines: HRESULT status = S_OK;

    //
    // Return names of known structs in multi string
    //
    ULONG sizeRemaining = *bufferSize, SizeNeeded = 0, Length;
    PCHAR copyAt = buffer;

    for (ULONG i = 0; i < arrayof(KnownStructs); i++) {
        if (sizeRemaining > (Length = (ULONG)strlen(KnownStructs[i]) + 1) &&
            status == S_OK) {
            status = StringCbCopy(copyAt, sizeRemaining, KnownStructs[i]);

            sizeRemaining -= Length;
            copyAt += Length;
        }
        else {
            status = S_FALSE;
        }
        SizeNeeded += Length;
    }
    // Terminate multistring and return size copied
    *copyAt = 0;
    *bufferSize = SizeNeeded+1;

    EXT_LEAVE();    // Macro includes: return status;
}

HRESULT OnGetSingleLineOutput(PDEBUG_CLIENT client,
                              ULONG64 address,
                              PSTR structName,
                              PSTR buffer,
                              PULONG bufferSize)
{
    EXT_ENTER();    // Defines: HRESULT status = S_OK;

    ExtVerb("OnGetSingleLineOutput [%s]\n", structName);

    if (strcmp(structName, "System_String") == 0 ||
        strcmp(structName, "String") == 0) {
        String str;
        ExtVerb("OnGetSingleLineOutput [%s] is string\n", structName);
        EXT_CHECK(StringStruct.Read(address, &str));
        ExtVerb("OnGetSingleLineOutput [%s] is still string\n", structName);

        WCHAR data[256];
        ULONG len;
        ULONG ret;

        len = (ULONG)str.m_stringLength;
        if (len > arrayof(data)) {
            len = arrayof(data);
        }
        if (len > *bufferSize - 20) {
            len = *bufferSize - 20;
        }

        if (len > 0) {
            EXT_CHECK(g_ExtData->ReadVirtual(address + StringFields[0].offset,
                                             data,
                                             sizeof(data[0]) * len,
                                             &ret));
            ExtVerb("OnGetSingleLineOutput [%s] read %d of %d chars to %p\n",
                    structName, len, *bufferSize, buffer);
        }

        status = StringCbPrintf(buffer, *bufferSize, " { [%d] \"%.*ls\" }",
                                (ULONG)str.m_stringLength, len, data);
        ExtVerb("OnGetSingleLineOutput [%s] status = %x\n",
                structName, status);
    }
    else if (!strcmp(structName, "System_Threading_Thread") ||
             !strcmp(structName, "Thread")) {
        Thread thread;
        EXT_CHECK(ThreadStruct.Read(address, &thread));

        status = StringCbPrintf(buffer, *bufferSize, " { eip=%I64x ebp=%I64x esp=%I64x }",
                                thread.context.eip,
                                thread.context.ebp,
                                thread.context.esp);
    }
#if 0
    else if (!strcmp(structName, "Microsoft_Singularity_X86_ProcessorContext") ||
             !strcmp(structName, "ProcessorContext")) {
        ProcessorContext pc;
        EXT_CHECK(ProcessorContext.Read(address, &pc));

        status = StringCbPrintf(buffer, *bufferSize, " { eip=%I64x ebp=%I64x esp=%I64x }",
                                thread.context.eip,
                                thread.context.ebp,
                                thread.context.esp);
    }
#endif
    else {
        status = E_INVALIDARG;
    }

    EXT_LEAVE();    // Macro includes: return status;
}

HRESULT OnGetSuppressTypeName(PDEBUG_CLIENT client, PSTR structName)
{
    UNREFERENCED_PARAMETER(structName);

    EXT_ENTER();    // Defines: HRESULT status = S_OK;
    EXT_LEAVE();    // Macro includes: return status;
}
