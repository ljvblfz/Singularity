/////////////////////////////////////////////////////////////////////////////
//
//  singx86.h - Singularity Debugger Extension common Header.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  For more information, see http://ddkslingshot/webs/debugexw/
//

#pragma warning(disable:4201) // Enable nameless structs and unions.

#include <winlean.h>
#include <stdio.h>
#include <stdlib.h>
#include <stddef.h>
#include <string.h>

//
// Define KDEXT_64BIT to make all wdbgexts APIs recognize 64 bit addresses
// It is recommended for extensions to use 64 bit headers from wdbgexts so
// the extensions could support 64 bit targets.
//
// #define KDEXT_64BIT
// #include <wdbgexts.h>
#include <dbgeng.h>

//////////////////////////////////////////////////////////////////////////////
//
// Load the Singularity types exported by halclass.h
//

#define arrayof(a)      (sizeof(a)/sizeof(a[0]))

#include <pshpack4.h>

/////////////////////////////////////////////////////////////// Static Assert.
//
// Compile-time (not run-time) assertion. Code will not compile if
// expr is false. Note: there is no non-debug version of this; we
// want this for all builds. The compiler optimizes the code away.
//
template <bool x> struct STATIC_ASSERT_FAILURE;
template <> struct STATIC_ASSERT_FAILURE<true> { };
template <int x> struct static_assert_test { };

#define STATIC_CAT_INNER(x,y) x##y
#define STATIC_CAT(x,y) STATIC_CAT_INNER(x,y)
#define STATIC_ASSERT(condition) \
   typedef static_assert_test< \
      sizeof(STATIC_ASSERT_FAILURE<(bool)(condition)>)> \
         STATIC_CAT(__static_assert_typedef_, __COUNTER__)

//////////////////////////////////////////////////////////////////////////////
//
#define OFFSETOF(s,m)   ((uintptr)&(((s *)0)->m))
#define ARRAYOF(a)      (sizeof(a)/sizeof(a[0]))


//#include <halclass.h>
#include <poppack.h>

//
//////////////////////////////////////////////////////////////////////////////

#ifdef __cplusplus
extern "C" {
#endif

    // Declares an extension routine.
#define EXT_DECL(Name) \
    extern "C" HRESULT CALLBACK \
    Name(PDEBUG_CLIENT client, PCSTR args)

    // Set up and clean up for an extension routine.
#define EXT_ENTER() \
    HRESULT status = ExtQuery(client); \
    if (status != S_OK) goto Exit; else 0

#define EXT_LEAVE() \
    Exit: ExtRelease(); return status

    // Safe release and NULL.
#define EXT_RELEASE(Unk) \
    ((Unk) != NULL ? ((Unk)->Release(), (Unk) = NULL) : NULL)

    // Evaluates a numeric expression from the current args
    // and updates the Args location.
    // Assumes variables args and status, plus exit label Exit.
#define EXT_ARG(Dst) \
    if ((status = ExtEvalU64(&args, &(Dst))) != S_OK) goto Exit; else 0

#define EXT_CHECK(expr) \
    if ((status = expr) != S_OK) goto Exit; else 0

    // Global variables initialized by query.
    extern PDEBUG_ADVANCED       g_ExtAdvanced;
    extern PDEBUG_CLIENT         g_ExtClient;
    extern PDEBUG_CONTROL4       g_ExtControl;
    extern PDEBUG_DATA_SPACES4   g_ExtData;
    extern PDEBUG_REGISTERS2     g_ExtRegisters;
    extern PDEBUG_SYMBOLS        g_ExtSymbols;
    extern PDEBUG_SYSTEM_OBJECTS g_ExtSystem;

    // Queries for all debugger interfaces.
    HRESULT ExtQuery(PDEBUG_CLIENT Client);

    // Cleans up all debugger interfaces.
    void ExtRelease(void);

    // Normal output.
    void __cdecl ExtOut(PCSTR Format, ...);
    // Error output.
    void __cdecl ExtErr(PCSTR Format, ...);
    // Warning output.
    void __cdecl ExtWarn(PCSTR Format, ...);
    // Verbose output.
    void __cdecl ExtVerb(PCSTR Format, ...);


    // Evaluates an expression from the given string
    // and updates the string pointer.  Consumes trailing whitespace.
    HRESULT ExtEvalU64(PCSTR* Str, PULONG64 Val);
    HRESULT ExtDefPointer(ULONG64 Address, PULONG64 Val);

    extern BOOL  Connected;
    extern ULONG TargetMachine;

    HRESULT OnTargetAccessible(PDEBUG_CLIENT Client);

    HRESULT OnGetKnownStructNames(PDEBUG_CLIENT Client,
                                  PSTR Buffer,
                                  PULONG BufferSize);
    HRESULT OnGetSingleLineOutput(PDEBUG_CLIENT Client,
                                  ULONG64 Address,
                                  PSTR StructName,
                                  PSTR Buffer,
                                  PULONG BufferSize);
    HRESULT OnGetSuppressTypeName(PDEBUG_CLIENT Client,
                                  PSTR StructName);

    ULONG64 GetStaticPointer(PCSTR name);

    ///////////////////////////////// Remote to Local conversions for structs.
    //

    // Metadata structures.
    struct StructType
    {
        struct StructType * next;
        struct FieldType * fields;
        ULONG       fieldCount;
        PCSTR       name;
        ULONG64     module;
        ULONG       type;
        ULONG       size;
        PBYTE       temp;
        ULONG       localSize;

        public:
        static StructType * registered;
        static HRESULT InitializeRegistered();

        public:
        StructType(PCSTR name, ULONG localSize, struct FieldType *fields);

        HRESULT Initialize();
        HRESULT RemoteOffsetFromLocal(ULONG localOffset, ULONG *remoteOffset);

        HRESULT Clear();
        HRESULT Read(ULONG64 address, PVOID local);
        HRESULT RawAccess(ULONG remoteOffset, PVOID *raw);
        HRESULT Update(PVOID local);
        HRESULT Flush(ULONG64 address);
    };

    struct FieldType
    {
        struct StructType *parent;
        PCSTR       name;
        ULONG       localOffset;
        ULONG64     module;
        ULONG       type;
        ULONG       offset;
        ULONG       size;
    };

    // Macros for defining metadata.
#define FIELD(s,f)  { NULL, #f, offsetof(s,f), 0, 0, 0, 0 }
#define FIELDEND()  { NULL, NULL, 0, 0, 0, 0, 0 }

    /////////////////////////////////////////////////////////// Known Structs.
    //
    struct MmxContext
    {
        ULONG64     fcw;
        ULONG64     fsw;
        ULONG64     ftw;
        ULONG64     fop;
        ULONG64     eip;
        ULONG64     cs;
        ULONG64     dp;
        ULONG64     ds;
        ULONG64     mxcsr;
        ULONG64     mxcsrmask;
        ULONG64     st0;
        ULONG64     st1;
        ULONG64     st2;
        ULONG64     st3;
        ULONG64     st4;
        ULONG64     st5;
        ULONG64     st6;
        ULONG64     st7;
    };

    struct ThreadContext
    {
        ULONG64     regs;
        ULONG64     prev;
        ULONG64     next;
        ULONG64     eip;
        ULONG64     efl;
        ULONG64     num;
        ULONG64     err;
        ULONG64     cr2;
        ULONG64     eax;
        ULONG64     ebx;
        ULONG64     ecx;
        ULONG64     edx;
        ULONG64     esp;
        ULONG64     ebp;
        ULONG64     esi;
        ULONG64     edi;
        ULONG64     stackBegin;
        ULONG64     stackLimit;
        ULONG64     processId;
        ULONG64     uncaughtFlag;
        ULONG64     suspendAlert;
        ULONG64     _thread;
        ULONG64     processThread;
        ULONG64     stackMarkers;
        ULONG64     processMarkers;
        ULONG64     threadIndex;
        ULONG64     processThreadIndex;
        ULONG64     gcStates;
        struct MmxContext   mmx;
    };

    extern FieldType ThreadContextFields[];
    extern StructType ThreadContextStruct;

    struct Thread
    {
        ULONG64     blocked;
        ULONG64     blockedOn;
        ULONG64     blockedOnCount;
        ULONG64     blockedUntil;
        ULONG64     process;
        ULONG64     schedulerEntry;
        ThreadContext   context;
    };

    extern FieldType ThreadFields[];
    extern StructType ThreadStruct;

    struct ThreadEntry
    {
        ULONG64     queue;
    };

    extern FieldType ThreadEntryFields[];
    extern StructType ThreadEntryStruct;

    struct ProcessorContext
    {
        ULONG64     threadContext;
        ULONG64     _processor;
        ULONG64     exception;
        ULONG64     schedulerStackBegin;
        ULONG64     schedulerStackLimit;
        ULONG64     interruptStackBegin;
        ULONG64     interruptStackLimit;
        ULONG64     exceptionStackBegin;
        ULONG64     exceptionStackLimit;

        ThreadContext   exceptionContext;
        ThreadContext   thirdContext;

        ULONG64     cpuId;
        ULONG64     nextProcessorContext;
    };
    extern FieldType ProcessorContextFields[];
    extern StructType ProcessorContextStruct;

    struct Processor
    {
        ULONG64     context;
        ULONG64     nextTimerInterrupt;

        ULONG64     pic;
        ULONG64     timer;
        ULONG64     clock;

        ULONG64     timerInterrupt;
        ULONG64     clockInterrupt;
        ULONG64     inInterruptContext;
        ULONG64     halted;

        ULONG64     NumExceptions;
        ULONG64     NumInterrupts;
        ULONG64     NumContextSwitches;

        ULONG64     interruptCounts;
    };
    extern FieldType ProcessorFields[];
    extern StructType ProcessorStruct;

    struct String
    {
        ULONG64     m_stringLength;
        ULONG64     m_firstChar;
    };

    extern FieldType StringFields[];
    extern StructType StringStruct;

    struct LogEntry
    {
        ULONG64     _cycleCount;
        ULONG64     _cpuId;
        ULONG64     _eip;
        ULONG64     _threadId;
        ULONG64     _processId;
        ULONG64     _tag;
        ULONG64     _severity;
        ULONG64     _strings;
        ULONG64     _text;
        union
        {
            ULONG64     _args[6];
            struct
            {
                ULONG64 _arg0;
                ULONG64 _arg1;
                ULONG64 _arg2;
                ULONG64 _arg3;
                ULONG64 _arg4;
                ULONG64 _arg5;
            };
        };
    };
    extern FieldType LogEntryFields[];
    extern StructType LogEntryStruct;


#ifdef __cplusplus
}
#endif
