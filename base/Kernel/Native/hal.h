///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   hal.h
//
//  Note:
//

#ifndef __hal_h_
#define __hal_h_

#define UNICODE
#define _UNICODE

//////////////////////////////////////////////////////////////////////////////
//
#define PTR_SIZE_32 1

/////////////////////////////////////////// Core types used by runtime system.
//

typedef __wchar_t           bartok_char;

typedef char                int8;
typedef short               int16;
typedef int                 int32;
typedef __int64             int64;

typedef unsigned char       uint8;
typedef unsigned short      uint16;
typedef unsigned int        uint32;
typedef unsigned __int64    uint64;

typedef float               float32;
typedef double              float64;

#if defined(PTR_SIZE_32)
typedef int                 intptr;
typedef unsigned int        uintptr;
#else
typedef __int64             intptr;
typedef unsigned __int64    uintptr;
#endif

struct uintPtr
{
    uintptr value;
};

struct intPtr
{
    intptr value;
};

typedef struct uintPtr *UIntPtr;
typedef struct intPtr *IntPtr;

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

//////////////////////////////////////////////////////////////////////////////
//
typedef signed int INT;
typedef signed char INT8;
typedef signed short INT16;
typedef signed long INT32;
typedef signed __int64 INT64;
typedef unsigned int UINT;
typedef unsigned char UINT8;
typedef unsigned short UINT16;
typedef unsigned long UINT32;
typedef unsigned __int64 UINT64;

typedef wchar_t WCHAR;
typedef unsigned char UCHAR;

typedef void *PVOID;
typedef uintptr ULONG_PTR;
typedef unsigned long   DWORD;
typedef uint64 ULARGEST;
typedef int64 LARGEST;

typedef struct
{
    UINT64  _lo;
    UINT64  _hi;
} UINT128;

#define NULL 0

#if SINGULARITY_KERNEL

#ifndef _VA_LIST_DEFINED
typedef char *va_list;
#define _VA_LIST_DEFINED

#define _INTSIZEOF(n)    ( (sizeof(n) + sizeof(int) - 1) & ~(sizeof(int) - 1) )
#define va_start(ap,v) ap = (va_list)&v + _INTSIZEOF(v)
#define va_arg(ap,t) ( *(t *)((ap += _INTSIZEOF(t)) - _INTSIZEOF(t)) )
#define va_end(ap) ap = (va_list)0
#endif

int printf(const char *pszFmt, ...);

void IdtInitialize();
void IdtLoad();

struct Struct_Microsoft_Singularity_CpuInfo;
void ProcessorInitialize(const Struct_Microsoft_Singularity_CpuInfo* cpu,
                         int cpuId);
int GetCurrentProcessorNumber();

#ifndef MAX_CPU
#define MAX_CPU 1
#endif  // MAX_CPU

#endif // SINGULARITY_KERNEL

//////////////////////////////////////////////////////////////////////////////
//
typedef int BOOL;

#if SINGULARITY_KERNEL

#include "winctx.h"
#include "minidump.h"

#elif SINGULARITY_PROCESS

//////////////////////////////////////////////////////////////////////////////
//
typedef int BOOL;

struct Struct_System_ObjectHeader
{
    int32  syncBlockValue;
};

#define MAKE_STRING(s,v) \
struct _##s \
{ \
    Struct_System_ObjectHeader header; \
    union \
    { \
        struct \
        { \
            Class_System_VTable * vable; \
            int32 arrayLength; \
            int32 stringLength; \
            wchar_t chars[sizeof(v)]; \
        }; \
        Class_System_String string; \
    }; \
} s = { \
    {}, \
    (Class_System_VTable *)&Class_System_String::_vtable, \
    sizeof(v), \
    sizeof(v) - 1, \
    L##v \
}

#endif // SINGULARITY_PROCESS

//////////////////////////////////////////////////////////////////////////////

#if     DEBUG

#if SINGULARITY_KERNEL
extern void fail_assert(const char *expr);
#define __fail_assert(expr, file, line) fail_assert("assert(" #expr ") failed at " file ":" #line)
#define Assert(expr) { if (!(expr)) { __fail_assert(expr, __FILE__, __LINE__); } }
#elif SINGULARITY_PROCESS
extern void fail_assert(Class_System_String *message);
#define Assert(expr) { if (!(expr)) { static MAKE_STRING(msg, "assert(" #expr ") failed at " __FILE__ ":" __LINE__); fail_assert(&msg.string); } }
#endif // SINGULARITY_KERNEL

#else //DEBUG
#define Assert(expr) { 0; }
#endif//DEBUG

#pragma warning(disable: 4201)  // Allow nameless struct/union
#pragma warning(disable:4127)   // 4127: warning about constant conditional

#define EXCEPTION_ACCESS_VIOLATION      1
#define EXCEPTION_CONTINUE_EXECUTION    2
#define EXCEPTION_CONTINUE_SEARCH       3
#define EXCEPTION_FLT_DIVIDE_BY_ZERO    4
#define EXCEPTION_INT_DIVIDE_BY_ZERO    5
#define EXCEPTION_INT_OVERFLOW          6
#define EXCEPTION_STACK_OVERFLOW        7

//////////////////////////////////////////////////////////////////////////////

// Used to pass various data through "int 29"
struct KdDebugTrapData
{
    enum Tag
    {
        FIRST_CHANCE_EXCEPTION,
        LOADED_BINARY,
        UNLOADED_BINARY
    } tag;
    union
    {
        struct
        {
            uintptr throwAddr;
        } firstChanceException;
        struct
        {
            UIntPtr baseAddress;
            UIntPtr bytes;
            UIntPtr name;
            uint32 checksum;
            uint32 timestamp;
            bool silent;
            bool ret;
        } loadedBinary;
        struct
        {
            UIntPtr baseAddress;
            bool silent;
            bool ret;
        } unloadedBinary;
    } u;
};

//////////////////////////////////////////////////////////////////////////////
//
#pragma warning(disable: 4103)
#pragma pack(push, 4)
#include "halclass.h"
#pragma pack(pop)

#if SINGULARITY_KERNEL
extern const Struct_Microsoft_Singularity_BootInfo *g_pBootInfo;
#endif // SINGULARITY_KERNEL

//////////////////////////////////////////////////////////////////////////////
//
struct ClassVector : Class_System_Object
{
    uint32          length;
};

struct ClassVector_uint8 : ClassVector
{
    static struct Class_System_VTable ClassVector_uint8::_vtable;

    uint8           values[1];
};

struct ClassVector_ClassVector_uint8 : ClassVector
{
    static struct Class_System_VTable ClassVector_ClassVector_uint8::_vtable;

    ClassVector_uint8 * values[1];
};

struct ClassVector_bartok_char : ClassVector
{
    static struct Class_System_VTable ClassVector_bartok_char::_vtable;

    bartok_char     values[1];
};


// Routine to read Pentium time stamp counter
_inline _declspec( naked ) UINT64 RDTSC()
{
    __asm {
        rdtsc;
        ret;
    }
}

_inline _declspec(naked) UINT32 DisableInterrupts()
{
    __asm {
        pushfd;
        pop eax;
        cli;
        ret;
    }
}

_inline _declspec(naked) void EnableInterrupts()
{
    __asm {
        sti;
        ret;
    }
}

_inline void RestoreInterrupts(UINT32 fd)
{
    if ((fd & Struct_Microsoft_Singularity_X86_EFlags_IF) != 0) {
        __asm sti;
    }
}

/////////////////////////////////////////////////////// Interlocked Intrinsics.
//

#define InterlockedIncrement              _InterlockedIncrement
#define InterlockedDecrement              _InterlockedDecrement
#define InterlockedExchange               _InterlockedExchange
#define InterlockedExchangeAdd            _InterlockedExchangeAdd
#define InterlockedCompareExchange        _InterlockedCompareExchange

extern "C"
{

INT32
__cdecl
InterlockedIncrement(
    INT32 volatile *lpAddend
    );

INT32
__cdecl
InterlockedDecrement(
    INT32 volatile *lpAddend
    );

INT32
__cdecl
InterlockedExchange(
    INT32 volatile *Target,
    INT32 Value
    );

INT32
__cdecl
InterlockedExchangeAdd(
    INT32 volatile *Addend,
    INT32 Value
    );

INT32
__cdecl
InterlockedCompareExchange (
    INT32 volatile *Destination,
    INT32 ExChange,
    INT32 Comperand
    );

#pragma intrinsic(_InterlockedIncrement)
#pragma intrinsic(_InterlockedDecrement)
#pragma intrinsic(_InterlockedExchange)
#pragma intrinsic(_InterlockedExchangeAdd)
#pragma intrinsic(_InterlockedCompareExchange)
}

//////////////////////////////////////////////////////////////////////////////

#define arrayof(a)      (sizeof(a)/sizeof(a[0]))
#define offsetof(s,m)   (size_t)&(((s *)0)->m)

#pragma warning(disable: 4100)  // allow unreferenced formal parameters

#endif // __hal_h_

//
////////////////////////////////////////////////////////////////// End of File.
