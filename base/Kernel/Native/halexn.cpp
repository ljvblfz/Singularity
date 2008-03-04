/////////////////////////////////////////////////////////////////////////////
//
//  halexn.cpp - Singularity/Bartok Exception Handling
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  Runtime support for CIL exceptions
//  * searching exception table
//  * make machine faults raise CIL exceptions
//

#if SINGULARITY
# include "hal.h"
#else
# include "brt.h"
# include <malloc.h>
#endif

extern "C" void __cdecl _throwDispatcher();
extern "C" void __cdecl _throwArithmeticException();
extern "C" void __cdecl _throwDivideByZeroException();
extern "C" void __cdecl _throwNullReferenceException();
extern "C" void __cdecl _throwOverflowException();
extern "C" void __cdecl _throwStackOverflowException();

#ifdef SINGULARITY
extern void DumpStack(UIntPtr ebp);
extern BOOL KdDebuggerNotPresent;
#endif

// See Bartok\tables\ExceptionTable.cs for a description of ExceptionTableEntry

//////////////////////////////////////////////////////////////////////////////
//
struct ExceptionTableEntry {
    uintptr scopeBaseAddr;
    union {
        struct {
            Class_System_Type *exceptionClass; // low bit is zero
            uintptr handlerAddr;
        };
        struct {
            uintptr frameSetupInfo; // low bit is one
            uintptr spillSize;
        };
    };
};

struct TableEntry {
    ExceptionTableEntry * tableBaseAddr;
    ExceptionTableEntry * tableEndAddr;
};

extern TableEntry TableBase[1];
extern TableEntry TableBound[1];

// This will break down with 64 bit pointers.  We will need to revisit the code
// that uses the ExceptionTableLookupReturn result (__throwDispatcher*).
STATIC_ASSERT(sizeof(uint64) == 2 * sizeof(uintptr));

union ExceptionTableLookupReturn {
    uint64 qword;
    struct {
        Class_System_Type *exceptionClass;
        uintptr handlerAddr;
    };
    struct {
        uintptr frameSetupInfo;
        uintptr spillSize;
    };
};

static uintptr LookupTable(uintptr throwAddr,
                           ExceptionTableEntry **tableBaseEntry,
                           ExceptionTableEntry **tableEndEntry) {
#if 0
    printf("LookupTable(throwAddr=%p, tableBase=%p, tableEnd=%p)\n",
           throwAddr, tableBaseEntry, tableEndEntry);
    printf("  TableBase=%p, TableBound=%p, maxIndex = %d\n",
           TableBase, TableBound, TableBound - TableBase);
    printf("  callSiteTableCount=%d\n",
           Class_System_GCs_CallStack::c_callSiteTableCount);
    printf("  codeBaseStartTable=%p\n",
           Class_System_GCs_CallStack::c_codeBaseStartTable);
    printf("  returnAddressToCallSiteSetNumbers=%p\n",
           Class_System_GCs_CallStack::c_returnAddressToCallSiteSetNumbers);
    printf("  callSiteSetCount=%p\n",
           Class_System_GCs_CallStack::c_callSiteSetCount);
#endif

    //  search to find which table to use
    int maxIndex = TableBound - TableBase;
    uintptr codeBase = (uintptr) -1;
    uintptr relCodeAddr = 0;
    for (int i = 0; i < maxIndex; i++) {
        TableEntry *entry = &TableBase[i];
#if 0
        printf("   TableBase[%d]  base=%p end=%p  codeBaseStartTable[]=%p\n",
               i, entry->tableBaseAddr, entry->tableEndAddr,
               Class_System_GCs_CallStack::c_codeBaseStartTable[i]);
#endif
        codeBase =
            ((uintptr*)Class_System_GCs_CallStack::c_codeBaseStartTable)[i];

        if (throwAddr < codeBase) {
            continue;
        }
        relCodeAddr = throwAddr - codeBase;
        *tableBaseEntry = entry->tableBaseAddr;
        *tableEndEntry = entry->tableEndAddr;

#if 0
        printf("   relCodeAddr = %p\n", relCodeAddr);
        printf("    tableBase scopeBaseAddr=%p class=%p handler=%p\n",
               (*tableBaseEntry)->scopeBaseAddr,
               (*tableBaseEntry)->exceptionClass,
               (*tableBaseEntry)->handlerAddr);
        printf("    tableEnd  scopeBaseAddr=%p class=%p handler=%p\n",
               (*tableEndEntry)->scopeBaseAddr,
               (*tableEndEntry)->exceptionClass,
               (*tableEndEntry)->handlerAddr);
#endif

        if ((relCodeAddr >= (*tableBaseEntry)->scopeBaseAddr)
            && (relCodeAddr <= (*tableEndEntry)->scopeBaseAddr)) {
            return codeBase;
        }
    }

    return (uintptr) -1;
    //exit(-2);
    //__asm int 3;
}

#if SINGULARITY
Class_System_VTable * getRealVTable(Class_System_VTable * vt)
{
    return (Class_System_VTable *)((uintptr)vt & (~((uintptr)3)));
}
#endif

// search an exception table

//////////////////////////////////////////////////////////////////////////////

// search an exception table
// - Returns the exception in eax.
// - Returns the handler address in edx.
// OR if the shared unwind handler should be used
// - Returns the frameSetupInfo in eax.
// - Returns the spill area size in edx.

uint64 __fastcall ExceptionTableLookup(Class_System_Exception *exception,
                                       uintptr throwAddr) {
#if 0
    printf("\n");
    printf("ExceptionTableLookup(exception=%p, vtable=%p, throwAddr=%p)\n",
           exception, ((uintptr *)exception)[0], throwAddr);
#endif

#if SINGULARITY
    if (exception->_throwAddress == NULL) {
        exception->_throwAddress = throwAddr;
    }
#endif

    //  search for table using throwAddr
    ExceptionTableEntry *baseEntry = NULL;
    ExceptionTableEntry *endEntry = NULL;
    uintptr codeBase = LookupTable(throwAddr, &baseEntry, &endEntry);

#if 0
    printf("  codeBase=%p baseEntry=%p endEntry=%p\n",
           codeBase, baseEntry, endEntry);
#endif

    if (codeBase == (uintptr) -1) {
#if SINGULARITY_KERNEL
        printf("Exception outside of any known code regions!\n");
        if (!KdDebuggerNotPresent) {
            __asm int 3;
        }
        Class_System_VTable::g_TerminateByException(exception);
        __asm int 3;
#elif SINGULARITY_PROCESS
        Assert("Exception outside of any known code regions!\n");
        Class_System_VTable::g_TerminateByException(exception);
        __asm int 3;
#else
        Class_System_VTable::g_TerminateByException(exception);
        exit(-2);
#endif
    }

    // bsearch for throwAddr
    int minIndex = 0;
    int maxIndex = endEntry-baseEntry;
    throwAddr -= codeBase;

    if (throwAddr < baseEntry[minIndex].scopeBaseAddr ||
        throwAddr > baseEntry[maxIndex].scopeBaseAddr) {
        // BUGBUG: callback to C# code that may trigger GC
#if SINGULARITY_KERNEL
        printf("Exception outside of known code region for %p\n", codeBase);
        if (!KdDebuggerNotPresent) {
            __asm int 3;
        }
        Class_System_VTable::g_TerminateByException(exception);
        printf("top-level exception handling code failed\n");
        __asm int 3;
#elif SINGULARITY_PROCESS
        Assert("Exception outside of known code region for.\n");
        Class_System_VTable::g_TerminateByException(exception);
        Assert("top-level exception handling code failed\n");
        __asm int 3;
#else
        Class_System_VTable::g_TerminateByException(exception);
        fprintf(stderr, "top-level exception handling code failed");
        fflush(stderr);
        __asm int 3;
        exit(-2);
#endif
    }
    while (minIndex+1 < maxIndex) {
        int midIndex = (minIndex+maxIndex)/2;
        uintptr midAddr = baseEntry[midIndex].scopeBaseAddr;
        if (throwAddr < midAddr) {
            maxIndex = midIndex;
        }
        else {
            minIndex = midIndex;
        }
    }
    ExceptionTableEntry *entry = &baseEntry[minIndex];

    //  back up to first entry containing throwAddr (there may be several)
    uintptr baseAddr;
    for (baseAddr = entry->scopeBaseAddr;
         entry->scopeBaseAddr == baseAddr && entry >= baseEntry;
         entry--) {
        continue;
    }

    //  check each of the handlers in turn

    for (entry++; entry->scopeBaseAddr <= throwAddr; entry++) {
#if 0
        printf("    entry=%p[%d]  "
               "scopeBaseAddr=%p exceptionClass=%p handler=%p\n",
               entry, entry - baseEntry,
               entry->scopeBaseAddr, entry->exceptionClass, entry->handlerAddr);
#endif

        // 0 now means "no frame pointer omission and no callee save registers
        // have been saved to the stack":
        // Assert(entry->exceptionClass);

        Assert(((entry->frameSetupInfo & 0x1) != 0)
               || (entry->handlerAddr != NULL));
        if (((entry->frameSetupInfo & 0x1) != 0)
            || Class_System_VTable::g_IsExceptionHandler(entry->exceptionClass,
                                                         exception)) {
#if 0
            printf("Found matching exception entry: %p\n", entry);
#endif
            break;
        }
    }
    Assert(entry->scopeBaseAddr == baseAddr);

    ExceptionTableLookupReturn retval;

    if((entry->frameSetupInfo & 0x1) != 0) {
        retval.frameSetupInfo = entry->frameSetupInfo;
        retval.spillSize = entry->spillSize;
#if SINGULARITY
        Class_Microsoft_Singularity_Tracing::g_Log
            (9, "Throw {0} from {1:x8} to shared unwind handler",
             getRealVTable(exception->postHeader.vtableObject)->vtableType->name,
             (UIntPtr)(codeBase + throwAddr));
#endif
    }
    else {
        retval.exceptionClass = entry->exceptionClass;
        retval.handlerAddr = entry->handlerAddr + codeBase;
#if SINGULARITY
        Class_Microsoft_Singularity_Tracing::g_Log
            (9, "Throw {0} from {1:x8} to {2:x8}",
             getRealVTable(exception->postHeader.vtableObject)->vtableType->name,
             (UIntPtr)(codeBase + throwAddr),
             (UIntPtr)(retval.handlerAddr));
#endif
    }

#if SINGULARITY_KERNEL || SINGULARITY_PROCESS
    if (!exception->_notifiedDebugger) {
        exception->_notifiedDebugger = true;

        bool iflag =
            Class_Microsoft_Singularity_Processor::g_DisableInterrupts();
        Class_System_VTable * vtable = getRealVTable(exception->postHeader.vtableObject);
        if((entry->frameSetupInfo & 0x1) != 0) {
            Class_Microsoft_Singularity_Tracing::g_Log
                (2, "First chance {0} at {1:x8}.  Handler is shared",
                 vtable->vtableType->name,
                 (UIntPtr)(codeBase + throwAddr));
        }
        else {
            Class_Microsoft_Singularity_Tracing::g_Log
                (2, "First chance {0} at {1:x8}.  Handler is {2:x8}",
                 vtable->vtableType->name,
                 (UIntPtr)(codeBase + throwAddr),
                 (UIntPtr)retval.handlerAddr);
        }
        if (exception->_message != NULL) {
            Class_Microsoft_Singularity_Tracing::g_Log
                (0, "  Message: {0}", exception->_message, 0);
        }

        if (!KdDebuggerNotPresent) {
            KdDebugTrapData trapData, *trapDataPtr = &trapData;
            trapData.tag = KdDebugTrapData::FIRST_CHANCE_EXCEPTION;
            trapData.u.firstChanceException.throwAddr = throwAddr;
            __asm {
                // int 3; // uncomment this line to cause every exception to break here.
                mov eax, exception;
                mov fs:[0]Struct_Microsoft_Singularity_X86_ProcessorContext.exception, eax;
                mov eax, trapDataPtr;
                int 29; // Notify debugging stub of first chance exception.
            }
        }
        Class_Microsoft_Singularity_Processor::g_RestoreInterrupts(iflag);
    }
#endif

    return(retval.qword);
}

#if !SINGULARITY

void alterContinuation(PCONTEXT contextRecord, DWORD newPC) {
    // assign edx location of instruction that faulted
    contextRecord->Edx = contextRecord->Eip;
    contextRecord->Eip = (DWORD) newPC;
}

// BartokMachineFaultFilter:
//   This filter is invoked when when a machine fault occurs.   If the
//   machine fault occurred in MSIL code, it modifies the context so
//   that the appropriate exception is thrown when the filter returns.
//

LONG WINAPI BartokMachineFaultFilter(struct _EXCEPTION_POINTERS *exnInfo) {
    PEXCEPTION_RECORD exceptionRecord = exnInfo->ExceptionRecord;
    PCONTEXT contextRecord = exnInfo->ContextRecord;

#if 0
    printf("BartokMachineFaultFilter(exnInfo=%p)\n", exnInfo);
#endif

    // check if it is an exception in MSIL code.

    // points at instruction that faulted
    uintptr throwAddr = contextRecord->Eip;

    // search for table using throwAddr
    ExceptionTableEntry *baseEntry = NULL;
    ExceptionTableEntry *endEntry = NULL;
    uintptr codeBase = LookupTable(throwAddr, &baseEntry, &endEntry);

    int minIndex = 0;
    int maxIndex = endEntry-baseEntry;
    throwAddr -= codeBase;

    if (throwAddr < baseEntry[minIndex].scopeBaseAddr ||
        throwAddr > baseEntry[maxIndex].scopeBaseAddr) {
        // nope, it isn't.
        return EXCEPTION_CONTINUE_SEARCH;
    }

    switch (exceptionRecord->ExceptionCode) {
        case EXCEPTION_FLT_DIVIDE_BY_ZERO:
        case EXCEPTION_INT_DIVIDE_BY_ZERO: {
            alterContinuation(contextRecord,
                              (DWORD) _throwDivideByZeroException);
            return EXCEPTION_CONTINUE_EXECUTION;
        }
        case EXCEPTION_INT_OVERFLOW: {
            alterContinuation(contextRecord,(DWORD) _throwOverflowException);
            return EXCEPTION_CONTINUE_EXECUTION;
        }
        case EXCEPTION_STACK_OVERFLOW: {
            alterContinuation(contextRecord,
                              (DWORD) _throwStackOverflowException);
            return EXCEPTION_CONTINUE_EXECUTION;
        }
        case EXCEPTION_ACCESS_VIOLATION: {
            DWORD *exceptionInformation = exceptionRecord->ExceptionInformation;
            // BUGBUG: how much memory is protected? NB we must trap negative offsets
            // from null because the first access through a null reference may be
            // to a header word (e.g. opening the object for read in a memory transaction)
            if (exceptionInformation[1] < 4096) {
                exceptionInformation[1] >= 0xfffff000) {
                alterContinuation(contextRecord,
                                  (DWORD) _throwNullReferenceException);
                return EXCEPTION_CONTINUE_EXECUTION;
            }
            else {
                return EXCEPTION_CONTINUE_SEARCH;
            }
        }
        default: {
            return EXCEPTION_CONTINUE_SEARCH;
        }
    }
}

// If the stack pointer is too close to the bottom of the stack, abort the
// process otherwise reset the guard page.
// This function is called when we handle stack overflow exception.

void ResetGuardPage(){

    if (_resetstkoflw() == 0) {
      fprintf(stderr, "Reset from stack overflow failed. Abort.");
      fflush(stderr);
      _exit(-2);
    }
}

#endif // !SINGULARITY
