//////////////////////////////////////////////////////////////////////////////
//
//  undump.cpp - Minidump Kernel Expander
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
typedef int INT;
typedef char INT8, *PINT8;
typedef short INT16, *PINT16;
typedef long INT32, *PINT32;
typedef __int64 INT64, *PINT64;
typedef long LARGEST;
typedef char CHAR, *PCHAR;
typedef unsigned int UINT;
typedef unsigned char UINT8, *PUINT8;
typedef unsigned short UINT16, *PUINT16;
typedef unsigned long UINT32, *PUINT32;
typedef unsigned __int64 UINT64, *PUINT64;
typedef unsigned long ULARGEST;
typedef unsigned short WCHAR;
typedef unsigned char UCHAR;
typedef int BOOL;
typedef void *PVOID;
typedef UINT32 ULONG_PTR;
typedef UINT32 UINTPTR;

#define NULL    0
#define arrayof(a)      (sizeof(a)/sizeof(a[0]))
#define offsetof(s,m)   (size_t)&(((s *)0)->m)

#define DO_ACPI    1
//#define DEBUG 1

//////////////////////////////////////////////////////////////////////////////
//
/////////////////////////////////////////// Core types used by runtime system.
//

extern "C" ULONG_PTR _security_cookie = 0;

typedef __wchar_t           bartok_char;

typedef signed char         int8;
typedef signed short        int16;
typedef signed int          int32;
typedef __int64             int64;

typedef unsigned char       uint8;
typedef unsigned short      uint16;
typedef unsigned int        uint32;
typedef unsigned __int64    uint64;

typedef float               float32;
typedef double              float64;

typedef int                 intptr;
typedef unsigned int        uintptr;

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

//////////////////////////////////////////////////////////////////////////////
//
#pragma warning(disable: 4103)
#pragma pack(push, 4)
#include "halclass.h"
#pragma pack(pop)

#include "winctx.h"
#include "minidump.h"

//////////////////////////////////////////////////////////////////////////////
//
#define TEST 1

#if TEST

typedef unsigned int uint;
typedef unsigned int UINT;
typedef signed long LARGEST;
typedef unsigned long ULARGEST;

#include "printf.cpp"
#include "debug.cpp"

UINT64 __fastcall RDMSR(UINT32 msr)
{
    __asm rdmsr;
}

void WRMSR(UINT32 msr, UINT64 value)
{
    __asm {
        lea ebx, value;
        mov eax, [ebx+0];
        mov edx, [ebx+4];
        mov ecx, msr;
        wrmsr;
    }
}

// Routine to read Pentium time stamp counter
_inline _declspec( naked ) UINT64 RDTSC()
{
    __asm {
        rdtsc;
        ret;
    }
}

void CPUID(UINT32 feature, UINT32 *output)
{
    __asm {
        mov eax, feature;
        cpuid;
        mov edi, output;
        mov [edi+0], eax;
        mov [edi+4], ebx;
        mov [edi+8], ecx;
        mov [edi+12], edx;
    }
}

void IoSpaceWrite8(UINT16 This, UINT8 Value)
{
    __asm {
        mov dx,This;
        mov al,Value;
        out dx,al;
    }
}

UINT8 IoSpaceRead8(UINT16 Port)
{
    UINT8 Value;
    __asm {
        mov dx, Port;
        in  al, dx;
        mov Value, al;
    }
    return Value;
}

void FpuInit()
{
    __asm fninit;
}

UINT8 Sum(UINT8 * pbData, int cbData)
{
    UINT8 sum = 0;

    while (cbData-- > 0) {
        sum += *pbData++;
    }
    return sum;
}

int Checksum(UINT8 * pbData, int cbData)
{
    return (Sum(pbData, cbData) == 0);
}

///////////////////////////////////////////////////////////// Screen Routines.
//
static UINT16 s_wAttr = 0x1f00;

void Dim()
{
    s_wAttr = 0x1800;
}

void Yellow()
{
    s_wAttr = 0x1e00;
}

void Green()
{
    s_wAttr = 0x1a00;
}

void Red()
{
    s_wAttr = 0x1c00;
}

void Normal()
{
    s_wAttr = 0x1f00;
}

static UINT nCursor = 0;

void Halt()
{
    Yellow();
    printf("Halt.");
    __asm hlt;
}

void Cls()
{
    BdPrintString("-------------------------------------------------------------------------------\n", 80);

    for (UINT16 n = 0; n < 4000; n++)
    {
        ((UINT16*)0xb8000)[n] = s_wAttr | ' ';
    }

    nCursor = 0;
}

void __cdecl PutChar(char cOut)
{
    if (cOut == '\r')
    {
        nCursor -= nCursor % 80;
    }
    else if (cOut == '\n')
    {
        do {
            ((UINT16*)0xb8000)[nCursor++] = s_wAttr | ' ';
        } while ((nCursor % 80) != 0);
    }
    else
    {
        ((UINT16*)0xb8000)[nCursor++] = (UINT16)(s_wAttr | cOut);
    }

    while (nCursor >= 4000)
    {
        UINT16 n = 0;
        for (; n < 4000 - 80; n++)
        {
            ((UINT16*)0xb8000)[n] = ((UINT16*)0xb8000)[n + 80];
        }
        for (; n < 4000; n++)
        {
            ((UINT16*)0xb8000)[n] = s_wAttr | ' ';
        }
        nCursor -= 80;
    }

    IoSpaceWrite8(0x3d4, 0xe);
    IoSpaceWrite8(0x3d5, (UINT8)(nCursor >> 8));
    IoSpaceWrite8(0x3d4, 0xf);
    IoSpaceWrite8(0x3d5, (UINT8)(nCursor & 0xff));

    static CHAR szBuffer[256];
    static INT nBuffer = 0;

    szBuffer[nBuffer++] = cOut;
    if (cOut == '\n' || nBuffer >= sizeof(szBuffer) - 1) {
        BdPrintString(szBuffer, nBuffer);
        nBuffer = 0;
    }
}

void Probe(UINT8 * pbData, UINT cbData)
{
    UINT8 b;

    for (UINT i = 0; i < cbData; i++) {
        b = pbData[i];
        pbData[i] = b;
    }
}

void Dump(UINT8 * pbData, UINT cbData)
{
    for (UINT n = 0; n < cbData; n += 16) {
        printf("  %08x:", pbData + n);
        UINT o = n;
        for (; o < n + 16; o++) {
            if (o >= cbData) {
                printf("  ");
            }
            else {
                printf("%02x", pbData[o]);
            }
            if (o % 4 == 3) {
                printf(" ");
            }
        }
        printf(" ");
        for (o = n; o < n + 16; o++) {
            if (o >= cbData) {
                printf("  ");
            }
            else {
                if (pbData[o] >= ' ' && pbData[o] < 127) {
                    printf("%c", pbData[o]);
                }
                else {
                    printf(".");
                }
            }
        }
        printf("\n");
    }
}

void Dump(UINT8 * pbData, UINT8 * pbLimit, UINT cbMax)
{
    UINT cbData = (uint) (pbLimit - pbData);
    if (cbData > cbMax) {
        cbData = cbMax;
    }
    Dump(pbData, cbData);
}

#endif


static void CopyUp(UINT8 *pbDst, UINT8 *pbSrc, UINT32 cbSrc)
{
    //!!! pbDst >= pbSrc
    if (pbDst < pbSrc) {
        printf("CopyUp(dst=%p < src=%p)\n", pbDst, pbSrc);
        Halt();
    }

    INT32 nSrc = (cbSrc + 3) / 4;
    UINT32 *pDst = ((UINT32*)pbDst) + nSrc;
    UINT32 *pSrc = ((UINT32*)pbSrc) + nSrc;

    while (nSrc-- > 0) {
        *--pDst = *--pSrc;
    }
}

static void CopyDown(UINT8 *pbDst, UINT8 *pbSrc, UINT32 cbSrc)
{
    //!!! pbDst <= pbSrc
    if (pbDst > pbSrc) {
        printf("CopyDown(dst=%p > src=%p)\n", pbDst, pbSrc);
        Halt();
    }

    INT32 nSrc = (cbSrc + 3) / 4;
    volatile UINT32 *pDst = (UINT32*)pbDst;
    volatile UINT32 *pSrc = (UINT32*)pbSrc;

    while (nSrc-- > 0) {
        *pDst = *pSrc;
        if (*pDst != *pSrc) {
            printf("CopyDown error at %p/%p : %08x != %08x\n",
                   pDst, pSrc, *pDst, *pSrc);
            Halt();
        }
        pDst++;
        pSrc++;
    }
}

const char * SmapTypeToString(int type)
{
    switch (type) {
      case 1: return "RAM     ";
      case 2: return "Reserved";
      case 3: return "ACPI RAM";
      case 4: return "ACPI NVS";
      default: return "Other   ";
    }
}

bool check(UINT8 *pbCache, UINT8 value, const char *pszDesc)
{
    for (int i = 0; i < 16; i++) {
        if (pbCache[i] == value) {
            pbCache[i] = 0;
            printf("      %s\n", pszDesc);
            return 1;
        }
    }
    return 0;
}

static int wcseq(UINT16 * pwzData, UINT16 * pwzKnown)
{
    while (*pwzData && *pwzKnown && *pwzData == *pwzKnown) {
        pwzData++;
        pwzKnown++;
    }
    return (*pwzData == *pwzKnown);
}

static int wcslen(UINT16 *pwz)
{
    int len = 0;

    while (*pwz++) {
        len++;
    }
    return len;
}

static int wcsfindcmd(UINT16 *pwzCmd, UINT16 *pwzKnown)
{
    for (;;) {
        int len = wcslen(pwzCmd);
        if (len == 0) {
            return 0;
        }
        if (wcseq(pwzCmd, pwzKnown) != 0) {
            return 1;
        }

        pwzCmd += len + 1;
    }
}

#if DO_ACPI
#include "acpi/acpi.cpp"
#endif

static void CheckPci(const Struct_Microsoft_Singularity_BootInfo *bi)
{
    ///////////////////////////////////////////////// Check for Compatibility.
    //
    if ((bi->PciBiosAX >> 8) != 0 || (bi->PciBiosEDX != 0x20494350)) {
        Yellow();
        printf("Hardware does not support PCI V2.x.\n");
        printf("PCI V2.x: AX:%04x, BX:%04x, CX:%04x, EDX:%08x\n",
               bi->PciBiosAX, bi->PciBiosBX, bi->PciBiosCX, bi->PciBiosEDX);
        Halt();
    }
    if (!(bi->PciBiosAX & 0x01)) {
        Yellow();
        printf("Hardware does not support multiple PCI buses.\n");
        printf("PCI V2.x: AX:%04x, BX:%04x, CX:%04x, EDX:%08x\n",
               bi->PciBiosAX, bi->PciBiosBX, bi->PciBiosCX, bi->PciBiosEDX);
        Halt();
    }

}

static bool CheckSmapForRam(const Struct_Microsoft_Singularity_BootInfo *bi, UINT64 base, UINT64 size)
{
    Struct_Microsoft_Singularity_SMAPINFO *sm = (Struct_Microsoft_Singularity_SMAPINFO *)bi->SmapData32;
    for (UINT i = 0; i < bi->SmapCount; i++) {
        if (sm[i].type == 1 &&
            sm[i].addr <= base &&
            sm[i].addr + sm[i].size >= base + size) {

            return true;
        }
    }
    return false;
}

static void CheckSmap(const Struct_Microsoft_Singularity_BootInfo *bi)
{
    /////////////////////////////////////////////////////// System Memory Map.
    //
    printf("        Base Address    Limit Address Type      \n");
    printf("    ================ ================ ==========\n");

    Struct_Microsoft_Singularity_SMAPINFO *sm = (Struct_Microsoft_Singularity_SMAPINFO *)bi->SmapData32;
    for (UINT i = 0; i < bi->SmapCount; i++) {
        printf("    %16lx", (UINT64)sm[i].addr);
        printf(" %16lx", (UINT64)sm[i].addr + (UINT64)sm[i].size);
        printf(" %d:%s\n",
               sm[i].type,
               SmapTypeToString((int)sm[i].type));
    }
    printf("\n");
}

static void DumpFeatures(UINT32 features)
{
    printf("      ");
    if (features & 0x1) { printf("x87 "); }
    if (features & 0x2) { printf("VME "); }
    if (features & 0x4) { printf("DBG "); }
    if (features & 0x8) { printf("PSE "); }
    if (features & 0x10) { printf("TSC "); }
    if (features & 0x20) { printf("MSR "); }
    if (features & 0x40) { printf("PAE "); }
    if (features & 0x80) { printf("MCE "); }
    if (features & 0x100) { printf("CX8 "); }
    if (features & 0x200) { printf("APIC "); }
    if (features & 0x200) { printf("R0 "); }
    if (features & 0x800) { printf("SEP "); }
    if (features & 0x1000) { printf("MTRR "); }
    if (features & 0x2000) { printf("PGE "); }
    if (features & 0x4000) { printf("MCA "); }
    if (features & 0x8000) { printf("CMOV "); }
    printf("\n      ");
    if (features & 0x10000) { printf("PAT "); }
    if (features & 0x20000) { printf("PSE36 "); }
    if (features & 0x40000) { printf("PSN "); }
    if (features & 0x80000) { printf("CFLUSH "); }
    if (features & 0x100000) { printf("R1 "); }
    if (features & 0x200000) { printf("DS "); }
    if (features & 0x400000) { printf("ACPI "); }
    if (features & 0x800000) { printf("MMX "); }
    if (features & 0x1000000) { printf("FXSR "); }
    if (features & 0x2000000) { printf("SSE "); }
    if (features & 0x4000000) { printf("SSE2 "); }
    if (features & 0x8000000) { printf("SS "); }
    if (features & 0x10000000) { printf("HTT "); }
    if (features & 0x20000000) { printf("TM "); }
    if (features & 0x40000000) { printf("R2 "); }
    if (features & 0x80000000) { printf("PBE "); }
    printf("\n");
}

static void CheckCpuid(const Struct_Microsoft_Singularity_BootInfo *bi)
{
    /////////////////////////////////////////////////////////////////// CPUID.
#if 1
    {
        UINT32 output[4];

        CPUID(1, output);

        UINT family = (output[0] & 0xf00) >> 8;
        UINT model = (output[0] & 0x0f0) >> 4;
        UINT step = (output[0] & 0x00f);
        UINT brand = (output[1] & 0xff);

        if (family == 0xf) {
            family += (output[0] & 0xff00000) >> 24;
        }
        if (model == 0xf) {
            model +=  (output[0] & 0xf0000) >> 16;
        }

        CPUID(0, output);
        printf("CPUID: %c%c%c%c%c%c%c%c%c%c%c%c Family %d, Model %d, Step %d, Brand %d\n",
               ((char *)output)[4],
               ((char *)output)[5],
               ((char *)output)[6],
               ((char *)output)[7],
               ((char *)output)[12],
               ((char *)output)[13],
               ((char *)output)[14],
               ((char *)output)[15],
               ((char *)output)[8],
               ((char *)output)[9],
               ((char *)output)[10],
               ((char *)output)[11],
               family, model, step, brand);

        UINT32 max = output[0];
        for (UINT32 i = 3; i <= max; i++) {
            CPUID(i, output);
            printf("    %08x: %08x %08x %08x %08x\n",
                   i, output[0], output[1], output[2], output[3]);
        }

        CPUID(1, output);
#if DEBUG
        if ((output[1] & 0xff00) != 0) {
            printf("      CFlush %d bytes\n", ((output[1] & 0xff00) >> 8) * 8);
        }
        if ((output[1] & 0xff0000) != 0) {
            printf("      Hyperthreads: %d\n", (output[1] & 0xff0000) >> 16);
        }
        if ((output[1] & 0xff000000) != 0xff) {
            printf("      Local APIC Id: %d\n", (output[1] & 0xff000000) >> 24);
        }
#endif

        printf("    Features: ");
        if (output[2] & 0x80) { printf("EST "); }
        if (output[2] & 0x100) { printf("TM2 "); }
        if (output[2] & 0x400) { printf("L1C "); }
        printf("\n");
        DumpFeatures(output[3]);

#if DEBUG
        printf("    Caches:\n");

        CPUID(2, output);
        for (int j = 0; j < 4; j++) {
            if (output[j] & 0x80000000) {
                output[j] = 0;
            }
        }
        output[0] &= 0xffffff00;

        UINT8 *pbCache = (UINT8*)output;

        check(pbCache, 0x01, "ITLB: 4KB Pages, 4-way, 32 entries");
        check(pbCache, 0x02, "ITLB: 4MB Pages, 4-way, 2 entries");
        check(pbCache, 0x03, "DTLB: 4KB Pages, 4-way, 64 entries");
        check(pbCache, 0x04, "DTLB: 4MB Pages, 4-way, 8 entries");
        check(pbCache, 0x06, "L1-I: 8KB, 4-way, 32 byte line size");
        check(pbCache, 0x08, "L1-I: 16KB, 4-way, 32 byte line size");
        check(pbCache, 0x0A, "L1-D: 8KB, 2-way, 32 byte line size");
        check(pbCache, 0x0C, "L1-D: 16KB, 4-way, 32 byte line size");
        check(pbCache, 0x22, "L3  : 512KB, 4-way, 64 byte line size, 128 byte sector size");
        check(pbCache, 0x23, "L3  : 1MB, 8-way, 64 byte line size, 128 byte sector size");
        check(pbCache, 0x25, "L3  : 2MB, 8-way, 64 byte line size, 128 byte sector size");
        check(pbCache, 0x29, "L3  : 4MB, 8-way, 64 byte line size, 128 byte sector size");
        check(pbCache, 0x2C, "L1-D: 32KB, 8-way, 64 byte line size");
        check(pbCache, 0x30, "L1-I: 32KB, 8-way, 64 byte line size");
        check(pbCache, 0x40, "----: No L2 or, if processor contains a valid L2, no L3");
        check(pbCache, 0x41, "L2  : 128KB, 4-way, 32 byte line size");
        check(pbCache, 0x42, "L2  : 256KB, 4-way, 32 byte line size");
        check(pbCache, 0x43, "L2  : 512KB, 4-way, 32 byte line size");
        check(pbCache, 0x44, "L2  : 1MB, 4-way, 32 byte line size");
        check(pbCache, 0x45, "L2  : 2MB, 4-way, 32 byte line size");
        check(pbCache, 0x50, "ITLB: 4KB and 4MB pages, 64 entries");
        check(pbCache, 0x51, "ITLB: 4KB and 4MB pages, 128 entries");
        check(pbCache, 0x52, "ITLB: 4KB and 4MB pages, 256 entries");
        check(pbCache, 0x5B, "DTLB: 4KB and 4MB pages, 64 entries");
        check(pbCache, 0x5C, "DTLB: 4KB and 4MB pages, 128 entries");
        check(pbCache, 0x5D, "DTLB: 4KB and 4MB pages, 256 entries");
        check(pbCache, 0x60, "L1-D: 16KB, 8-way, 64 byte line size");
        check(pbCache, 0x66, "L1-D: 8KB, 4-way, 64 byte line size");
        check(pbCache, 0x67, "L1-D: 16KB, 4-way, 64 byte line size");
        check(pbCache, 0x68, "L1-D: 32KB, 4-way, 64 byte line size");
        check(pbCache, 0x70, "TC  : 12K-uop, 8-way");
        check(pbCache, 0x71, "TC  : 16K-uop, 8-way");
        check(pbCache, 0x72, "TC  : 32K-uop, 8-way");
        check(pbCache, 0x78, "L2  : 1MB, 8-way, 64 byte line size");
        check(pbCache, 0x79, "L2  : 128KB, 8-way, 64 byte line size, 128 byte sector size");
        check(pbCache, 0x7A, "L2  : 256KB, 8-way, 64 byte line size, 128 byte sector size");
        check(pbCache, 0x7B, "L2  : 512KB, 8-way, 64 byte line size, 128 byte sector size");
        check(pbCache, 0x7C, "L2  : 1MB, 8-way, 64 byte line size, 128 byte sector size");
        check(pbCache, 0x7D, "L2  : 2MB, 8-way, 64 byte line size");
        check(pbCache, 0x7F, "L2  : 512KB, 2-way, 64 byte line size");
        check(pbCache, 0x82, "L2  : 256KB, 8-way, 32 byte line size");
        check(pbCache, 0x83, "L2  : 512KB, 8-way, 32 byte line size");
        check(pbCache, 0x84, "L2  : 1MB, 8-way, 32 byte line size");
        check(pbCache, 0x85, "L2  : 2MB, 8-way, 32 byte line size");
        check(pbCache, 0x86, "L2  : 512KB, 4-way, 64 byte line size");
        check(pbCache, 0x87, "L2  : 1MB, 8-way, 64 byte line size");
        check(pbCache, 0xB0, "ITLB: 4KB Pages, 4-way, 128 entries");
        check(pbCache, 0xB3, "DTLB: 4KB Pages, 4-way, 128 entries");
        check(pbCache, 0xF0, "----: 64 byte prefetch");
        check(pbCache, 0xF1, "----: 128 byte prefetch");

        for (int j = 0; j < 16; j++) {
            if (pbCache[j] != 0) {
                printf("      Missed %02x\n", pbCache[j]);
            }
        }
#endif

        CPUID(0x80000000, output);
        if ((output[0] & 0x80000000)) {
            UINT32 max = output[0];
#if DEBUG
            printf("Max ext CPUID = %x\n", max);
            for (UINT32 i = 0x80000005; i <= max; i++) {
                CPUID(i, output);
                printf("    %08x: %08x %08x %08x %08x\n",
                       i, output[0], output[1], output[2], output[3]);
            }
#endif

            CPUID(0x80000001, output);
            if (output[3] != 0) {
                printf("    AMD Features:\n");
                printf("      ");
                if (output[3] & 0x1) { printf("x87 "); }
                if (output[3] & 0x2) { printf("VME "); }
                if (output[3] & 0x4) { printf("DBG "); }
                if (output[3] & 0x8) { printf("PSE "); }
                if (output[3] & 0x10) { printf("TSC "); }
                if (output[3] & 0x20) { printf("MSR "); }
                if (output[3] & 0x40) { printf("PAE "); }
                if (output[3] & 0x80) { printf("MCE "); }
                if (output[3] & 0x100) { printf("CX8 "); }
                if (output[3] & 0x200) { printf("APIC "); }
                if (output[3] & 0x400) { printf("R0 "); }
                if (output[3] & 0x800) { printf("FSEP "); }
                if (output[3] & 0x1000) { printf("MTRR "); }
                if (output[3] & 0x2000) { printf("PGE "); }
                if (output[3] & 0x4000) { printf("MCA "); }
                if (output[3] & 0x8000) { printf("CMOV "); }
                printf("\n      ");
                if (output[3] & 0x10000) { printf("PAT "); }
                if (output[3] & 0x20000) { printf("PSE36 "); }
                if (output[3] & 0x40000) { printf("R1 "); }
                if (output[3] & 0x80000) { printf("R2 "); }
                if (output[3] & 0x100000) { printf("NX "); }
                if (output[3] & 0x200000) { printf("R3 "); }
                if (output[3] & 0x400000) { printf("xMMX "); }
                if (output[3] & 0x800000) { printf("MMX "); }
                if (output[3] & 0x1000000) { printf("FXSR "); }
                if (output[3] & 0x2000000) { printf("FFXSR "); }
                if (output[3] & 0x4000000) { printf("R4 "); }
                if (output[3] & 0x8000000) { printf("R5 "); }
                if (output[3] & 0x10000000) { printf("R6 "); }
                if (output[3] & 0x20000000) { printf("LM "); }
                if (output[3] & 0x40000000) { printf("x3D "); }
                if (output[3] & 0x80000000) { printf("3D "); }
                printf("\n");
            }

            CHAR szBrand[64];
            CPUID(0x80000002, (UINT32*)&szBrand[0]);
            CPUID(0x80000003, (UINT32*)&szBrand[16]);
            CPUID(0x80000004, (UINT32*)&szBrand[32]);
            szBrand[48] = '\0';
            CHAR *pszBrand = szBrand;
            while (*pszBrand == ' ') {
                pszBrand++;
            }
            printf("    Brand: %s\n", pszBrand);
        }
        printf("\n");
    }
#endif
}

static void CheckApic(const Struct_Microsoft_Singularity_BootInfo *bi)
{
    ////////////////////////////////////////////////////// Local and I/O APIC.
    //
#if DEBUG
    UINT32 apicid = 0;
    UINT32 apicver = 0;

    printf("    IO APIC:\n");
    for (UINT32 base = 0xfec00000; base <= 0xfec0fc00; base += 0x400) {
        __asm {
            mov ebx, base;
            mov eax, 0;
            mov [ebx], eax;
            mov eax, [ebx+0x10];
            mov apicid, eax;

            mov eax, 1;
            mov [ebx], eax;
            mov eax, [ebx+0x10];
            mov apicver, eax;
        }
        if (apicid == ~0u && apicver == ~0u) {
            continue;
        }
        printf("      %08x: %08x %08x\n", base, apicid, apicver);
    }

    printf("    Local APIC:\n");
    for (UINT32 base = 0xfee00000; base <= 0xfee00000; base += 0x400) {
        __asm {
            mov ebx, base;
            mov eax, [ebx+0x20];
            mov apicid, eax;
            mov eax, [ebx+0x30];
            mov apicver, eax;
        }
        printf("      %08x: id=%08x ver=%08x\n", base, apicid, apicver);
    }

    printf("      MSR[0x%02x] = %016lx\n", 0x10, RDMSR(0x10));
    //printf("      MSR[0x%02x] = %016lx\n", 0x17, RDMSR(0x17));
    //printf("      MSR[0x%02x] = %016lx\n", 0x1b, RDMSR(0x1b));

    printf("\n");
#endif
}

struct ACPI_RSDP
{
    CHAR    Signature[8];                       // "RSD PTR "
    UINT8   Checksum;
    CHAR    OemId[6];
    UINT8   Revision;
    UINT32  RsdtAddress;
    UINT32  RsdtLength;
    UINT32  XsdtAddrLo;
    UINT32  XsdtAddrHi;
    UINT8   ExtendedChecksum;
    UINT8   Reserved[3];
};
typedef ACPI_RSDP * PACPI_RSDP;

int IsAcpiRsdp(PACPI_RSDP pScan)
{
    if (pScan->Signature[0] != 'R' || pScan->Signature[1] != 'S' ||
        pScan->Signature[2] != 'D' || pScan->Signature[3] != ' ' ||
        pScan->Signature[4] != 'P' || pScan->Signature[5] != 'T' ||
        pScan->Signature[6] != 'R' || pScan->Signature[7] != ' ') {

        return 0;
    }
    return Checksum((UINT8*)pScan, 20);
}

static void CheckAcpi(Struct_Microsoft_Singularity_BootInfo *bi)
{
    /////////////////////////////////////////////////////////// Scan for ACPI.
    //
    PACPI_RSDP pAcpi = NULL;

    for (UINT8 *pbScan = (UINT8*)0xe0000; pbScan < (UINT8*)0xfffff; pbScan += 0x10) {
        PACPI_RSDP pScan = (PACPI_RSDP)pbScan;
        if (IsAcpiRsdp(pScan)) {
            pAcpi = pScan;
            break;
        }
    }

    if (pAcpi == NULL && bi->Ebda32 != NULL) {
#if 0
        printf("bi->Ebda32 = %08x\n", bi->Ebda32);
        Dump((UINT8*)bi->Ebda32, 0x400);
#endif

        for (UINT8 *pbScan = (UINT8*)bi->Ebda32;
             pbScan < (UINT8*)bi->Ebda32 + 0x400; pbScan += 0x10) {

            PACPI_RSDP pScan = (PACPI_RSDP)pbScan;
            if (IsAcpiRsdp(pScan)) {
                pAcpi = pScan;
                break;
            }
        }
    }

    bi->AcpiRoot32 = (UINT)pAcpi;

#if DO_ACPI
    if (pAcpi != NULL) {
        printf("ACPI Information:\n");
        Acpi(pAcpi);
    }
#endif
}

//////////////////////////////////////////////////////////////////////////////
//
struct MP_FLOAT
{
    UINT8       Signature[4];                          // "_MP_"
    UINT32      AddrMpConfigTable32;  //canonical form
    UINT8       Paragraphs;
    UINT8       Version;
    UINT8       Checksum;
    UINT8       Features[5];
};

struct MP_FIXED
{
    UINT8       Signature[4];
    UINT16      BaseTableSize;
    UINT8       Version;
    UINT8       Checksum;
    UINT8       OemId[8];
    UINT8       ProductId[12];
    UINT32      AddrOemTablePointer; //canonical form
    UINT16      OemTableSize;
    UINT32      LocalAPIC;
    UINT16      ExtendedTableSize;
    UINT8       ExtendedTableChecksum;
};

struct MP_PROCESSOR
{
    UINT8       EntryType;  // == 0x00
    UINT8       ApicId;
    UINT8       ApicVersion;
    UINT8       Flags;
    UINT32      Signature;
    UINT32      Features;
    UINT32      Reserved0;
    UINT32      Reserved1;
};

struct MP_BUS
{
    UINT8       EntryType;  // == 0x01
    UINT8       BusId;
    UINT8       BusType[6];
};

struct MP_IO_APIC
{
    UINT8       EntryType;  // == 0x02
    UINT8       ApicId;
    UINT8       ApicVersion;
    UINT8       Flags;
    UINT32      Address;
};

struct MP_IO_INTERRUPT
{
    UINT8       EntryType;  // == 0x03
    UINT8       Type;
    UINT16      Flags;
    UINT8       SrcBusId;
    UINT8       SrcBusIrq;
    UINT8       DstApicId;
    UINT8       DstApicIrq;
};

struct MP_LOCAL_INTERRUPT
{
    UINT8       EntryType;  // == 0x04
    UINT8       Type;
    UINT16      Flags;
    UINT8       SrcBusId;
    UINT8       SrcBusIrq;
    UINT8       DstApicId;
    UINT8       DstApicIrq;
};

struct MP_ADDRESS_SPACE_MAPPING
{
    UINT8       EntryType;  // == 0x80
    UINT8       EntrySize;
    UINT8       BusId;
    UINT8       AddressType;
    UINT64      AddressBase;
    UINT64      AddressSize;
};

struct MP_BUS_HIERARCHY
{
    UINT8       EntryType;  // == 0x81
    UINT8       EntrySize;
    UINT8       BusId;
    UINT8       BusInfo;
    UINT8       ParentBusId;
    UINT8       Reserved[3];
};

struct MP_ADDRESS_SPACE_MODIFIER
{
    UINT8       EntryType;  // == 0x82
    UINT8       EntrySize;
    UINT8       BusId;
    UINT8       Predefined;
    UINT32      RangeList;
};

int IsMpsFps(MP_FLOAT * pScan)
{
    if (pScan->Signature[0] != '_' || pScan->Signature[1] != 'M' ||
        pScan->Signature[2] != 'P' || pScan->Signature[3] != '_') {

        return 0;
    }
    return Checksum((UINT8*)pScan, pScan->Paragraphs * 0x10);
}

MP_FLOAT * ScanForMp(UINT8 * pbBase, UINT8 * pbLimit)
{
    for (; pbBase < pbLimit; pbBase += 0x10) {
        MP_FLOAT * pScan = (MP_FLOAT *)pbBase;
        if (IsMpsFps(pScan)) {
            return pScan;
        }
    }
    return NULL;
}

static void CheckMp(Struct_Microsoft_Singularity_BootInfo *bi)
{
    printf("MP Information:\n");

    const UINT16 CMOS_SELECT        = 0x70;
    const UINT16 CMOS_DATA          = 0x71;

    IoSpaceWrite8(CMOS_SELECT, 0xf);
    UINT8 boot = IoSpaceRead8(CMOS_DATA);
#if DEBUG
    printf("CMOS Boot type = %02x\n", boot);
#endif

    if ((bi->MpEnter32 & 0xfff00fff) != 0) {
        printf("MpEnter is not valid for IPI Startup: %08x\n", bi->MpEnter32);
        Halt();
    }

    //////////////////////////////////////////////////////////// Scan for MPS.
    //
    MP_FLOAT * pFloat = NULL;

    if (pFloat == NULL && bi->Ebda32 != NULL) {
        pFloat = ScanForMp((UINT8*)bi->Ebda32, (UINT8*)bi->Ebda32 + 0x400);
    }
    if (pFloat == NULL) {
        pFloat = ScanForMp((UINT8*)0x9fc00, (UINT8*)0xa0000);
    }
    if (pFloat == NULL) {
        pFloat = ScanForMp((UINT8*)0x7fc00, (UINT8*)0x80000);
    }
    if (pFloat == NULL) {
        pFloat = ScanForMp((UINT8*)0xf0000, (UINT8*)0xfffff);
    }
    if (pFloat == NULL) {
        return;
    }

    printf("  Found MPS Version 1.%d at %lp\n", pFloat->Version, pFloat);
    if (pFloat->Version < 4) {
        pFloat = NULL;
        return;
    }
    bi->MpFloat32 = (UINT32) pFloat;

#if DEBUG
    printf("    MpConfigTable32=%x, Features=%02x%02x%02x%02x%02x\n",
           pFloat->AddrMpConfigTable32,
           pFloat->Features[0],
           pFloat->Features[1],
           pFloat->Features[2],
           pFloat->Features[3],
           pFloat->Features[4]);
#endif

    if (pFloat->AddrMpConfigTable32) {
#if DEBUG
        MP_FIXED * pFixed = (MP_FIXED *) pFloat->AddrMpConfigTable32;

        printf("    %c%c%c%c len=%d ver=%d sum=%02x\n",
               pFixed->Signature[0],
               pFixed->Signature[1],
               pFixed->Signature[2],
               pFixed->Signature[3],
               pFixed->BaseTableSize,
               pFixed->Version,
               pFixed->Checksum);
        printf("    Oem=[%-8.8s] Product=[%-12.12s] table=%x size=%d\n",
               pFixed->OemId,
               pFixed->ProductId,
               pFixed->AddrOemTablePointer,
               pFixed->OemTableSize);
        printf("    LocalAPIC=%08x ExtSize=%d ExtSum=%02x\n",
               pFixed->LocalAPIC,
               pFixed->ExtendedTableSize,
               pFixed->ExtendedTableChecksum);

        UINT8 *pbBegin = ((UINT8*)pFixed) + sizeof(*pFixed);
        UINT8 *pbLimit = ((UINT8*)pFixed) + pFixed->BaseTableSize;

        while (pbBegin < pbLimit) {
            printf("    %4d: ", pbBegin - ((UINT8*)pFixed));

            switch (pbBegin[0]) {
              case 0:
                  {
                      MP_PROCESSOR *pProc = (MP_PROCESSOR *)pbBegin;

                      printf("Proc Apic=%02x Ver=%02x Flags=%02x Sign=%08x Feat=%08x\n",
                             pProc->ApicId,
                             pProc->ApicVersion,
                             pProc->Flags,
                             pProc->Signature,
                             pProc->Features);
                      DumpFeatures(pProc->Features);

                      pbBegin = (UINT8*)(pProc + 1);
                  }
                  break;

              case 1:
                  {
                      MP_BUS *pBus = (MP_BUS *)pbBegin;

                      printf("Bus  Id=%02x Type=[%-6.6s]\n",
                             pBus->BusId,
                             pBus->BusType);

                      pbBegin = (UINT8*)(pBus + 1);
                  }
                  break;

              case 2:
                  {
                      MP_IO_APIC *pApic = (MP_IO_APIC *)pbBegin;

                      printf("Apic Apic=%02x Ver=%02x Flags=%02x Address=%08x\n",
                             pApic->ApicId,
                             pApic->ApicVersion,
                             pApic->Flags,
                             pApic->Address);

                      pbBegin = (UINT8*)(pApic + 1);
                  }
                  break;

              case 3:
                  {
                      MP_IO_INTERRUPT *pInterrupt = (MP_IO_INTERRUPT *)pbBegin;

                      printf("I/O  Type=%02x Flags=%04x [Bus=%02x Irq=%02x] -> [Apic=%02x Irq=%02x]\n",
                             pInterrupt->Type,
                             pInterrupt->Flags,
                             pInterrupt->SrcBusId,
                             pInterrupt->SrcBusIrq,
                             pInterrupt->DstApicId,
                             pInterrupt->DstApicIrq);

                      pbBegin = (UINT8*)(pInterrupt + 1);
                  }
                  break;

              case 4:
                  {
                      MP_LOCAL_INTERRUPT *pInterrupt = (MP_LOCAL_INTERRUPT *)pbBegin;

                      printf("Loc  Type=%02x Flags=%04x [Id=%02x Irq=%02x] -> [Id=%02x Irq=%02x]\n",
                             pInterrupt->Type,
                             pInterrupt->Flags,
                             pInterrupt->SrcBusId,
                             pInterrupt->SrcBusIrq,
                             pInterrupt->DstApicId,
                             pInterrupt->DstApicIrq);

                      pbBegin = (UINT8*)(pInterrupt + 1);
                  }
                  break;

              default:
                printf("Unknown Configuration Type = %02x\n", pbBegin[0]);
                pbBegin = pbLimit;
                break;
            }
        }

        pbBegin = ((UINT8*)pFixed) + pFixed->BaseTableSize;
        pbLimit = pbBegin + pFixed->ExtendedTableSize;

        while (pbBegin < pbLimit) {
            printf("    %4d: ", pbBegin - ((UINT8*)pFixed));

            switch (pbBegin[0]) {
              case 0x80:
                  {
                      MP_ADDRESS_SPACE_MAPPING *pMap = (MP_ADDRESS_SPACE_MAPPING *)pbBegin;

                      printf("Map  Bus=%02x Type=%02x Base=%08lx Size=%08lx\n",
                             pMap->BusId,
                             pMap->AddressType,
                             pMap->AddressBase,
                             pMap->AddressSize);
                  }
                  break;

              case 0x81:
                  {
                      MP_BUS_HIERARCHY *pHier = (MP_BUS_HIERARCHY *)pbBegin;

                      printf("Hier Bus=%02x Info=%02x ParentBusId=%02x\n",
                             pHier->BusId,
                             pHier->BusInfo,
                             pHier->ParentBusId);
                  }
                  break;

              case 0x82:
                  {
                      MP_ADDRESS_SPACE_MODIFIER *pMap = (MP_ADDRESS_SPACE_MODIFIER *)pbBegin;

                      printf("Mod  Bus=%02x Prefined=%02x RangeList=%08x\n",
                             pMap->BusId,
                             pMap->Predefined,
                             pMap->RangeList);
                  }
                  break;

              default:
                printf("Unknown Extended Type = %02x (len=%d bytes)\n", pbBegin[0], pbBegin[1]);
                break;
            }

            pbBegin += pbBegin[1];
        }
#endif

#if BOOT_START_MP
        printf("   MP Init:\n");
        volatile UINT32 * LocalApicIcrLo = (UINT32 *)(pFixed->LocalAPIC + 0x300);
        volatile UINT32 * LocalApicIcrHi = (UINT32 *)(pFixed->LocalAPIC + 0x310);
        volatile UINT32 * LocalApicEoi = (UINT32 *)(pFixed->LocalAPIC + 0x0b0);
        volatile UINT32 * LocalApicEsr = (UINT32 *)(pFixed->LocalAPIC + 0x280);

        const UINT32 AllExcludingSelf   = 0xc0000;
        const UINT32 LevelAssert        = 0x04000;
        const UINT32 SendPending        = 0x01000;
        const UINT32 DeliveryStartup    = 0x00600;
        const UINT32 DeliveryInit       = 0x00500;

        printf("    Sending IPI Init.\n");
        *LocalApicEsr = 0;
        *LocalApicEsr = 0;
        *LocalApicIcrLo = (AllExcludingSelf |
                           LevelAssert |
                           SendPending |
                           DeliveryInit);

        while (*LocalApicIcrLo & SendPending) {
            __asm pause;

            // Write tells APIC to update ESR.
            *LocalApicEsr = 0;
            if (*LocalApicEsr != 0) {
                printf("      APIC Error: %08x\n", *LocalApicEsr);
                break;
            }
        }

        printf("    Sent IPI Init.\n");

        for (int i = 0; i < 10000; i++) {
        }

        for (int i = 0; i < 2; i++) {
            printf(    "Sending IPI Startup %d.\n", i);
            *LocalApicEsr = 0;
            *LocalApicEsr = 0;
            *LocalApicIcrLo = (AllExcludingSelf |
                               LevelAssert |
                               SendPending |
                               DeliveryStartup |
                               (bi->MpEnter32 >> 12));

            while (*LocalApicIcrLo & SendPending) {
                __asm pause;

                // Write tells APIC to update ESR.
                *LocalApicEsr = 0;
                if (*LocalApicEsr != 0) {
                    printf("  APIC Error: %08x\n", *LocalApicEsr);
                    break;
                }
            }
            printf("    Sent IPI Startup.\n");

            for (int i = 0; i < 10000; i++) {
            }
        }
#endif // BOOT_START_MP
    }
    printf("\n");
}

static void CheckMinidump(Struct_Microsoft_Singularity_BootInfo *bi, uintptr *pEntry, uintptr *pStack)
{
    //////////////////////////////////////////////////////////// Vet Minidump.
    //
    UINT8* pbImage = (UINT8*)bi->DumpAddr32;
    UINT32 cbImage = bi->DumpSize32;

    UINT32 *plHeader = (UINT32*)pbImage;
    UINT32 *plTrailer = NULL;

#if DEBUG
    printf("Minidump at %x\n", pbImage);
#endif

    if (plHeader[0] != MINIDUMP_SIGNATURE ||
        plHeader[1] != MINIDUMP_VERSION) {

        printf("Image is corrupt!\n");
        printf("  Addr: %8x, Size: %8x\n", bi->DumpAddr32, bi->DumpSize32);
        printf("  %08x != %08x, %08x != %08x\n",
               MINIDUMP_SIGNATURE, plHeader[0],
               MINIDUMP_VERSION, plHeader[1]);
        Halt();
    }

    /////////////////////////////////////////////////////// Size the Minidump.
    //
    UINT32 Entry = 0;
    UINT32 Stack = 0;
    UINT32 Base = 0xffffffff;
    UINT32 Limit = 0;
    UINT32 cbSave = 0xfffffff;

    PMINIDUMP_HEADER pHeader = (PMINIDUMP_HEADER)(pbImage + 0);
    PMINIDUMP_DIRECTORY pDir
        = (PMINIDUMP_DIRECTORY)(pbImage + pHeader->StreamDirectoryRva);

#if DEBUG
    printf("  Streams: %d\n", pHeader->NumberOfStreams);
#endif
    for (UINT i = 0; i < pHeader->NumberOfStreams; i++) {
        switch (pDir[i].StreamType) {
          case ThreadListStream:
              {
                  PMINIDUMP_THREAD_LIST pl =
                      (PMINIDUMP_THREAD_LIST)(pbImage + pDir[i].Location.Rva);
#if DEBUG
                  printf("   Threads: %d\n", pl->NumberOfThreads);
#endif
                  for (UINT t = 0; t < pl->NumberOfThreads; t++) {
                      PCONTEXT pContext = (PCONTEXT)(pbImage + pl->Threads[t].ThreadContext.Rva);
                      Entry = pContext->Eip;
                      Stack = pContext->Esp;
#if DEBUG
                  printf("       entry: %x, stack %x \n", Entry, Stack);
#endif
                  }
              }
              break;

          case MemoryListStream:
              {
                  PMINIDUMP_MEMORY_LIST pl =
                      (PMINIDUMP_MEMORY_LIST)(pbImage + pDir[i].Location.Rva);
#if DEBUG
                  printf("   Number of Memory Ranges: %d, pl =%lx \n", pl->NumberOfMemoryRanges);
                  for (UINT t = 0; t < pl->NumberOfMemoryRanges; t++) {
                          printf("    stream at %lx, size =%lx \n",
                                 pl->MemoryRanges[t].StartOfMemoryRange,
                                 pl->MemoryRanges[t].Memory.DataSize);
                  }
#endif

                  for (UINT t = 0; t < pl->NumberOfMemoryRanges; t++) {
                      if (cbSave > pl->MemoryRanges[t].Memory.Rva) {
                          cbSave = pl->MemoryRanges[t].Memory.Rva;
                      }

                      UINT32 pbDst = (UINT32)pl->MemoryRanges[t].StartOfMemoryRange;
                      UINT32 cbSrc = (UINT32)pl->MemoryRanges[t].Memory.DataSize;

                      if (!CheckSmapForRam(bi,
                                           pl->MemoryRanges[t].StartOfMemoryRange,
                                           pl->MemoryRanges[t].Memory.DataSize)) {
                          printf("No RAM available at %lx..%lx.\n",
                                 pl->MemoryRanges[t].StartOfMemoryRange,
                                 pl->MemoryRanges[t].StartOfMemoryRange +
                                 pl->MemoryRanges[t].Memory.DataSize);
                          Halt();
                      }

                      if (pbDst > 0x100000) {
                          if (Base > pbDst) {
                              Base = pbDst;
                          }
                          if (Limit < pbDst + cbSrc) {
                              Limit = pbDst + cbSrc;
                          }
                      }
                  }
              }
              break;

          case 0xcafeface:
            plTrailer = (UINT32 *)(pbImage + pDir[i].Location.Rva);
            break;
        }
    }

    /////////////////////////////////////////////////// Validate the minidump.

    if (plTrailer == NULL || plTrailer[0] != 0xfeedbeef || plTrailer[1] != 0xcafeface) {
        printf("Image is corrupt!\n");
        printf("  Addr: %8x, Size: %8x\n", bi->DumpAddr32, bi->DumpSize32);
        printf("  0xfeedbeef != 0x%08x, 0xcafeface != 0x%08x\n",
               plTrailer[0], plTrailer[1]);
        Halt();
    }
    else {
        UINT32 *plPos = (UINT32*)pbImage;
        UINT32 *plEnd = (UINT32*)(plTrailer + 4);
        UINT32 sum = 0;
        for (; plPos < plEnd;) {
            sum += *plPos++;
        }
        if (sum != 0) {
            printf("Image is corrupt!\n");
            printf("  Addr: %8x, Size: %8x\n", bi->DumpAddr32, bi->DumpSize32);
            printf("  Checksum: %08x\n", sum);
            Halt();
        }
    }

    ///////////////////////////////////////////////////// Expand the minidump.
    //
    for (UINT i = 0; i < pHeader->NumberOfStreams; i++) {
        if (pDir[i].StreamType == MemoryListStream) {
            PMINIDUMP_MEMORY_LIST pl =
                (PMINIDUMP_MEMORY_LIST)(pbImage + pDir[i].Location.Rva);

            for (UINT t = 0; t < pl->NumberOfMemoryRanges; t++) {
                UINT8 *pbSrc = pbImage + pl->MemoryRanges[t].Memory.Rva;
                UINT8 *pbDst = (UINT8*)pl->MemoryRanges[t].StartOfMemoryRange;
                UINT32 cbSrc = pl->MemoryRanges[t].Memory.DataSize;

                if (pbDst < (UINT8*)0x100000) {
                    // Don't write into memory below 1MB.
#if DEBUG
                    printf("   %p..%p -> %p..%p Ignored.\n",
                           pbSrc, pbSrc + cbSrc,
                           pbDst, pbDst + cbSrc);
#endif
                }
                else {
#if DEBUG
                    printf("   %p..%p -> %p..%p Copied\n",
                           pbSrc, pbSrc + cbSrc,
                           pbDst, pbDst + cbSrc);
#endif
                    CopyDown(pbDst, pbSrc, cbSrc);
                }
            }
        }
    }

#if DEBUG
    printf("   Minidump at %x..%x\n",
           bi->DumpAddr32, bi->DumpAddr32 + bi->DumpSize32);
    printf("   Kernel at %x..%x [Entry=%x, Stack=%x]\n", Base, Limit, Entry, Stack);
#endif

    *pEntry = Entry;
    *pStack = Stack;

    bi->DumpBase = Base;
    bi->DumpLimit = Limit;
    bi->DumpRemainder = (UINT32)(plTrailer + 4);
}

static uintptr g_Entry = 0;
static uintptr g_Stack = 0;
static Struct_Microsoft_Singularity_BootInfo *g_bi = 0;
static Struct_Microsoft_Singularity_X86_IDTP g_idt;
static Struct_Microsoft_Singularity_X86_IDTE g_idtEntries[32];

extern "C" void __cdecl IdtHandler(UINT32 _cr2,
                                   UINT32 _edi, UINT32 _esi, UINT32 _ebp, UINT32 _esp,
                                   UINT32 _ebx, UINT32 _edx, UINT32 _ecx, UINT32 _eax,
                                   UINT32 _num, UINT32 _err, UINT32 _eip, UINT32 _cs0,
                                   UINT32 _efl)
{
    printf("\n");
    printf("=-=-=-= X86 Exception 0x%x [%x] =-=-=-=\n", _num, &_efl);
    printf("err=%08x cr2=%08x eip=%08x efl=%08x\n",
           _err, _cr2, _eip, _efl);
    printf("eax=%08x ebx=%08x ecx=%08x edx=%08x\n",
           _eax, _ebx, _ecx, _edx);
    printf("esi=%08x edi=%08x ebp=%08x esp=%08x\n",
           _esi, _edi, _ebp, _esp);

    printf("Stack:\n");
    UINT32 * stack = ((UINT32*)_esp);
    for (int i = 0; i < 10; i++) {
        printf("    %08x: %08x\n", &stack[i], stack[i]);
    }
    printf("Call Stack:\n");
    for (int i = 0; i < 10 && _ebp >= Struct_Microsoft_Singularity_BootInfo_PHYSICAL_DISABLED; i++) {
        ULONG_PTR next = ((ULONG_PTR *)_ebp)[0];
        ULONG_PTR code = ((ULONG_PTR *)_ebp)[1];

        printf("    %p: %p %p\n", _ebp, next, code);
        _ebp = next;
    }

    Halt();
    for (;;);
}

static void IdtInit()
{
    Struct_Microsoft_Singularity_BootInfo *bi = g_bi;

    UINT32 entry = (UINT32)bi->IdtEnter0;
    UINT32 offset = ((UINT32)bi->IdtEnter1) - ((UINT32)bi->IdtEnter0);

    for (int i = 0; i < arrayof(g_idtEntries); i++) {
        g_idtEntries[i].offset_0_15 = (UINT16)entry;
        g_idtEntries[i].selector =
            (UINT16)(offsetof(Struct_Microsoft_Singularity_CpuInfo,GdtPC) -
                     offsetof(Struct_Microsoft_Singularity_CpuInfo,GdtNull));
        g_idtEntries[i].access =
            (Struct_Microsoft_Singularity_X86_IDTE_PRESENT |
             Struct_Microsoft_Singularity_X86_IDTE_DPL_RING0 |
             Struct_Microsoft_Singularity_X86_IDTE_INT_GATE);
        g_idtEntries[i].offset_16_31 = (UINT16)(entry >> 16);

        entry += offset;
    }

    *(void **)bi->IdtTarget = (void *)IdtHandler;

    g_idt.limit = sizeof(g_idtEntries);
    g_idt.addr = (UINT32)g_idtEntries;

    __asm {
        lidt g_idt.limit;
    }

}

static void __declspec(naked) __fastcall SwitchStack
(
 void (__fastcall *pfNext)(void) /* ECX */,
 UINT32 _esp /* EDX */
)
{
    __asm {
#if 0
        push ebp;
        mov ebp, esp;

        mov ebx, 0xb8000;
        mov eax, ecx;
        call printdw;

        mov eax, edx;
        call printdw;

        mov eax, ecx;
        call printdw;
#endif

        mov esp, edx;
        jmp ecx;

#if 0
        // Print a DWORD to the screen
        // [in] ebx = address of screen
        // [in] eax = dword to print
        // [in] edi = trashed for temporary
        //
      printdw:
        mov     edi, eax;
        shr     edi, 28;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print28;
        add     edi, 7;
      print28:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 24;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print24;
        add     edi, 7;
      print24:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 20;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print20;
        add     edi, 7;
      print20:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 16;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print16;
        add     edi, 7;
      print16:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 12;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print12;
        add     edi, 7;
      print12:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 8;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print08;
        add     edi, 7;
      print08:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 4;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print04;
        add     edi, 7;
      print04:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 0;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print00;
        add     edi, 7;
      print00:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, 0x0f20;
        mov     [ebx+0], edi;
        add     ebx, 2;
        mov     [ebx+0], edi;
        add     ebx, 2;
        ret;
#endif
    }
}

static void __fastcall undump1(void);
static void __fastcall undump2(void);

static void __fastcall undump_mp(int cpu);

extern "C" void undump(Struct_Microsoft_Singularity_BootInfo *bi, int cpu)
{
    if (cpu == 0)
    {
        g_bi = bi;
        g_bi->BootCount = 0;
        undump1();
    }
    else
    {
        g_bi->MpStatus32 = Struct_Microsoft_Singularity_MpBootStatus_UndumpEntry;
        undump_mp(cpu);
    }
}


static void __fastcall undump1(void)
{
    BdInitDebugger(g_bi->DebugBasePort);
    Normal();
    Cls();

    Struct_Microsoft_Singularity_BootInfo *bi = (Struct_Microsoft_Singularity_BootInfo *)g_bi;

    printf("32-bit Singularity Undump [" __DATE__ " " __TIME__ "]\n");
    printf("BootInfo: %08lx (0x%x/%d bytes)\n", bi, sizeof(*bi), sizeof(*bi));

    if (sizeof(Struct_Microsoft_Singularity_BootInfo) != bi->RecSize) {
        printf("sizeof(BootInfo)=%d, bi->RecSize=%ld\n",
               sizeof(Struct_Microsoft_Singularity_BootInfo), bi->RecSize);
        for(;;);
    }

    IdtInit();


    CheckMinidump(bi, &g_Entry, &g_Stack);

#if 0
    UINT32 _esp;
    __asm mov _esp, esp;
    printf("    SwitchStack(bi=%x, next=%x, esp=%x) was esp=%x\n",
           bi, undump2, g_Stack, _esp);

    Dump(((UINT8*)undump2) - 8, 16);
    Probe(((UINT8*)undump2) - 8, 16);
    Dump(((UINT8*)g_Stack) - 8, 16);
    Probe(((UINT8*)g_Stack) - 8, 16);
#endif
    printf("Switching stack to %x and calling  %x\n", g_Stack, undump2);
    SwitchStack(undump2, g_Stack);   // Transfers execution to undump2();
}

static void __fastcall undump2(void)
{
#if 0
    __asm {
        mov eax, esp;
        call printdw;

        int 3;

      again: jmp again;
        jmp ecx;

        // Print a DWORD to the screen
        // [in] ebx = address of screen
        // [in] eax = dword to print
        // [in] edi = trashed for temporary
        //
      printdw:
        mov     edi, eax;
        shr     edi, 28;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print28;
        add     edi, 7;
      print28:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 24;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print24;
        add     edi, 7;
      print24:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 20;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print20;
        add     edi, 7;
      print20:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 16;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print16;
        add     edi, 7;
      print16:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 12;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print12;
        add     edi, 7;
      print12:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 8;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print08;
        add     edi, 7;
      print08:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 4;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print04;
        add     edi, 7;
      print04:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, eax;
        shr     edi, 0;
        and     edi, 0xf;
        add     edi, 0x0f30;
        cmp     edi, 0x0f3a;
        jl      print00;
        add     edi, 7;
      print00:
        mov     [ebx+0], edi;
        add     ebx, 2;

        mov     edi, 0x0f20;
        mov     [ebx+0], edi;
        add     ebx, 2;
        mov     [ebx+0], edi;
        add     ebx, 2;
        ret;
    }
#endif

    Struct_Microsoft_Singularity_BootInfo *bi = g_bi;
    void (__cdecl * pfHal)(const Struct_Microsoft_Singularity_BootInfo *, int)
        = (void (__cdecl *)(const Struct_Microsoft_Singularity_BootInfo *, int))g_Entry;

#if DEBUG
    printf("Probing basic hardware.\n");
#endif

    CheckPci(bi);
    CheckCpuid(bi);
    CheckApic(bi);
    CheckAcpi(bi);
    CheckMp(bi);
    FpuInit();
    printf("Starting Singularity Kernel (at 0x%x)\n", pfHal);

    pfHal(bi, 0);

    bi->BootCount++;

    SwitchStack(undump1, Struct_Microsoft_Singularity_BootInfo_REAL_STACK);
    // Transfers execution to undump1();
}

static void __fastcall undump_mp(int cpu)
{
    IdtInit();
    FpuInit();

    g_bi->MpStatus32 = Struct_Microsoft_Singularity_MpBootStatus_HalEntry;

    void (__cdecl * pfHal)(const Struct_Microsoft_Singularity_BootInfo *, int)
        = (void (__cdecl *)(const Struct_Microsoft_Singularity_BootInfo *, int))g_Entry;

    pfHal(g_bi, cpu);
}
