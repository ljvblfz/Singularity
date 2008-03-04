//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// halkd.h: runtime support for debugging
//


#define FALSE 0
#define TRUE   1

#define IN
#define OUT
#define OPTIONAL
#define CONST const

#define ASSERT(x)
#define NT_SUCCESS(Status) ((NTSTATUS)(Status) >= 0)

// NT Compat types
typedef BOOL BOOLEAN;
typedef UINT16 USHORT;
typedef INT32  LONG;
typedef UINT32 ULONG, *PULONG;
typedef UINT64 ULONG64;
typedef INT64 LONG64;
typedef char *PCHAR;
typedef char  CHAR;
typedef void   VOID;
typedef UCHAR *PUCHAR;
typedef WCHAR *LPWSTR, *PWSTR;
typedef /*_W64 */ long LONG_PTR, *PLONG_PTR;
typedef DWORD NTSTATUS;
typedef struct _STRING {
    USHORT Length;
    USHORT MaximumLength;
    PCHAR  Buffer;
} STRING, *PSTRING;
typedef struct _UNICODE_STRING {
    USHORT Length;
    USHORT MaximumLength;
    PWSTR  Buffer;
} UNICODE_STRING;
typedef struct _LIST_ENTRY {
    struct _LIST_ENTRY *Flink;
    struct _LIST_ENTRY *Blink;
} LIST_ENTRY, *PLIST_ENTRY;

//
// Some quick macros to avoid having to edit too much NT code
//
#define RtlZeroMemory(base, len)            memset((base), 0, (len))
#define KdpQuickMoveMemory(dst,src,len)     memcpy((dst),(src),(len))

// Read memory from an untrusted pointer into a trusted buffer.
#define KdpCopyFromPtr(Dst, Src, Size, Done) \
    KdpCopyMemoryChunks((ULONG_PTR)(Src), Dst, Size, 0,                       \
                        MMDBG_COPY_UNSAFE, Done)
// Write memory from a trusted buffer through an untrusted pointer.
#define KdpCopyToPtr(Dst, Src, Size, Done) \
    KdpCopyMemoryChunks((ULONG_PTR)(Dst), Src, Size, 0,                       \
                        MMDBG_COPY_WRITE | MMDBG_COPY_UNSAFE, Done)

#define RtlInitUnicodeString(string, source, length) \
 { (string)->Buffer = (source); (string)->Length = (string)->MaximumLength = (length); }

#define FORCEINLINE __inline

VOID
FORCEINLINE
InitializeListHead(
    IN PLIST_ENTRY ListHead
    )
{
    ListHead->Flink = ListHead->Blink = ListHead;
}

BOOLEAN
FORCEINLINE
RemoveEntryList(
    IN PLIST_ENTRY Entry
    )
{
    PLIST_ENTRY Blink;
    PLIST_ENTRY Flink;

    Flink = Entry->Flink;
    Blink = Entry->Blink;
    Blink->Flink = Flink;
    Flink->Blink = Blink;
    return (BOOLEAN)(Flink == Blink);
}

VOID
FORCEINLINE
InsertTailList(
    IN PLIST_ENTRY ListHead,
    IN PLIST_ENTRY Entry
    )
{
    PLIST_ENTRY Blink;

    Blink = ListHead->Blink;
    Entry->Flink = ListHead;
    Entry->Blink = Blink;
    Blink->Flink = Entry;
    ListHead->Blink = Entry;
}

//#define KdpCopyFromPtr(dst, src, size, done)  { memcpy((dst),(src),(size)); *(done)=(size); }
//#define KdpCopyToPtr(dst, src, size, done)  { memcpy((dst),(src),(size)); *(done)=(size); }



//======================================================================
// Selected structs and defines used by the kernel debugger
//
typedef void *PNON_PAGED_DEBUG_INFO;

typedef struct _KLDR_DATA_TABLE_ENTRY {
    LIST_ENTRY InLoadOrderLinks;
    PVOID __Unused1;
    PVOID __Unused2;
    PVOID __Unused3;
    PNON_PAGED_DEBUG_INFO NonPagedDebugInfo;
    PVOID DllBase;
    PVOID EntryPoint;
    ULONG SizeOfImage;
    UNICODE_STRING FullDllName;
    UNICODE_STRING BaseDllName;
    ULONG Flags;
    USHORT LoadCount;
    USHORT __Unused5;
    PVOID SectionPointer;
    ULONG CheckSum;
    // ULONG padding on IA64
    ULONG TimeDateStamp;
    //    PVOID LoadedImports;
    PVOID __Unused6;
} KLDR_DATA_TABLE_ENTRY, *PKLDR_DATA_TABLE_ENTRY;

//======================================================================
// Selected structs and defines used by the KD protocol
//

//
//  Values put in ExceptionRecord.ExceptionInformation[0]
//  First parameter is always in ExceptionInformation[1],
//  Second parameter is always in ExceptionInformation[2]
//

#define BREAKPOINT_BREAK            0
#define BREAKPOINT_PRINT            1
#define BREAKPOINT_PROMPT           2
#define BREAKPOINT_LOAD_SYMBOLS     3
#define BREAKPOINT_UNLOAD_SYMBOLS   4
#define BREAKPOINT_COMMAND_STRING   5


#define CONTEXT_i386    0x00010000    // this assumes that i386 and
#define CONTEXT_i486    0x00010000    // i486 have identical context records

// end_wx86

#define CONTEXT_CONTROL         (CONTEXT_i386 | 0x00000001L) // SS:SP, CS:IP, FLAGS, BP
#define CONTEXT_INTEGER         (CONTEXT_i386 | 0x00000002L) // AX, BX, CX, DX, SI, DI
#define CONTEXT_SEGMENTS        (CONTEXT_i386 | 0x00000004L) // DS, ES, FS, GS
#define CONTEXT_FLOATING_POINT  (CONTEXT_i386 | 0x00000008L) // 387 state
#define CONTEXT_DEBUG_REGISTERS (CONTEXT_i386 | 0x00000010L) // DB 0-3,6,7
#define CONTEXT_EXTENDED_REGISTERS  (CONTEXT_i386 | 0x00000020L) // cpu specific extensions

#define CONTEXT_FULL (CONTEXT_CONTROL | CONTEXT_INTEGER |\
                      CONTEXT_SEGMENTS)

#define CONTEXT_ALL (CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_SEGMENTS | CONTEXT_FLOATING_POINT | CONTEXT_DEBUG_REGISTERS | CONTEXT_EXTENDED_REGISTERS)

#define CONTEXT_TO_PROGRAM_COUNTER(Context) ((Context)->Eip)

typedef struct _DESCRIPTOR {
    USHORT  Pad;
    USHORT  Limit;
    ULONG   Base;
} KDESCRIPTOR, *PKDESCRIPTOR;

typedef struct _KSPECIAL_REGISTERS {
    ULONG Cr0;
    ULONG Cr2;
    ULONG Cr3;
    ULONG Cr4;
    ULONG KernelDr0;
    ULONG KernelDr1;
    ULONG KernelDr2;
    ULONG KernelDr3;
    ULONG KernelDr6;
    ULONG KernelDr7;
    KDESCRIPTOR Gdtr;
    KDESCRIPTOR Idtr;
    USHORT Tr;
    USHORT Ldtr;
    ULONG Reserved[6];
} KSPECIAL_REGISTERS, *PKSPECIAL_REGISTERS;

//
// Processor State frame: Before a processor freezes itself, it
// dumps the processor state to the processor state frame for
// debugger to examine.
//

typedef struct _KPROCESSOR_STATE {
    struct _CONTEXT ContextFrame;
    struct _KSPECIAL_REGISTERS SpecialRegisters;
} KPROCESSOR_STATE, *PKPROCESSOR_STATE;

#if 0  // XXX We have one of these already in minidump.h
typedef struct _EXCEPTION_RECORD64 {
    DWORD    ExceptionCode;
    DWORD ExceptionFlags;
    DWORD64 ExceptionRecord;
    DWORD64 ExceptionAddress;
    DWORD NumberParameters;
    DWORD __unusedAlignment;
    DWORD64 ExceptionInformation[EXCEPTION_MAXIMUM_PARAMETERS];
} EXCEPTION_RECORD64, *PEXCEPTION_RECORD64;
#endif

// Don't care what these point to!
typedef void *PKTRAP_FRAME;
typedef void *PKEXCEPTION_FRAME;

//
// Processor modes.
//
typedef CHAR KPROCESSOR_MODE;
typedef enum _MODE {
    KernelMode,
    UserMode,
    MaximumMode
} MODE;


typedef struct _DBGKM_EXCEPTION64 {
    EXCEPTION_RECORD64 ExceptionRecord;
    ULONG FirstChance;
} DBGKM_EXCEPTION64, *PDBGKM_EXCEPTION64;


#define DBGKD_MAXSTREAM 16

typedef struct _X86_DBGKD_CONTROL_REPORT {
    ULONG   Dr6;
    ULONG   Dr7;
    USHORT  InstructionCount;
    USHORT  ReportFlags;
    UCHAR   InstructionStream[DBGKD_MAXSTREAM];
    USHORT  SegCs;
    USHORT  SegDs;
    USHORT  SegEs;
    USHORT  SegFs;
    ULONG   EFlags;
} X86_DBGKD_CONTROL_REPORT, *PX86_DBGKD_CONTROL_REPORT;

#define X86_REPORT_INCLUDES_SEGS    0x0001
// Indicates the current CS is a standard 32-bit flat segment.
// This allows the debugger to avoid retrieving the
// CS descriptor to see if it's 16-bit code or not.
// Note that the V86 flag in EFlags must also be checked
// when determining the code type.
#define X86_REPORT_STANDARD_CS      0x0002

typedef X86_DBGKD_CONTROL_REPORT   DBGKD_CONTROL_REPORT;

typedef struct _ALPHA_DBGKD_CONTROL_REPORT {
    ULONG InstructionCount;
    UCHAR InstructionStream[DBGKD_MAXSTREAM];
} ALPHA_DBGKD_CONTROL_REPORT, *PALPHA_DBGKD_CONTROL_REPORT;

typedef struct _IA64_DBGKD_CONTROL_REPORT {
    ULONG InstructionCount;
    UCHAR InstructionStream[DBGKD_MAXSTREAM];
} IA64_DBGKD_CONTROL_REPORT, *PIA64_DBGKD_CONTROL_REPORT;

typedef struct _AMD64_DBGKD_CONTROL_REPORT {
    ULONG64 Dr6;
    ULONG64 Dr7;
    ULONG EFlags;
    USHORT InstructionCount;
    USHORT ReportFlags;
    UCHAR InstructionStream[DBGKD_MAXSTREAM];
    USHORT SegCs;
    USHORT SegDs;
    USHORT SegEs;
    USHORT SegFs;
} AMD64_DBGKD_CONTROL_REPORT, *PAMD64_DBGKD_CONTROL_REPORT;

typedef struct _DBGKD_ANY_CONTROL_REPORT
{
    union
    {
        X86_DBGKD_CONTROL_REPORT X86ControlReport;
        // XXX Though we only care about x86 this union must be the correct size
        ALPHA_DBGKD_CONTROL_REPORT AlphaControlReport;
        IA64_DBGKD_CONTROL_REPORT IA64ControlReport;
        AMD64_DBGKD_CONTROL_REPORT Amd64ControlReport;
    };
} DBGKD_ANY_CONTROL_REPORT, *PDBGKD_ANY_CONTROL_REPORT;

typedef struct _DBGKD_LOAD_SYMBOLS64 {
    ULONG PathNameLength;
    ULONG64 BaseOfDll;
    ULONG64 ProcessId;
    ULONG CheckSum;
    ULONG SizeOfImage;
    BOOLEAN UnloadSymbols;
} DBGKD_LOAD_SYMBOLS64, *PDBGKD_LOAD_SYMBOLS64;

typedef struct _DBGKD_COMMAND_STRING {
    ULONG Flags;
    ULONG Reserved1;
    ULONG64 Reserved2[7];
} DBGKD_COMMAND_STRING, *PDBGKD_COMMAND_STRING;

// Protocol version 6 state change.
typedef struct _DBGKD_ANY_WAIT_STATE_CHANGE {
    ULONG NewState;
    USHORT ProcessorLevel;
    USHORT Processor;
    ULONG NumberProcessors;
    ULONG64 Thread;
    ULONG64 ProgramCounter;
    union {
        DBGKM_EXCEPTION64 Exception;
        DBGKD_LOAD_SYMBOLS64 LoadSymbols;
        DBGKD_COMMAND_STRING CommandString;
    } u;
    // The ANY control report is unioned here to
    // ensure that this structure is always large
    // enough to hold any possible state change.
    union {
        DBGKD_CONTROL_REPORT ControlReport;
        DBGKD_ANY_CONTROL_REPORT AnyControlReport;
    };
} DBGKD_ANY_WAIT_STATE_CHANGE, *PDBGKD_ANY_WAIT_STATE_CHANGE;


typedef struct _DBGKD_READ_MEMORY64 {
    ULONG64 TargetBaseAddress;
    ULONG TransferCount;
    ULONG ActualBytesRead;
} DBGKD_READ_MEMORY64, *PDBGKD_READ_MEMORY64;

typedef struct _DBGKD_WRITE_MEMORY64 {
    ULONG64 TargetBaseAddress;
    ULONG TransferCount;
    ULONG ActualBytesWritten;
} DBGKD_WRITE_MEMORY64, *PDBGKD_WRITE_MEMORY64;

//
// Response is a get context message with a full context record following
//

typedef struct _DBGKD_GET_CONTEXT {
    ULONG Unused;
} DBGKD_GET_CONTEXT, *PDBGKD_GET_CONTEXT;

//
// Full Context record follows
//

typedef struct _DBGKD_SET_CONTEXT {
    ULONG ContextFlags;
} DBGKD_SET_CONTEXT, *PDBGKD_SET_CONTEXT;

//
// Define breakpoint table entry structure.
//


// XXX Breakpoints are x86 specific
#define KDP_BREAKPOINT_TYPE  UCHAR
#define KDP_BREAKPOINT_BUFFER sizeof(UCHAR)
#define KDP_BREAKPOINT_ALIGN 0
#define KDP_BREAKPOINT_INSTR_ALIGN 0
#define KDP_BREAKPOINT_VALUE 0xcc

#define KD_BREAKPOINT_IN_USE        0x00000001
#define KD_BREAKPOINT_NEEDS_WRITE   0x00000002
#define KD_BREAKPOINT_SUSPENDED     0x00000004
#define KD_BREAKPOINT_NEEDS_REPLACE 0x00000008

typedef struct _BREAKPOINT_ENTRY {
    ULONG Flags;
    ULONG_PTR DirectoryTableBase;
    PVOID Address;
    KDP_BREAKPOINT_TYPE Content;
} BREAKPOINT_ENTRY, *PBREAKPOINT_ENTRY;

#define BREAKPOINT_TABLE_SIZE   32      // max number supported by kernel


typedef struct _DBGKD_WRITE_BREAKPOINT32 {
    ULONG BreakPointAddress;
    ULONG BreakPointHandle;
} DBGKD_WRITE_BREAKPOINT32, *PDBGKD_WRITE_BREAKPOINT32;

typedef struct _DBGKD_WRITE_BREAKPOINT64 {
    ULONG64 BreakPointAddress;
    ULONG BreakPointHandle;
} DBGKD_WRITE_BREAKPOINT64, *PDBGKD_WRITE_BREAKPOINT64;

typedef struct _DBGKD_RESTORE_BREAKPOINT {
    ULONG BreakPointHandle;
} DBGKD_RESTORE_BREAKPOINT, *PDBGKD_RESTORE_BREAKPOINT;

typedef struct _DBGKD_CONTINUE {
    NTSTATUS ContinueStatus;
} DBGKD_CONTINUE, *PDBGKD_CONTINUE;

// DBGKD_ANY_CONTROL_SET is 32-bit packed with an NTSTATUS in
// DBGKD_CONTINUE2 so start with a 32-bit value to get the 64-bit
// values aligned.

#pragma pack(push,4)

typedef struct _X86_DBGKD_CONTROL_SET {
    ULONG   TraceFlag;
    ULONG   Dr7;
    ULONG   CurrentSymbolStart;
    ULONG   CurrentSymbolEnd;
} X86_DBGKD_CONTROL_SET, *PX86_DBGKD_CONTROL_SET;

typedef ULONG ALPHA_DBGKD_CONTROL_SET, *PALPHA_DBGKD_CONTROL_SET;

#define IA64_DBGKD_CONTROL_SET_CONTINUE_NONE                0x0000
#define IA64_DBGKD_CONTROL_SET_CONTINUE_TRACE_INSTRUCTION   0x0001
#define IA64_DBGKD_CONTROL_SET_CONTINUE_TRACE_TAKEN_BRANCH  0x0002

typedef struct _IA64_DBGKD_CONTROL_SET {
    ULONG   Continue;
    ULONG64 CurrentSymbolStart;
    ULONG64 CurrentSymbolEnd;
} IA64_DBGKD_CONTROL_SET, *PIA64_DBGKD_CONTROL_SET;

typedef struct _AMD64_DBGKD_CONTROL_SET {
    ULONG   TraceFlag;
    ULONG64 Dr7;
    ULONG64 CurrentSymbolStart;
    ULONG64 CurrentSymbolEnd;
} AMD64_DBGKD_CONTROL_SET, *PAMD64_DBGKD_CONTROL_SET;

typedef struct _DBGKD_ANY_CONTROL_SET
{
    union
    {
        X86_DBGKD_CONTROL_SET X86ControlSet;
        ALPHA_DBGKD_CONTROL_SET AlphaControlSet;
        IA64_DBGKD_CONTROL_SET IA64ControlSet;
        AMD64_DBGKD_CONTROL_SET Amd64ControlSet;
    };
} DBGKD_ANY_CONTROL_SET, *PDBGKD_ANY_CONTROL_SET;

typedef X86_DBGKD_CONTROL_SET      DBGKD_CONTROL_SET;

#pragma pack(pop)

// This structure must be 32-bit packed for
// for compatibility with older, processor-specific
// versions of this structure.

#pragma pack(push,4)

typedef struct _DBGKD_CONTINUE2 {
    NTSTATUS ContinueStatus;
    // The ANY control set is unioned here to
    // ensure that this structure is always large
    // enough to hold any possible continue.
    union {
        DBGKD_CONTROL_SET ControlSet;
        DBGKD_ANY_CONTROL_SET AnyControlSet;
    };
} DBGKD_CONTINUE2, *PDBGKD_CONTINUE2;

#pragma pack(pop)

//
// MSR support
//

typedef struct _DBGKD_READ_WRITE_MSR {
    ULONG Msr;
    ULONG DataValueLow;
    ULONG DataValueHigh;
} DBGKD_READ_WRITE_MSR, *PDBGKD_READ_WRITE_MSR;


typedef struct _DBGKD_GET_VERSION64 {
    USHORT  MajorVersion;
    USHORT  MinorVersion;
    USHORT  ProtocolVersion;
    USHORT  Flags;
    USHORT  MachineType;

    //
    // Protocol command support descriptions.
    // These allow the debugger to automatically
    // adapt to different levels of command support
    // in different kernels.
    //

    // One beyond highest packet type understood, zero based.
    UCHAR   MaxPacketType;
    // One beyond highest state change understood, zero based.
    UCHAR   MaxStateChange;
    // One beyond highest state manipulate message understood, zero based.
    UCHAR   MaxManipulate;

    // Kind of execution environment the kernel is running in,
    // such as a real machine or a simulator.  Written back
    // by the simulation if one exists.
    UCHAR   Simulation;

    USHORT  Unused[1];

    ULONG64 KernBase;
    ULONG64 PsLoadedModuleList;

    //
    // Components may register a debug data block for use by
    // debugger extensions.  This is the address of the list head.
    //
    // There will always be an entry for the debugger.
    //

    ULONG64 DebuggerDataList;

} DBGKD_GET_VERSION64, *PDBGKD_GET_VERSION64;


typedef struct _DBGKD_MANIPULATE_STATE64 {
    ULONG ApiNumber;
    USHORT ProcessorLevel;
    USHORT Processor;
    NTSTATUS ReturnStatus;
    union {
        DBGKD_READ_MEMORY64 ReadMemory;
        DBGKD_WRITE_MEMORY64 WriteMemory;
        DBGKD_GET_VERSION64 GetVersion64;
        DBGKD_GET_CONTEXT GetContext;
        DBGKD_SET_CONTEXT SetContext;
        DBGKD_WRITE_BREAKPOINT64 WriteBreakPoint;
        DBGKD_RESTORE_BREAKPOINT RestoreBreakPoint;
        DBGKD_CONTINUE Continue;
        DBGKD_CONTINUE2 Continue2;
        DBGKD_READ_WRITE_MSR ReadWriteMsr;
#if 0
        DBGKD_READ_WRITE_IO64 ReadWriteIo;
        DBGKD_READ_WRITE_IO_EXTENDED64 ReadWriteIoExtended;
        DBGKD_QUERY_SPECIAL_CALLS QuerySpecialCalls;
        DBGKD_SET_SPECIAL_CALL64 SetSpecialCall;
        DBGKD_SET_INTERNAL_BREAKPOINT64 SetInternalBreakpoint;
        DBGKD_GET_INTERNAL_BREAKPOINT64 GetInternalBreakpoint;
        DBGKD_BREAKPOINTEX BreakPointEx;
        DBGKD_SEARCH_MEMORY SearchMemory;
        DBGKD_GET_SET_BUS_DATA GetSetBusData;
        DBGKD_FILL_MEMORY FillMemory;
        DBGKD_QUERY_MEMORY QueryMemory;
        DBGKD_SWITCH_PARTITION SwitchPartition;
#endif

    } u;
} DBGKD_MANIPULATE_STATE64, *PDBGKD_MANIPULATE_STATE64;

//
// If the packet type is PACKET_TYPE_KD_DEBUG_IO, then
// the format of the packet data is as follows:
//

#define DbgKdPrintStringApi     0x00003230L
#define DbgKdGetStringApi       0x00003231L

//
// For print string, the Null terminated string to print
// immediately follows the message
//
typedef struct _DBGKD_PRINT_STRING {
    ULONG LengthOfString;
} DBGKD_PRINT_STRING, *PDBGKD_PRINT_STRING;

//
// For get string, the Null terminated prompt string
// immediately follows the message. The LengthOfStringRead
// field initially contains the maximum number of characters
// to read. Upon reply, this contains the number of bytes actually
// read. The data read immediately follows the message.
//
//
typedef struct _DBGKD_GET_STRING {
    ULONG LengthOfPromptString;
    ULONG LengthOfStringRead;
} DBGKD_GET_STRING, *PDBGKD_GET_STRING;

typedef struct _DBGKD_DEBUG_IO {
    ULONG ApiNumber;
    USHORT ProcessorLevel;
    USHORT Processor;
    union {
        DBGKD_PRINT_STRING PrintString;
        DBGKD_GET_STRING GetString;
    } u;
} DBGKD_DEBUG_IO, *PDBGKD_DEBUG_IO;

//
// DbgKd APIs are for the portable kernel debugger
//

//
// KD_PACKETS are the low level data format used in KD. All packets
// begin with a packet leader, byte count, packet type. The sequence
// for accepting a packet is:
//
//  - read 4 bytes to get packet leader.  If read times out (10 seconds)
//    with a short read, or if packet leader is incorrect, then retry
//    the read.
//
//  - next read 2 byte packet type.  If read times out (10 seconds) with
//    a short read, or if packet type is bad, then start again looking
//    for a packet leader.
//
//  - next read 4 byte packet Id.  If read times out (10 seconds)
//    with a short read, or if packet Id is not what we expect, then
//    ask for resend and restart again looking for a packet leader.
//
//  - next read 2 byte count.  If read times out (10 seconds) with
//    a short read, or if byte count is greater than PACKET_MAX_SIZE,
//    then start again looking for a packet leader.
//
//  - next read 4 byte packet data checksum.
//
//  - The packet data immediately follows the packet.  There should be
//    ByteCount bytes following the packet header.  Read the packet
//    data, if read times out (10 seconds) then start again looking for
//    a packet leader.
//


typedef struct _KD_PACKET {
    ULONG PacketLeader;
    USHORT PacketType;
    USHORT ByteCount;
    ULONG PacketId;
    ULONG Checksum;
} KD_PACKET, *PKD_PACKET;

#define PACKET_MAX_SIZE 4000
#define INITIAL_PACKET_ID 0x80800000    // Don't use 0
#define SYNC_PACKET_ID    0x00000800    // Or in with INITIAL_PACKET_ID
                                        // to force a packet ID reset.

//
// BreakIn packet
//

#define BREAKIN_PACKET                  0x62626262
#define BREAKIN_PACKET_BYTE             0x62

//
// Packet lead in sequence
//

#define PACKET_LEADER                   0x30303030 //0x77000077
#define PACKET_LEADER_BYTE              0x30

#define CONTROL_PACKET_LEADER           0x69696969
#define CONTROL_PACKET_LEADER_BYTE      0x69

//
// Packet Trailing Byte
//

#define PACKET_TRAILING_BYTE            0xAA

//
// Packet Types
//

#define PACKET_TYPE_UNUSED              0
#define PACKET_TYPE_KD_STATE_CHANGE32   1
#define PACKET_TYPE_KD_STATE_MANIPULATE 2
#define PACKET_TYPE_KD_DEBUG_IO         3
#define PACKET_TYPE_KD_ACKNOWLEDGE      4       // Packet-control type
#define PACKET_TYPE_KD_RESEND           5       // Packet-control type
#define PACKET_TYPE_KD_RESET            6       // Packet-control type
#define PACKET_TYPE_KD_STATE_CHANGE64   7
#define PACKET_TYPE_KD_POLL_BREAKIN     8
#define PACKET_TYPE_KD_TRACE_IO         9
#define PACKET_TYPE_KD_CONTROL_REQUEST  10
#define PACKET_TYPE_KD_FILE_IO          11
#define PACKET_TYPE_MAX                 12


// State change constants
#define DbgKdMinimumStateChange       0x00003030L
#define DbgKdExceptionStateChange     0x00003030L
#define DbgKdLoadSymbolsStateChange   0x00003031L
#define DbgKdCommandStringStateChange 0x00003032L
#define DbgKdMaximumStateChange       0x00003033L


//
// If the packet type is PACKET_TYPE_KD_STATE_MANIPULATE, then
// the format of the packet data is as follows:
//
// Api Numbers for state manipulation
//

#define DbgKdMinimumManipulate              0x00003130L

#define DbgKdReadVirtualMemoryApi           0x00003130L
#define DbgKdWriteVirtualMemoryApi          0x00003131L
#define DbgKdGetContextApi                  0x00003132L
#define DbgKdSetContextApi                  0x00003133L
#define DbgKdWriteBreakPointApi             0x00003134L
#define DbgKdRestoreBreakPointApi           0x00003135L
#define DbgKdContinueApi                    0x00003136L
#define DbgKdReadControlSpaceApi            0x00003137L
#define DbgKdWriteControlSpaceApi           0x00003138L
#define DbgKdReadIoSpaceApi                 0x00003139L
#define DbgKdWriteIoSpaceApi                0x0000313AL
#define DbgKdRebootApi                      0x0000313BL
#define DbgKdContinueApi2                   0x0000313CL
#define DbgKdReadPhysicalMemoryApi          0x0000313DL
#define DbgKdWritePhysicalMemoryApi         0x0000313EL
//#define DbgKdQuerySpecialCallsApi           0x0000313FL
#define DbgKdSetSpecialCallApi              0x00003140L
#define DbgKdClearSpecialCallsApi           0x00003141L
#define DbgKdSetInternalBreakPointApi       0x00003142L
#define DbgKdGetInternalBreakPointApi       0x00003143L
#define DbgKdReadIoSpaceExtendedApi         0x00003144L
#define DbgKdWriteIoSpaceExtendedApi        0x00003145L
#define DbgKdGetVersionApi                  0x00003146L
#define DbgKdWriteBreakPointExApi           0x00003147L
#define DbgKdRestoreBreakPointExApi         0x00003148L
#define DbgKdCauseBugCheckApi               0x00003149L
#define DbgKdSwitchProcessor                0x00003150L
#define DbgKdPageInApi                      0x00003151L // obsolete
#define DbgKdReadMachineSpecificRegister    0x00003152L
#define DbgKdWriteMachineSpecificRegister   0x00003153L
#define OldVlm1                             0x00003154L
#define OldVlm2                             0x00003155L
#define DbgKdSearchMemoryApi                0x00003156L
#define DbgKdGetBusDataApi                  0x00003157L
#define DbgKdSetBusDataApi                  0x00003158L
#define DbgKdCheckLowMemoryApi              0x00003159L
#define DbgKdClearAllInternalBreakpointsApi 0x0000315AL
#define DbgKdFillMemoryApi                  0x0000315BL
#define DbgKdQueryMemoryApi                 0x0000315CL
#define DbgKdSwitchPartition                0x0000315DL

#define DbgKdMaximumManipulate              0x0000315EL



typedef struct _KD_CONTEXT {
    ULONG KdpDefaultRetries;
    BOOLEAN KdpControlCPending;
} KD_CONTEXT, *PKD_CONTEXT;

typedef enum {
    ContinueError = FALSE,
    ContinueSuccess = TRUE,
    ContinueProcessorReselected,
    ContinueNextProcessor
} KCONTINUE_STATUS;

//
// status Constants for Packet waiting
//

typedef enum {
    KDP_PACKET_RECEIVED     = 0,
    KDP_PACKET_TIMEOUT      = 1,
    KDP_PACKET_RESEND       = 2
} KDP_STATUS;

//
// Status Constants for reading data from comport
//

#define STATUS_SUCCESS                  ((NTSTATUS)0x00000000L)     // ntsubauth

#define STATUS_VCPP_EXCEPTION           ((NTSTATUS)0x406d1388L)
#define STATUS_CPP_EH_EXCEPTION         ((NTSTATUS)0xe06d7363L)

//  The operation that was requested is pending completion.
#define STATUS_PENDING                  ((NTSTATUS)0x00000103L)     // winnt

//  The requested operation was unsuccessful.
#define STATUS_UNSUCCESSFUL             ((NTSTATUS)0xC0000001L)

//  The instruction at "0x%08lx" referenced memory at "0x%08lx". The memory could not be "%s".
#define STATUS_ACCESS_VIOLATION         ((NTSTATUS)0xC0000005L)     // winnt

//  Illegal Instruction: An attempt was made to execute an illegal instruction.
#define STATUS_ILLEGAL_INSTRUCTION      ((NTSTATUS)0xC000001DL)     // winnt

//  {Application Error}:
//  The exception %s (0x%08lx) occurred in the application at location 0x%08lx.
//
#define STATUS_UNHANDLED_EXCEPTION      ((NTSTATUS)0xC0000144L)

//  {EXCEPTION}: Breakpoint: A breakpoint has been reached.
#define STATUS_BREAKPOINT               ((NTSTATUS)0x80000003L)     // winnt

//
// MessageId: STATUS_SINGLE_STEP
//
// MessageText:
//
//  {EXCEPTION}
//  Single Step
//  A single step or trace operation has just been completed.
//
#define STATUS_SINGLE_STEP               ((NTSTATUS)0x80000004L)    // winnt

//
// MessageId: STATUS_WAKE_SYSTEM_DEBUGGER
//
// MessageText:
//
//  {EXCEPTION}
//  Kernel Debugger Awakened
//  The system debugger was awakened by an interrupt.
//
#define STATUS_WAKE_SYSTEM_DEBUGGER      ((NTSTATUS)0x80000007L)    // winnt


#define DBGKD_64BIT_PROTOCOL_VERSION2 6
//
// If DBGKD_VERS_FLAG_DATA is set in Flags, info should be retrieved from
// the KDDEBUGGER_DATA block rather than from the DBGKD_GET_VERSION
// packet.  The data will remain in the version packet for a while to
// reduce compatibility problems.
//

#define DBGKD_VERS_FLAG_MP         0x0001   // kernel is MP built
#define DBGKD_VERS_FLAG_DATA       0x0002   // DebuggerDataList is valid
#define DBGKD_VERS_FLAG_PTR64      0x0004   // native pointers are 64 bits
#define DBGKD_VERS_FLAG_NOMM       0x0008   // No MM - don't decode PTEs
#define DBGKD_VERS_FLAG_HSS        0x0010   // hardware stepping support
#define DBGKD_VERS_FLAG_PARTITIONS 0x0020   // multiple OS partitions exist

#define IMAGE_FILE_MACHINE_I386              0x014c  // Intel 386.

//
// KD version MajorVersion high-byte identifiers.
//
typedef enum _DBGKD_MAJOR_TYPES
{
    DBGKD_MAJOR_NT,
    DBGKD_MAJOR_XBOX,
    DBGKD_MAJOR_BIG,
    DBGKD_MAJOR_EXDI,
    DBGKD_MAJOR_NTBD,
    DBGKD_MAJOR_EFI,
    DBGKD_MAJOR_TNT,
    DBGKD_MAJOR_SINGULARITY,
    DBGKD_MAJOR_COUNT
} DBGKD_MAJOR_TYPES;

#define DBGKD_MAJOR_TYPE(MajorVersion) \
    ((DBGKD_MAJOR_TYPES)((MajorVersion) >> 8))


#define MMDBG_COPY_WRITE            0x00000001
#define MMDBG_COPY_PHYSICAL         0x00000002
#define MMDBG_COPY_UNSAFE           0x00000004
#define MMDBG_COPY_CACHED           0x00000008
#define MMDBG_COPY_UNCACHED         0x00000010
#define MMDBG_COPY_WRITE_COMBINED   0x00000020

#define MMDBG_COPY_MAX_SIZE 8


//////////////////////////////////////////////////////////////////////////////
//

void KdInitialize(Struct_Microsoft_Singularity_BootInfo *bi);
void KdPutChar(char c);

void kdprintf(const char * pszFmt, ...); // Low level debug (to screen) only.
void kdprints(const char * pszFmt); // Low level debug (to screen) only.

void KdpSpin();
ULONG KdpComputeChecksum(IN PCHAR Buffer, IN ULONG Length);

bool KdpComInit(Struct_Microsoft_Singularity_BootInfo *bi);
void KdpComSendPacket(ULONG PacketType,
                      IN PSTRING MessageHeader,
                      IN PSTRING MessageData OPTIONAL,
                      IN OUT PKD_CONTEXT KdContext);
KDP_STATUS KdpComReceivePacket(IN ULONG PacketType,
                               OUT PSTRING MessageHeader,
                               OUT PSTRING MessageData,
                               OUT PULONG DataLength,
                               IN OUT PKD_CONTEXT KdContext);
bool KdpComPollBreakIn();

bool Kdp1394Init(Struct_Microsoft_Singularity_BootInfo *bi);
void Kdp1394SendPacket(ULONG PacketType,
                       IN PSTRING MessageHeader,
                       IN PSTRING MessageData OPTIONAL,
                       IN OUT PKD_CONTEXT KdContext);
KDP_STATUS Kdp1394ReceivePacket(IN ULONG PacketType,
                                OUT PSTRING MessageHeader,
                                OUT PSTRING MessageData,
                                OUT PULONG DataLength,
                                IN OUT PKD_CONTEXT KdContext);
bool Kdp1394PollBreakIn();


//////////////////////////////////////////////////////////// Shared Variables.
//

extern BOOL KdDebuggerNotPresent;
extern ULONG KdCompNumberRetries;
extern ULONG KdCompRetryCount;
extern ULONG KdPacketId;

///////////////////////////////////////////////////////////////// End of File.
