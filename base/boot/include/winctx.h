////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   winctx.h
//
//  Note:   Context structures required by Windows Minidumps.
//

#pragma pack(push,4)

#if defined(_MSC_VER)
#if _MSC_VER >= 800
#if _MSC_VER >= 1200
#pragma warning(push)
#endif
#pragma warning(disable:4200)    /* Zero length array */
#pragma warning(disable:4201)    /* Nameless struct/union */
#endif
#endif

//////////////////////////////////////////////////////////////////////////////
//

//-----> From winver.h#132

typedef struct tagVS_FIXEDFILEINFO
{
    UINT32   dwSignature;            /* e.g. 0xfeef04bd */
    UINT32   dwStrucVersion;         /* e.g. 0x00000042 = "0.42" */
    UINT32   dwFileVersionMS;        /* e.g. 0x00030075 = "3.75" */
    UINT32   dwFileVersionLS;        /* e.g. 0x00000031 = "0.31" */
    UINT32   dwProductVersionMS;     /* e.g. 0x00030010 = "3.10" */
    UINT32   dwProductVersionLS;     /* e.g. 0x00000031 = "0.31" */
    UINT32   dwFileFlagsMask;        /* = 0x3F for version "0.42" */
    UINT32   dwFileFlags;            /* e.g. VFF_DEBUG | VFF_PRERELEASE */
    UINT32   dwFileOS;               /* e.g. VOS_DOS_WINDOWS16 */
    UINT32   dwFileType;             /* e.g. VFT_DRIVER */
    UINT32   dwFileSubtype;          /* e.g. VFT2_DRV_KEYBOARD */
    UINT32   dwFileDateMS;           /* e.g. 0 */
    UINT32   dwFileDateLS;           /* e.g. 0 */
} VS_FIXEDFILEINFO;

//-----> From winnt.h#2175
#if defined(AMD64)
//
// Define initial MxCsr and FpCsr control.
//

#define INITIAL_MXCSR 0x1f80            // initial MXCSR value
#define INITIAL_FPCSR 0x027f            // initial FPCSR value

//
// Define 128-bit 16-byte aligned xmm register type.

typedef struct __declspec(align(16))  _M128A {
    ULONGLONG Low;
    LONGLONG High;
} M128A, *PM128A;

//
// Format of data for 32-bit fxsave/fxrstor instructions.
//

typedef struct _XMM_SAVE_AREA32 {
    WORD   ControlWord;
    WORD   StatusWord;
    BYTE  TagWord;
    BYTE  Reserved1;
    WORD   ErrorOpcode;
    DWORD ErrorOffset;
    WORD   ErrorSelector;
    WORD   Reserved2;
    DWORD DataOffset;
    WORD   DataSelector;
    WORD   Reserved3;
    DWORD MxCsr;
    DWORD MxCsr_Mask;
    M128A FloatRegisters[8];
    M128A XmmRegisters[16];
    BYTE  Reserved4[96];
} XMM_SAVE_AREA32, *PXMM_SAVE_AREA32;

#define LEGACY_SAVE_AREA_LENGTH sizeof(XMM_SAVE_AREA32)

//
// Context Frame
//
//  This frame has a several purposes: 1) it is used as an argument to
//  NtContinue, 2) is is used to construct a call frame for APC delivery,
//  and 3) it is used in the user level thread creation routines.
//
//
// The flags field within this record controls the contents of a CONTEXT
// record.
//
// If the context record is used as an input parameter, then for each
// portion of the context record controlled by a flag whose value is
// set, it is assumed that that portion of the context record contains
// valid context. If the context record is being used to modify a threads
// context, then only that portion of the threads context is modified.
//
// If the context record is used as an output parameter to capture the
// context of a thread, then only those portions of the thread's context
// corresponding to set flags will be returned.
//
// CONTEXT_CONTROL specifies SegSs, Rsp, SegCs, Rip, and EFlags.
//
// CONTEXT_INTEGER specifies Rax, Rcx, Rdx, Rbx, Rbp, Rsi, Rdi, and R8-R15.
//
// CONTEXT_SEGMENTS specifies SegDs, SegEs, SegFs, and SegGs.
//
// CONTEXT_DEBUG_REGISTERS specifies Dr0-Dr3 and Dr6-Dr7.
//
// CONTEXT_MMX_REGISTERS specifies the floating point and extended registers
//     Mm0/St0-Mm7/St7 and Xmm0-Xmm15).
//

typedef struct __declspec(align(16))  _CONTEXT {

    //
    // Register parameter home addresses.
    //
    // N.B. These fields are for convenience - they could be used to extend the
    //      context record in the future.
    //

    DWORD64 P1Home;
    DWORD64 P2Home;
    DWORD64 P3Home;
    DWORD64 P4Home;
    DWORD64 P5Home;
    DWORD64 P6Home;

    //
    // Control flags.
    //

    DWORD ContextFlags;
    DWORD MxCsr;

    //
    // Segment Registers and processor flags.
    //

    WORD   SegCs;
    WORD   SegDs;
    WORD   SegEs;
    WORD   SegFs;
    WORD   SegGs;
    WORD   SegSs;
    DWORD EFlags;

    //
    // Debug registers
    //

    DWORD64 Dr0;
    DWORD64 Dr1;
    DWORD64 Dr2;
    DWORD64 Dr3;
    DWORD64 Dr6;
    DWORD64 Dr7;

    //
    // Integer registers.
    //

    DWORD64 Rax;
    DWORD64 Rcx;
    DWORD64 Rdx;
    DWORD64 Rbx;
    DWORD64 Rsp;
    DWORD64 Rbp;
    DWORD64 Rsi;
    DWORD64 Rdi;
    DWORD64 R8;
    DWORD64 R9;
    DWORD64 R10;
    DWORD64 R11;
    DWORD64 R12;
    DWORD64 R13;
    DWORD64 R14;
    DWORD64 R15;

    //
    // Program counter.
    //

    DWORD64 Rip;

    //
    // Floating point state.
    //

    union {
        XMM_SAVE_AREA32 FltSave;
        struct {
            M128A Header[2];
            M128A Legacy[8];
            M128A Xmm0;
            M128A Xmm1;
            M128A Xmm2;
            M128A Xmm3;
            M128A Xmm4;
            M128A Xmm5;
            M128A Xmm6;
            M128A Xmm7;
            M128A Xmm8;
            M128A Xmm9;
            M128A Xmm10;
            M128A Xmm11;
            M128A Xmm12;
            M128A Xmm13;
            M128A Xmm14;
            M128A Xmm15;
        };
    };

    //
    // Vector registers.
    //

    M128A VectorRegister[26];
    DWORD64 VectorControl;

    //
    // Special debug control registers.
    //

    DWORD64 DebugControl;
    DWORD64 LastBranchToRip;
    DWORD64 LastBranchFromRip;
    DWORD64 LastExceptionToRip;
    DWORD64 LastExceptionFromRip;
} CONTEXT, *PCONTEXT;
#else  //_AMD64_
#define SIZE_OF_80387_REGISTERS      80
#define MAXIMUM_SUPPORTED_EXTENSION     512

typedef struct _FLOATING_SAVE_AREA {
    UINT32   ControlWord;
    UINT32   StatusWord;
    UINT32   TagWord;
    UINT32   ErrorOffset;
    UINT32   ErrorSelector;
    UINT32   DataOffset;
    UINT32   DataSelector;
    UINT8    RegisterArea[SIZE_OF_80387_REGISTERS];
    UINT32   Cr0NpxState;
} FLOATING_SAVE_AREA;

typedef FLOATING_SAVE_AREA *PFLOATING_SAVE_AREA;

//
// Context Frame
//
//  This frame has a several purposes: 1) it is used as an argument to
//  NtContinue, 2) is is used to construct a call frame for APC delivery,
//  and 3) it is used in the user level thread creation routines.
//
//  The layout of the record conforms to a standard call frame.
//

typedef struct _CONTEXT {

    //
    // The flags values within this flag control the contents of
    // a CONTEXT record.
    //
    // If the context record is used as an input parameter, then
    // for each portion of the context record controlled by a flag
    // whose value is set, it is assumed that that portion of the
    // context record contains valid context. If the context record
    // is being used to modify a threads context, then only that
    // portion of the threads context will be modified.
    //
    // If the context record is used as an IN OUT parameter to capture
    // the context of a thread, then only those portions of the thread's
    // context corresponding to set flags will be returned.
    //
    // The context record is never used as an OUT only parameter.
    //

    UINT32 ContextFlags;

    //
    // This section is specified/returned if CONTEXT_DEBUG_REGISTERS is
    // set in ContextFlags.  Note that CONTEXT_DEBUG_REGISTERS is NOT
    // included in CONTEXT_FULL.
    //

    UINT32   Dr0;
    UINT32   Dr1;
    UINT32   Dr2;
    UINT32   Dr3;
    UINT32   Dr6;
    UINT32   Dr7;

    //
    // This section is specified/returned if the
    // ContextFlags word contains the flag CONTEXT_FLOATING_POINT.
    //

    FLOATING_SAVE_AREA FloatSave;

    //
    // This section is specified/returned if the
    // ContextFlags word contains the flag CONTEXT_SEGMENTS.
    //

    UINT32   SegGs;
    UINT32   SegFs;
    UINT32   SegEs;
    UINT32   SegDs;

    //
    // This section is specified/returned if the
    // ContextFlags word contains the flag CONTEXT_INTEGER.
    //

    UINT32   Edi;
    UINT32   Esi;
    UINT32   Ebx;
    UINT32   Edx;
    UINT32   Ecx;
    UINT32   Eax;

    //
    // This section is specified/returned if the
    // ContextFlags word contains the flag CONTEXT_CONTROL.
    //

    UINT32   Ebp;
    UINT32   Eip;
    UINT32   SegCs;              // MUST BE SANITIZED
    UINT32   EFlags;             // MUST BE SANITIZED
    UINT32   Esp;
    UINT32   SegSs;

    //
    // This section is specified/returned if the ContextFlags word
    // contains the flag CONTEXT_EXTENDED_REGISTERS.
    // The format and contexts are processor specific
    //

    UINT8    ExtendedRegisters[MAXIMUM_SUPPORTED_EXTENSION];

} CONTEXT;
#endif // _AMD64_ 


typedef CONTEXT *PCONTEXT;

//-----> From winnt.h#3526
#define EXCEPTION_MAXIMUM_PARAMETERS 15 // maximum number of exception parameters

//-----> From winnt.h#3566

//
// Exception record definition.
//

typedef struct _EXCEPTION_RECORD {
    UINT32    ExceptionCode;
    UINT32 ExceptionFlags;
    struct _EXCEPTION_RECORD *ExceptionRecord;
    PVOID ExceptionAddress;
    UINT32 NumberParameters;
    ULONG_PTR ExceptionInformation[EXCEPTION_MAXIMUM_PARAMETERS];
    } EXCEPTION_RECORD;

typedef EXCEPTION_RECORD *PEXCEPTION_RECORD;

typedef struct _EXCEPTION_RECORD32 {
    UINT32    ExceptionCode;
    UINT32 ExceptionFlags;
    UINT32 ExceptionRecord;
    UINT32 ExceptionAddress;
    UINT32 NumberParameters;
    UINT32 ExceptionInformation[EXCEPTION_MAXIMUM_PARAMETERS];
} EXCEPTION_RECORD32, *PEXCEPTION_RECORD32;

typedef struct _EXCEPTION_RECORD64 {
    UINT32    ExceptionCode;
    UINT32 ExceptionFlags;
    UINT64 ExceptionRecord;
    UINT64 ExceptionAddress;
    UINT32 NumberParameters;
    UINT32 __unusedAlignment;
    UINT64 ExceptionInformation[EXCEPTION_MAXIMUM_PARAMETERS];
} EXCEPTION_RECORD64, *PEXCEPTION_RECORD64;

//
// Typedef for pointer returned by exception_info()
//

typedef struct _EXCEPTION_POINTERS {
    PEXCEPTION_RECORD ExceptionRecord;
    PCONTEXT ContextRecord;
} EXCEPTION_POINTERS, *PEXCEPTION_POINTERS;

#pragma pack(pop)

//
//////////////////////////////////////////////////////////////////////////////
