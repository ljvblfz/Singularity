//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  halkd.cpp: runtime support for debugging
//
//  For more information see:
//      \nt\base\ntos\kd64
//      \nt\base\boot\kdcom
//      \nt\base\boot\kd1394
//      \nt\base\boot\kdusb2
//      \nt\sdktools\debuggers\ntsd64
//
#include "hal.h"
#include "halkd.h"

extern "C" void *  __cdecl memcpy(void *, const void *, size_t);
extern "C" void *  __cdecl memset(void *, int, size_t);

//
// Debugger Debugging
//
#define KDDBG if (0) kdprintf
#define KDDBG2 if (0) kdprintf

#if defined(_IA64_)
#define SIGN_EXTEND_PTR(p) p
#else
#define SIGN_EXTEND_PTR(p) ((ULONG_PTR)(LONG_PTR)(LONG)(p))
#endif

#define KeProcessorLevel 15

//
// Globals
//
extern const Struct_Microsoft_Singularity_BootInfo *g_pBootInfo;
static Struct_Microsoft_Singularity_X86_IDTP g_idt;

BOOL KdDebuggerNotPresent = FALSE;

#define KDP_MESSAGE_BUFFER_SIZE 4096
static CHAR KdpMessageBuffer[KDP_MESSAGE_BUFFER_SIZE];
static BOOL KdpContextSent;

static KPROCESSOR_STATE KdpProcessorState[MAX_CPU];
static KD_CONTEXT KdpContext;
static int KeNumberProcessors = 1;
static UINT16 KdpSpinBase = 0x2f00;

KDP_BREAKPOINT_TYPE KdpBreakpointInstruction = KDP_BREAKPOINT_VALUE;
BREAKPOINT_ENTRY KdpBreakpointTable[BREAKPOINT_TABLE_SIZE] = {0};

//
// KdpRetryCount controls the number of retries before we give
// up and assume kernel debugger is not present.
// KdpNumberRetries is the number of retries left.  Initially,
// it is set to 5 such that booting NT without debugger won't be
// delayed to long.
//
ULONG KdCompNumberRetries = 5;
ULONG KdCompRetryCount    = 5;
ULONG KdPacketId = 0;

void (*KdSendPacket)(ULONG PacketType,
                     IN PSTRING MessageHeader,
                     IN PSTRING MessageData OPTIONAL,
                     IN OUT PKD_CONTEXT KdContext) = NULL;
KDP_STATUS (*KdReceivePacket)(IN ULONG PacketType,
                         OUT PSTRING MessageHeader,
                         OUT PSTRING MessageData,
                         OUT PULONG DataLength,
                         IN OUT PKD_CONTEXT KdContext) = NULL;
bool (*KdPollBreakIn)() = NULL;

//
// Static data - these are walked by the kernel debugger
//

//PsLoadedModuleList ===========================================================

struct KLDR_DATA_TABLE_ENTRY_WITH_NAME : KLDR_DATA_TABLE_ENTRY
{
    WCHAR   wzName[32];
};

static KLDR_DATA_TABLE_ENTRY_WITH_NAME KdModuleKernelEntry[128];
static ULONG KdModuleKernelUsed = 0;
static LIST_ENTRY PsLoadedModuleList;
static PMINIDUMP_MODULE_LIST KdMinidumpModuleList = NULL;

// KdVersionBlock =============================================================

static DBGKD_GET_VERSION64 KdVersionBlock = {
    DBGKD_MAJOR_SINGULARITY << 8, // MajorVersion ... this one sort of works
    0, // Minor
    DBGKD_64BIT_PROTOCOL_VERSION2, // Protocol
    DBGKD_VERS_FLAG_NOMM, //DBGKD_VERS_FLAG_DATA, // Flags
    IMAGE_FILE_MACHINE_I386, // Machine Type
    PACKET_TYPE_MAX, // Max packet
    DbgKdMaximumStateChange - DbgKdMinimumStateChange, // MaxStateChange
    DbgKdSetContextApi /*DbgKdMaximumManipulate*/ - DbgKdMinimumManipulate, // MaxManipulate we support
    0, // Simulation
    0, // Unused
    0, // KernBase
    (ULONG64)&PsLoadedModuleList, // PsLoadedModuleList
    0 // DebuggerDataList
};

// ========================================================================

// Forward declarations

static void KdpLock(void);
static void KdpUnlock(void);

static void KdpEnter(void);
static void KdpLeave(void);

static void KdpFakeOutPsLoadedModuleList(void);

static KCONTINUE_STATUS
KdpSendWaitContinue(
    IN ULONG OutPacketType,
    IN PSTRING OutMessageHeader,
    IN PSTRING OutMessageData OPTIONAL,
    IN OUT Struct_Microsoft_Singularity_X86_ThreadContext *x86Context
    );

static void LoadedBinary(Struct_Microsoft_Singularity_X86_ThreadContext *context);
static void UnloadedBinary(Struct_Microsoft_Singularity_X86_ThreadContext *context);

extern int printf(const char *pszFmt, ...);

///////////////////////////////////////// Debugger Unique Interrupts Routines.
//
//  NB: Without these, we share routines with the mainline code and we get
//  caught in a loop when the debugger inserts a break after the pushfd when
//  someone tries to single step through Processor:g_DisableInterrupts!
//
static __declspec(naked) bool KdpDisableInterrupts()
{
    __asm {
        pushfd;
        pop eax;
        test eax, Struct_Microsoft_Singularity_X86_EFlags_IF;
        setnz al;
        nop;    // required so that the linker doesn't combine with g_Disable
        cli;
        ret;
    }
}

static __declspec(naked) void KdpRestoreInterrupts(bool enabled)
{
    __asm {
        nop;
        test cl, cl;
        je done;
        nop;    // required so that the linker doesn't combine with g_Restore
        sti;
      done:
        ret;
    }
}

//////////////////////////////////////////////////////////////////////////////
//
static volatile INT32 KdpInDebugger = 0;
static volatile bool KdpInDebuggerIntEnabled = FALSE;

#define MAXIMUM_RETRIES 20

static void KdpNulSendPacket(ULONG PacketType,
                             IN PSTRING MessageHeader,
                             IN PSTRING MessageData OPTIONAL,
                             IN OUT PKD_CONTEXT KdContext)
{
}

static KDP_STATUS KdpNulReceivePacket(IN ULONG PacketType,
                                      OUT PSTRING MessageHeader,
                                      OUT PSTRING MessageData,
                                      OUT PULONG DataLength,
                                      IN OUT PKD_CONTEXT KdContext)
{
    return KDP_PACKET_TIMEOUT;
}

static bool KdpNulPollBreakIn()
{
    return false;
}

void KdInitialize(Struct_Microsoft_Singularity_BootInfo *bi)
{
    KdpLock();

    if (KdpComInit(bi)) {
        KdpSpinBase = 0x2f00;
        KdSendPacket = KdpComSendPacket;
        KdReceivePacket = KdpComReceivePacket;
        KdPollBreakIn = KdpComPollBreakIn;
        kdprintf("Serial Port (bi->DebugBasePort=%x).\n", bi->DebugBasePort);
    }
    else if (Kdp1394Init(bi)) {
        KdpSpinBase = 0x4f00;
        KdSendPacket = Kdp1394SendPacket;
        KdReceivePacket = Kdp1394ReceivePacket;
        KdPollBreakIn = Kdp1394PollBreakIn;
        kdprintf("1394 Port (bi->DebugBasePort=%x).\n", bi->DebugBasePort);
    }
    else {
        kdprintf("No debugger.\n");
        KdSendPacket = KdpNulSendPacket;
        KdReceivePacket = KdpNulReceivePacket;
        KdPollBreakIn = KdpNulPollBreakIn;
        KdDebuggerNotPresent = true;
    }

    // Retries are set to this after boot
    KdpContext.KdpDefaultRetries = MAXIMUM_RETRIES;

    KdpUnlock();

    KdpFakeOutPsLoadedModuleList();
}

static void KdpLock()
{
    for (;;) {
        bool enabled = KdpDisableInterrupts();
        if (InterlockedCompareExchange(&KdpInDebugger, 1, 0) == 0) {
            KdpInDebuggerIntEnabled = enabled;
            return;
        }
        KdpRestoreInterrupts(enabled);
        __asm pause
    }
}

static void KdpUnlock()
{
    KdpInDebugger = 0;
    KdpRestoreInterrupts(KdpInDebuggerIntEnabled);
}

static void KdpEnter()
{
    if (KeNumberProcessors > 1) {
        Class_Microsoft_Singularity_MpExecution::g_FreezeAllProcessors();
    }
}

static void KdpLeave()
{
    if (KeNumberProcessors > 1) {
        Class_Microsoft_Singularity_MpExecution::g_ThawAllProcessors();
    }
}

//////////////////////////////////////////////////////////////////////////////
//
extern int strformat(void (*pfOutput)(void *pContext, char c), void *pContext,
                     const char * pszFmt, va_list args);

#define KD_LEFT     0
#define KD_HEIGHT   46

static UINT16 kdcurs = KD_LEFT;
static UINT16 kdattr = 0x2f00;

static void koutput(void *pContext, char c)
{
    //
    // Update cursor position
    //
    if ((kdcurs % 80) < KD_LEFT) {
        kdcurs += KD_LEFT - (kdcurs % 80);
    }

    if (kdcurs >= KD_HEIGHT * 80) {
        for (UINT16 i = 0; i < KD_HEIGHT - 1; i++) {
            for (UINT16 j = KD_LEFT; j < 80; j++) {
                ((UINT16 *)0xb8000)[i*80+j] = ((UINT16 *)0xb8000)[i*80+80+j];
            }
        }
        for (UINT16 j = KD_LEFT; j < 80; j++) {
            ((UINT16 *)0xb8000)[(KD_HEIGHT-1)*80+j] = kdattr | ' ';
        }
        kdcurs = kdcurs - 80;
    }

    //
    // Output character
    //
    if (c >= ' ' && c <= '~') {
        ((UINT16 *)0xb8000)[kdcurs++] = kdattr | c;
    }
    else if (c == '\t') {
        kdcurs += 8 - (kdcurs % 8);
    }
    else if (c == '\n') {
        while ((kdcurs % 80) != 0) {
            ((UINT16 *)0xb8000)[kdcurs++] = kdattr | ' ';
        }
    }
    else if (c == '\r') {
        kdcurs -= (kdcurs % 80);
    }
    else if (c == '\f') {
        kdcurs = 0;
    }
}

void kdprints(const char * pszFmt)
{
    while (*pszFmt) {
        koutput(NULL, *pszFmt++);
    }
}

void kdprintf(const char * pszFmt, ...)
{
    va_list args;

    va_start(args, pszFmt);
    strformat(koutput, NULL, pszFmt, args);
    va_end(args);
}

void KdpSpin()
{
    static UINT8 state = 0;

    *((UINT16 *)0xb809e) = KdpSpinBase + ("+-|*" [state++ & 0x3]);
}

//////////////////////////////////////////////////////////////////////////////
//
ULONG KdpComputeChecksum(IN PCHAR Buffer, IN ULONG Length)
{
    // Compute the checksum for the string passed in.
    ULONG   Checksum = 0;

    while (Length > 0) {
        Checksum = Checksum + (ULONG)*(PUCHAR)Buffer++;
        Length--;
    }

    return(Checksum);
} // KdpComputeChecksum

//////////////////////////////////////////////////////////////////////////////
//
static
VOID KdSingularityToWindbgContext(IN CONST Struct_Microsoft_Singularity_X86_ThreadContext *singularity,
                                  OUT CONTEXT *windbg)
{
    RtlZeroMemory(windbg, sizeof(*windbg));

    windbg->ContextFlags = (CONTEXT_CONTROL |
                            CONTEXT_INTEGER |
                            CONTEXT_SEGMENTS |
                            CONTEXT_DEBUG_REGISTERS);

    // CONTEXT_FULL;
    windbg->Eax = singularity->eax;
    windbg->Ebx = singularity->ebx;
    windbg->Ecx = singularity->ecx;
    windbg->Edx = singularity->edx;
    windbg->Esp = singularity->esp;
    windbg->Ebp = singularity->ebp;
    windbg->Esi = singularity->esi;
    windbg->Edi = singularity->edi;
    windbg->Eip = singularity->eip;
    windbg->EFlags = singularity->efl;

    // CONTEXT_FLOATING_POINT
    if (singularity->regs) {
        windbg->ContextFlags |= CONTEXT_FLOATING_POINT;

#if 0
        PUCHAR pmmx = (PUCHAR)&singularity->mmx;
        kdprintf("  MMX=%02x%02x%02x%02x %02x%02x%02x%02x %02x%02x%02x%02x %02x%02x%02x%02x\n",
                 pmmx[0], pmmx[1], pmmx[2], pmmx[3],
                 pmmx[4], pmmx[5], pmmx[6], pmmx[7],
                 pmmx[8], pmmx[9], pmmx[10], pmmx[11],
                 pmmx[12], pmmx[13], pmmx[14], pmmx[15]);
        kdprintf("  MMX=%04x %04x %02x %03x %04x:%08x %04x:%08x\n",
                 singularity->mmx.fcw,
                 singularity->mmx.fsw,
                 singularity->mmx.ftw,
                 singularity->mmx.fop,
                 singularity->mmx.cs,
                 singularity->mmx.eip,
                 singularity->mmx.ds,
                 singularity->mmx.dp);
#endif

        windbg->FloatSave.ControlWord = singularity->mmx.fcw;
        windbg->FloatSave.StatusWord = singularity->mmx.fsw;
        windbg->FloatSave.TagWord = singularity->mmx.ftw;
        windbg->FloatSave.ErrorOffset = singularity->mmx.eip;
        windbg->FloatSave.ErrorSelector = singularity->mmx.cs;
        windbg->FloatSave.DataOffset = singularity->mmx.dp;
        windbg->FloatSave.DataSelector = singularity->mmx.ds;

        memcpy((uint8*)windbg->FloatSave.RegisterArea+0, &singularity->mmx.st0, 10);
        memcpy((uint8*)windbg->FloatSave.RegisterArea+10, &singularity->mmx.st1, 10);
        memcpy((uint8*)windbg->FloatSave.RegisterArea+20, &singularity->mmx.st2, 10);
        memcpy((uint8*)windbg->FloatSave.RegisterArea+30, &singularity->mmx.st3, 10);
        memcpy((uint8*)windbg->FloatSave.RegisterArea+40, &singularity->mmx.st4, 10);
        memcpy((uint8*)windbg->FloatSave.RegisterArea+50, &singularity->mmx.st5, 10);
        memcpy((uint8*)windbg->FloatSave.RegisterArea+60, &singularity->mmx.st6, 10);
        memcpy((uint8*)windbg->FloatSave.RegisterArea+70, &singularity->mmx.st7, 10);
    }

    // CONTEXT_EXTENDED_REGISTERS
    if (singularity->regs & 1) {
        windbg->ContextFlags |= CONTEXT_EXTENDED_REGISTERS;
        memcpy(windbg->ExtendedRegisters, &singularity->mmx, 512);
    }

    //
    // This section is specified/returned if CONTEXT_DEBUG_REGISTERS is
    // set in ContextFlags.  Note that CONTEXT_DEBUG_REGISTERS is NOT
    // included in CONTEXT_FULL.
    //
    windbg->Dr0 = singularity->dr0;
    windbg->Dr1 = singularity->dr1;
    windbg->Dr2 = singularity->dr2;
    windbg->Dr3 = singularity->dr3;
    windbg->Dr6 = singularity->dr6;
    windbg->Dr7 = singularity->dr7;

#if 0
    __asm {
        mov ebx, windbg;

        mov eax, dr0;
        mov [ebx]CONTEXT.Dr0, eax;
        mov eax, dr1;
        mov [ebx]CONTEXT.Dr1, eax;
        mov eax, dr2;
        mov [ebx]CONTEXT.Dr2, eax;
        mov eax, dr3;
        mov [ebx]CONTEXT.Dr3, eax;
        mov eax, dr6;
        mov [ebx]CONTEXT.Dr6, eax;
        mov eax, dr7;
        mov [ebx]CONTEXT.Dr7, eax;
    }
#endif

#if 1
    //
    // This section is specified/returned if the
    // ContextFlags word contains the flag CONTEXT_SEGMENTS.
    //
    __asm {
        mov ebx, windbg;

        xor eax, eax;
        mov ax, gs;
        mov [ebx]CONTEXT.SegGs, eax;
        mov ax, fs;
        mov [ebx]CONTEXT.SegFs, eax;
        mov ax, es;
        mov [ebx]CONTEXT.SegEs, eax;
        mov ax, ds;
        mov [ebx]CONTEXT.SegDs, eax;
    }

    //
    // This section is specified/returned if the
    // ContextFlags word contains the flag CONTEXT_CONTROL.
    //

    __asm {
        mov ebx, windbg;

        xor eax, eax;
        mov ax, ss;
        mov [ebx]CONTEXT.SegSs, eax;
    }

    windbg->SegCs = singularity->cs0;
#endif
}

VOID KdWindbgToSingularityContext(IN CONST CONTEXT *windbg,
                                  OUT Struct_Microsoft_Singularity_X86_ThreadContext *singularity)
{
    singularity->eax = windbg->Eax;
    singularity->ebx = windbg->Ebx;
    singularity->ecx = windbg->Ecx;
    singularity->edx = windbg->Edx;
    singularity->esp = windbg->Esp;
    singularity->ebp = windbg->Ebp;
    singularity->esi = windbg->Esi;
    singularity->edi = windbg->Edi;
    singularity->eip = windbg->Eip;
    singularity->efl = windbg->EFlags;

    // CONTEXT_FLOATING_POINT
    if (windbg->ContextFlags & CONTEXT_FLOATING_POINT) {
        singularity->mmx.fcw = (uint16)windbg->FloatSave.ControlWord;
        singularity->mmx.fsw = (uint16)windbg->FloatSave.StatusWord;
        singularity->mmx.ftw = (uint16)windbg->FloatSave.TagWord;
        singularity->mmx.cs = (uint16)windbg->FloatSave.ErrorSelector;
        singularity->mmx.eip = windbg->FloatSave.ErrorOffset;
        singularity->mmx.ds = (uint16)windbg->FloatSave.DataSelector;
        singularity->mmx.dp = windbg->FloatSave.DataOffset;
        memcpy(&singularity->mmx.st0, windbg->FloatSave.RegisterArea+0, 10);
        memcpy(&singularity->mmx.st1, windbg->FloatSave.RegisterArea+10, 10);
        memcpy(&singularity->mmx.st2, windbg->FloatSave.RegisterArea+20, 10);
        memcpy(&singularity->mmx.st3, windbg->FloatSave.RegisterArea+30, 10);
        memcpy(&singularity->mmx.st4, windbg->FloatSave.RegisterArea+40, 10);
        memcpy(&singularity->mmx.st5, windbg->FloatSave.RegisterArea+50, 10);
        memcpy(&singularity->mmx.st6, windbg->FloatSave.RegisterArea+60, 10);
        memcpy(&singularity->mmx.st7, windbg->FloatSave.RegisterArea+70, 10);
    }

    // CONTEXT_EXTENDED_REGISTERS
    if (windbg->ContextFlags & CONTEXT_EXTENDED_REGISTERS) {
        memcpy(&singularity->mmx, windbg->ExtendedRegisters, 512);
    }

    //
    // This section is specified/returned if CONTEXT_DEBUG_REGISTERS is
    // set in ContextFlags.  Note that CONTEXT_DEBUG_REGISTERS is NOT
    // included in CONTEXT_FULL.
    //
    if (windbg->ContextFlags & CONTEXT_DEBUG_REGISTERS) {
        singularity->dr0 = windbg->Dr0;
        singularity->dr1 = windbg->Dr1;
        singularity->dr2 = windbg->Dr2;
        singularity->dr3 = windbg->Dr3;
        singularity->dr6 = windbg->Dr6;
        singularity->dr7 = windbg->Dr7;
#if 0
        __asm {
            mov ebx, windbg;
            mov eax, [ebx]CONTEXT.Dr0;
            mov dr0, eax;
            mov eax, [ebx]CONTEXT.Dr1;
            mov dr1, eax;
            mov eax, [ebx]CONTEXT.Dr2;
            mov dr2, eax;
            mov eax, [ebx]CONTEXT.Dr3;
            mov dr3, eax;
            mov eax, 0; //CONTEXT.Dr6 : DR6 is reset when changing BPTs.
            mov dr6, eax;
            mov eax, [ebx]CONTEXT.Dr7;
            mov eax, dr7;
        }
#endif
    }
}

//////////////////////////////////////////////////////////////////////////////

//
// Misc KD functions
//


NTSTATUS
KdpCopyMemoryChunks(
    ULONG64 Address,
    PVOID Buffer,
    ULONG TotalSize,
    ULONG ChunkSize,
    ULONG Flags,
    PULONG ActualSize OPTIONAL
    )
    //  Routine Description:
    //      Copies memory to/from a buffer to/from a system address.
    //      The address can be physical or virtual.
    //      The buffer is assumed to be valid for the duration of this call.
    //
    //  Arguments:
    //      Address - System address.
    //      Buffer - Buffer to read from or write to.
    //      TotalSize - Number of bytes to read/write.
    //      ChunkSize - Maximum single item transfer size, must
    //                  be 1, 2, 4 or 8.
    //                  0 means choose a default.
    //      Flags - MMDBG_COPY flags for MmDbgCopyMemory.
    //      ActualSize - Number of bytes actually read/written.
    //
    //  Return Value:
    //      NTSTATUS
{
    ULONG Length;
    ULONG CopyChunk;
//    NTSTATUS Status;
#if defined(_IA64_)
    ULONG64 AddressStart = Address;
#endif

    if (ChunkSize > MMDBG_COPY_MAX_SIZE) {
        ChunkSize = MMDBG_COPY_MAX_SIZE;
    } else if (ChunkSize == 0) {
        // Default to 4 byte chunks as that's
        // what the previous code did.
        ChunkSize = 4;
    }

    //
    // MmDbgCopyMemory only copies a single aligned chunk at a
    // time.  It is Kd's responsibility to chunk up a larger
    // request for individual copy requests.  This gives Kd
    // the flexibility to pick a chunk size and also frees
    // Mm from having to worry about more than a page at a time.
    // Additionally, it is important that we access memory with the
    // largest size possible because we could be accessing
    // memory-mapped I/O space.
    //

    Length = TotalSize;
    CopyChunk = 1;

    while (Length > 0) {

        // Expand the chunk size as long as:
        //   We haven't hit the chunk limit.
        //   We have enough data left.
        //   The address is properly aligned.
        while (CopyChunk < ChunkSize &&
               (CopyChunk << 1) <= Length &&
               (Address & ((CopyChunk << 1) - 1)) == 0) {
            CopyChunk <<= 1;
        }

        // Shrink the chunk size to fit the available data.
        while (CopyChunk > Length) {
            CopyChunk >>= 1;
        }

        Address &= 0xffffffff;
        if (Address < Struct_Microsoft_Singularity_BootInfo_PHYSICAL_DISABLED) {
            break;
        }
        if (Address == 0) {
            break;
        }
#if PAGING
        ULONG64 RawAddress = Address;
        if (Flags & MMDBG_COPY_PHYSICAL) {
            // Temporarily map the physical memory range.
            // TODO: KernelMapPhysicalMemory tries to acquire a lock -- if
            // this lock is not free, we may deadlock here.
            // Also, remapping for every chunk is inefficient.
            KDDBG("Physical address = 0x%x size = 0x%x\n", int(Address), int(CopyChunk));
            Struct_Microsoft_Singularity_Memory_PhysicalAddress physical;
            Struct_Microsoft_Singularity_Memory_PhysicalAddress::m__ctor(
                &physical,
                UIntPtr(Address));
            Address = ULONG64(Class_Microsoft_Singularity_Memory_MemoryManager
                ::g_KernelMapPhysicalMemory(
                    physical,
                    UIntPtr(CopyChunk)));
            KDDBG("Physical address 0x%x mapped to virtual address 0x%x\n", int(RawAddress), int(Address));
        }
        else {
            if (Class_Microsoft_Singularity_Memory_MemoryManager::c_isInitialized
                && !Class_Microsoft_Singularity_Memory_VMManager::g_IsPageMapped(
                        Class_Microsoft_Singularity_Memory_MemoryManager::g_PageAlign(
                            UIntPtr(Address)))) {
                break;
            }
        }
#endif
        if (Flags & MMDBG_COPY_WRITE) {
            memcpy((void*)Address, Buffer, CopyChunk);
        } else {
            memcpy(Buffer, (void*)Address, CopyChunk);
        }

#if PAGING
        if (Flags & MMDBG_COPY_PHYSICAL) {
            KDDBG("Unmapping physical address 0x%x (virtual address 0x%x)\n", int(RawAddress), int(Address));
            Class_Microsoft_Singularity_Memory_MemoryManager
                ::g_KernelUnmapPhysicalMemory(
                    UIntPtr(Address), UIntPtr(Address + CopyChunk));
            KDDBG("Unmapped physical address 0x%x (virtual address 0x%x)\n", int(RawAddress), int(Address));
            Address = RawAddress;
        }
#endif

        Address += CopyChunk;
        Buffer = (PVOID)((PUCHAR)Buffer + CopyChunk);
        Length -= CopyChunk;
    }

    if (ActualSize) {
        *ActualSize = TotalSize - Length;
    }

    //
    // Flush the instruction cache in case the write was into the instruction
    // stream.  Only do this when writing into the kernel address space,
    // and if any bytes were actually written
    //

    if ((Flags & MMDBG_COPY_WRITE) && Length < TotalSize) {
#if defined(_IA64_)
        //
        // KeSweepCurrentIcacheRange requires a valid virtual address.
        // It is used because KeSweepCurrentICache does not work until
        // the HAL as been initialized.
        //

        if (Flags & MMDBG_COPY_PHYSICAL) {
            KeSweepCurrentIcache();
        }
        else {
            KeSweepCurrentIcacheRange((PVOID)AddressStart, TotalSize - Length);
        }
#else
        __asm wbinvd;
        //   KeSweepCurrentIcache();
#endif

    }

    return Length != 0 ? STATUS_UNSUCCESSFUL : STATUS_SUCCESS;
}

static
VOID
KdpSetCommonState(
    IN ULONG NewState,
    IN Struct_Microsoft_Singularity_X86_ThreadContext *x86Context,
    OUT PDBGKD_ANY_WAIT_STATE_CHANGE WaitStateChange
    )
{
    PCHAR PcMemory = (PCHAR)x86Context->eip;
    ULONG InstrCount;
    PUCHAR InstrStream;

    WaitStateChange->NewState = NewState;
    WaitStateChange->ProcessorLevel = KeProcessorLevel;
    WaitStateChange->Processor = GetCurrentProcessorNumber();
    WaitStateChange->NumberProcessors = KeNumberProcessors;
    WaitStateChange->Thread = SIGN_EXTEND_PTR(x86Context->_thread);
    WaitStateChange->ProgramCounter = SIGN_EXTEND_PTR(x86Context->eip);

    RtlZeroMemory(&WaitStateChange->AnyControlReport,
                  sizeof(WaitStateChange->AnyControlReport));

    //
    // Copy instruction stream immediately following location of event.
    //

    InstrStream = WaitStateChange->ControlReport.InstructionStream;
    KdpCopyFromPtr(InstrStream, PcMemory, DBGKD_MAXSTREAM, &InstrCount);
    WaitStateChange->ControlReport.InstructionCount = (USHORT)InstrCount;

    //
    // Clear breakpoints in copied area.
    // If there were any breakpoints cleared, recopy the instruction area
    // without them.
    //

    // if (KdpDeleteBreakpointRange(PcMemory, PcMemory + InstrCount - 1)) {
    //    KdpCopyFromPtr(InstrStream, PcMemory, InstrCount, &InstrCount);
    // }
}

VOID
KdpSetContextState(
    IN OUT PDBGKD_ANY_WAIT_STATE_CHANGE WaitStateChange,
    IN Struct_Microsoft_Singularity_X86_ThreadContext *x86Context
    )
    //  Routine Description:
    //      Fill in the Wait_State_Change message record.
    //
    //  Arguments:
    //      WaitStateChange - Supplies pointer to record to fill in
    //      x86Context - Supplies a pointer to a context record.
    //
    //  Return Value:
    //      None.
{
#if 0
    PKPRCB Prcb;

    //
    //  Special registers for the x86
    //
    Prcb = KeGetCurrentPrcb();
    WaitStateChange->ControlReport.Dr6 = Prcb->ProcessorState.SpecialRegisters.KernelDr6;
    WaitStateChange->ControlReport.Dr7 = Prcb->ProcessorState.SpecialRegisters.KernelDr7;
#endif

    UINT32 _dr6;
    UINT32 _dr7;
    UINT16 _cs;
    UINT16 _ds;
    UINT16 _es;
    UINT16 _fs;

    __asm {
        mov eax, dr6;
        mov _dr6, eax;
        mov eax, dr7;
        mov _dr7, eax;
        mov ax, cs;
        mov _cs, ax;
        mov ax, ds;
        mov _ds, ax;
        mov ax, es;
        mov _es, ax;
        mov ax, fs;
        mov _fs, ax;
    }

    // I'm not sure we're handling dr6 right here.
    KDDBG("KdpSetContextState dr6=%08x dr7=%08x cs=%04x/%04x\n",
          _dr6, _dr7, _cs, _ds);

    WaitStateChange->ControlReport.Dr6 = _dr6;
    WaitStateChange->ControlReport.Dr7 = _dr7;
    WaitStateChange->ControlReport.SegCs  = _cs;
    WaitStateChange->ControlReport.SegDs  = _ds;
    WaitStateChange->ControlReport.SegEs  = _es;
    WaitStateChange->ControlReport.SegFs  = _fs;
    WaitStateChange->ControlReport.EFlags = x86Context->efl;
    WaitStateChange->ControlReport.ReportFlags = X86_REPORT_INCLUDES_SEGS;

#if !PAGING
    // Let the debugger know so that it doesn't have to retrieve the CS descriptor.
    WaitStateChange->ControlReport.ReportFlags |= X86_REPORT_STANDARD_CS;
#endif
}

ULONG
WcsToStr(WCHAR *pwcsSrc, ULONG Length, PCHAR pszDst)
{
    for (ULONG n = 0; n < Length; n++) {
        *pszDst++ = (char)*pwcsSrc++;
    }
    *pszDst++ = '\0';

    return Length + 1;
}

bool
KdpReportLoadSymbolsStateChange(
    IN WCHAR *PathName,
    IN ULONG PathNameLength,
    IN ULONG64 BaseOfDll,
    IN ULONG   ProcessId,
    IN ULONG   CheckSum,
    IN ULONG   SizeOfImage,
    IN BOOLEAN UnloadSymbols,
    IN OUT Struct_Microsoft_Singularity_X86_ThreadContext *x86Context
    )
    //  Routine Description:
    //      This routine sends a load symbols state change packet to the kernel
    //      debugger and waits for a manipulate state message.
    //
    //  Arguments:
    //      PathName - Supplies a pointer to the pathname of the image whose
    //          symbols are to be loaded.
    //      BaseOfDll - Supplies the base address where the image was loaded.
    //      ProcessId - Unique identifier for process that is using
    //          the symbols.  -1 for system process.
    //      CheckSum - Checksum from image header.
    //      UnloadSymbol - TRUE if the symbols that were previously loaded for
    //          the named image are to be unloaded from the debugger.
    //
    //  Return Value:
    //      A value of TRUE is returned if the exception is handled. Otherwise, a
    //      value of FALSE is returned.
{
    // NB: \nt\sdktools\debuggers\ntsd64\event.cpp
    // PathNameLength = 0, ProcessId = 0, BaseOfDll = -1 for reboot.
    // PathNameLength = 0, ProcessId = 0, BaseOfDll = -2 for hibernate.

    STRING MessageData;
    STRING MessageHeader;
    DBGKD_ANY_WAIT_STATE_CHANGE WaitStateChange;
    KCONTINUE_STATUS Status;

    KDDBG("KdpReportLoadSymbolsStateChange %p\n", x86Context);

    do {
        //
        // Construct the wait state change message and message descriptor.
        //

        KdpSetCommonState(DbgKdLoadSymbolsStateChange, x86Context,
                          &WaitStateChange);

        WaitStateChange.u.LoadSymbols.UnloadSymbols = UnloadSymbols;
        WaitStateChange.u.LoadSymbols.BaseOfDll = BaseOfDll;
        WaitStateChange.u.LoadSymbols.ProcessId = ProcessId;
        WaitStateChange.u.LoadSymbols.CheckSum = CheckSum;
        WaitStateChange.u.LoadSymbols.SizeOfImage = SizeOfImage;
        if (PathName != NULL) {
            WaitStateChange.u.LoadSymbols.PathNameLength =
                WcsToStr(PathName, PathNameLength, KdpMessageBuffer);
        }
        else {
            WaitStateChange.u.LoadSymbols.PathNameLength = 0;
        }
        MessageData.Buffer = KdpMessageBuffer;
        MessageData.Length = (USHORT)WaitStateChange.u.LoadSymbols.PathNameLength;

        KdpSetContextState(&WaitStateChange, x86Context);

        MessageHeader.Length = sizeof(WaitStateChange);
        MessageHeader.Buffer = (PCHAR)&WaitStateChange;

        Status = KdpSendWaitContinue(
                    PACKET_TYPE_KD_STATE_CHANGE64,
                    &MessageHeader,
                    &MessageData,
                    x86Context
                    );

    } while (Status == ContinueProcessorReselected) ;

    return (Status == ContinueSuccess) ? true : false;
}

static
KCONTINUE_STATUS
KdpReportExceptionStateChange(
    IN PEXCEPTION_RECORD64 ExceptionRecord,
    IN OUT Struct_Microsoft_Singularity_X86_ThreadContext *x86Context,
    IN BOOLEAN FirstChance
    )
    //  Routine Description:
    //      This routine sends an exception state change packet to the kernel
    //      debugger and waits for a manipulate state message.
    //
    //  Arguments:
    //      ExceptionRecord - Supplies a pointer to an exception record.
    //      x86Context - Supplies a pointer to a context record.
    //      FirstChance - Supplies a boolean value that determines whether this is
    //          the first or second chance for the exception.
    //
    //  Return Value:
    //      A value of TRUE is returned if the exception is handled. Otherwise, a
    //      value of FALSE is returned.
{
    STRING MessageData;
    STRING MessageHeader;
    DBGKD_ANY_WAIT_STATE_CHANGE WaitStateChange;
    KCONTINUE_STATUS Status;

    KDDBG("KdpReportExceptionStateChange %p\n", x86Context);

    do {

        //
        // Construct the wait state change message and message descriptor.
        //

        KdpSetCommonState(DbgKdExceptionStateChange, x86Context,
                          &WaitStateChange);

        WaitStateChange.u.Exception.ExceptionRecord = *ExceptionRecord;
        WaitStateChange.u.Exception.FirstChance = FirstChance;

        KdpSetContextState(&WaitStateChange, x86Context);

        MessageHeader.Length = sizeof(WaitStateChange);
        MessageHeader.Buffer = (PCHAR)&WaitStateChange;
        MessageData.Length = 0;

        //
        // Send packet to the kernel debugger on the host machine,
        // wait for answer.
        //
        Status = KdpSendWaitContinue(
                    PACKET_TYPE_KD_STATE_CHANGE64,
                    &MessageHeader,
                    &MessageData,
                    x86Context
                    );
    } while (Status == ContinueProcessorReselected) ;

    return Status;
}


static
VOID
KdpReadVirtualMemory(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData
    )
    //  Routine Description:
    //      This function is called in response to a read virtual memory 32-bit
    //      state manipulation message. Its function is to read virtual memory
    //      and return.
    //
    //  Arguments:
    //      m - Supplies a pointer to the state manipulation message.
    //      AdditionalData - Supplies a pointer to a descriptor for the data to read.
    //      x86Context - Supplies a pointer to the current context.
    //
    //  Return Value:
    //      None.
{
    ULONG Length;
    STRING MessageHeader;

    //
    // Trim the transfer count to fit in a single message.
    //

    Length = m->u.ReadMemory.TransferCount;
    if (Length > (PACKET_MAX_SIZE - sizeof(DBGKD_MANIPULATE_STATE64))) {
        Length = PACKET_MAX_SIZE - sizeof(DBGKD_MANIPULATE_STATE64);
    }

    //
    // Move the data to the destination buffer.
    //
    if (m->u.ReadMemory.TargetBaseAddress >= 0x80000000) {
        KDDBG("  Read out of range!\n");

        for (UINT i = 0; i < Length; i++) {
            AdditionalData->Buffer[i] = 0;
        }
        Length = 0;
    }
    else {
        m->ReturnStatus =
            KdpCopyMemoryChunks(m->u.ReadMemory.TargetBaseAddress,
                                AdditionalData->Buffer,
                                Length,
                                0,
                                MMDBG_COPY_UNSAFE,
                                &Length);
    }

    //
    // Set the actual number of bytes read, initialize the message header,
    // and send the reply packet to the host debugger.
    //

    AdditionalData->Length = (USHORT)Length;
    m->u.ReadMemory.ActualBytesRead = Length;

    MessageHeader.Length = sizeof(DBGKD_MANIPULATE_STATE64);
    MessageHeader.Buffer = (PCHAR)m;
    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 AdditionalData,
                 &KdpContext);

    return;
}

static
VOID
KdpWriteVirtualMemory(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData
    )
    //  Routine Description:
    //      This function is called in response of a write virtual memory 32-bit
    //      state manipulation message. Its function is to write virtual memory
    //      and return.
    //
    //  Arguments:
    //      m - Supplies a pointer to the state manipulation message.
    //      AdditionalData - Supplies a pointer to a descriptor for the data to write.
    //      x86Context - Supplies a pointer to the current context.
    //
    //  Return Value:
    //      None.
{

    STRING MessageHeader;

    //
    // Move the data to the destination buffer.
    //

    m->ReturnStatus =
        KdpCopyMemoryChunks(m->u.WriteMemory.TargetBaseAddress,
                            AdditionalData->Buffer,
                            AdditionalData->Length,
                            0,
                            MMDBG_COPY_WRITE | MMDBG_COPY_UNSAFE,
                            &m->u.WriteMemory.ActualBytesWritten);

    //
    // Set the actual number of bytes written, initialize the message header,
    // and send the reply packet to the host debugger.
    //

    MessageHeader.Length = sizeof(DBGKD_MANIPULATE_STATE64);
    MessageHeader.Buffer = (PCHAR)m;
    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 NULL,
                 &KdpContext);

    return;
}

static
VOID
KdpReadPhysicalMemory(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData
    )
    //  Routine Description:
    //      This function is called in response to a read physical memory 32-bit
    //      state manipulation message. Its function is to read physical memory
    //      and return.
    //
    //  Arguments:
    //      m - Supplies a pointer to the state manipulation message.
    //      AdditionalData - Supplies a pointer to a descriptor for the data to read.
    //      x86Context - Supplies a pointer to the current context.
    //
    //  Return Value:
    //      None.
{
    ULONG Length;
    STRING MessageHeader;

    //
    // Trim the transfer count to fit in a single message.
    //

    Length = m->u.ReadMemory.TransferCount;
    if (Length > (PACKET_MAX_SIZE - sizeof(DBGKD_MANIPULATE_STATE64))) {
        Length = PACKET_MAX_SIZE - sizeof(DBGKD_MANIPULATE_STATE64);
    }

    m->ReturnStatus =
        KdpCopyMemoryChunks(m->u.ReadMemory.TargetBaseAddress,
                            AdditionalData->Buffer,
                            Length,
                            0,
                            MMDBG_COPY_UNSAFE | MMDBG_COPY_PHYSICAL,
                            &Length);

    //
    // Set the actual number of bytes read, initialize the message header,
    // and send the reply packet to the host debugger.
    //

    AdditionalData->Length = (USHORT)Length;
    m->u.ReadMemory.ActualBytesRead = Length;

    MessageHeader.Length = sizeof(DBGKD_MANIPULATE_STATE64);
    MessageHeader.Buffer = (PCHAR)m;
    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 AdditionalData,
                 &KdpContext);

    return;
}

static
VOID
KdpWritePhysicalMemory(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData
    )
    //  Routine Description:
    //      This function is called in response of a write physical memory 32-bit
    //      state manipulation message. Its function is to write physical memory
    //      and return.
    //
    //  Arguments:
    //      m - Supplies a pointer to the state manipulation message.
    //      AdditionalData - Supplies a pointer to a descriptor for the data to write.
    //      x86Context - Supplies a pointer to the current context.
    //
    //  Return Value:
    //      None.
{

    STRING MessageHeader;

    //
    // Move the data to the destination buffer.
    //

    m->ReturnStatus =
        KdpCopyMemoryChunks(m->u.WriteMemory.TargetBaseAddress,
                            AdditionalData->Buffer,
                            AdditionalData->Length,
                            0,
                            MMDBG_COPY_WRITE | MMDBG_COPY_UNSAFE | MMDBG_COPY_PHYSICAL,
                            &m->u.WriteMemory.ActualBytesWritten);

    //
    // Set the actual number of bytes written, initialize the message header,
    // and send the reply packet to the host debugger.
    //

    MessageHeader.Length = sizeof(DBGKD_MANIPULATE_STATE64);
    MessageHeader.Buffer = (PCHAR)m;
    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 NULL,
                 &KdpContext);

    return;
}

static
VOID
KdpReadMachineSpecificRegister(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData,
    IN Struct_Microsoft_Singularity_X86_ThreadContext *x86Context
    )
    //  Routine Description:
    //      This function is called in response of a write physical memory 32-bit
    //      state manipulation message. Its function is to write physical memory
    //      and return.
    //
    //  Arguments:
    //      m - Supplies a pointer to the state manipulation message.
    //      AdditionalData - Supplies a pointer to a descriptor for the data to write.
    //      x86Context - Supplies a pointer to the current context.
    //
    //  Return Value:
    //      None.
{

    STRING MessageHeader;

    //
    // Read the MSR
    //

    UINT32 msr = m->u.ReadWriteMsr.Msr;
    UINT32 hi, lo;

    __asm {
        mov ecx, msr;
        rdmsr;
        mov hi, edx;
        mov lo, eax;
    }

    m->u.ReadWriteMsr.DataValueHigh = hi;
    m->u.ReadWriteMsr.DataValueLow = lo;
    m->ReturnStatus = STATUS_SUCCESS;

    //
    // Set the actual number of bytes written, initialize the message header,
    // and send the reply packet to the host debugger.
    //

    MessageHeader.Length = sizeof(DBGKD_MANIPULATE_STATE64);
    MessageHeader.Buffer = (PCHAR)m;
    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 NULL,
                 &KdpContext);

    return;
}

static
VOID
KdpWriteMachineSpecificRegister(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData,
    IN Struct_Microsoft_Singularity_X86_ThreadContext *x86Context
    )
    //  Routine Description:
    //      This function is called in response of a write physical memory 32-bit
    //      state manipulation message. Its function is to write physical memory
    //      and return.
    //
    //  Arguments:
    //      m - Supplies a pointer to the state manipulation message.
    //      AdditionalData - Supplies a pointer to a descriptor for the data to write.
    //      x86Context - Supplies a pointer to the current context.
    //
    //  Return Value:
    //      None.
{

    STRING MessageHeader;

    //
    // Write the MSR
    //

    UINT32 msr = m->u.ReadWriteMsr.Msr;
    UINT32 hi = m->u.ReadWriteMsr.DataValueHigh, lo = m->u.ReadWriteMsr.DataValueLow;

    __asm {
        mov ecx, msr;
        mov edx, hi;
        mov eax, lo;
        wrmsr;
    }

    m->ReturnStatus = STATUS_SUCCESS;

    //
    // Set the actual number of bytes written, initialize the message header,
    // and send the reply packet to the host debugger.
    //

    MessageHeader.Length = sizeof(DBGKD_MANIPULATE_STATE64);
    MessageHeader.Buffer = (PCHAR)m;
    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 NULL,
                 &KdpContext);

    return;
}

static int wcslen(WCHAR *pwz)
{
    int len = 0;

    while (*pwz++) {
        len++;
    }
    return len;
}

static WCHAR * trim(WCHAR *pwz)
{
    WCHAR *pwzBeg = pwz;
    for (; *pwz; pwz++) {
        if (*pwz == '\\') {
            pwzBeg = pwz + 1;
        }
    }
    return pwzBeg;
}

static void KdpFakeOutPsLoadedModuleList()
{
    UINT8* pbImage = (UINT8*)g_pBootInfo->DumpAddr32;
    PMINIDUMP_HEADER pHeader = (PMINIDUMP_HEADER)(pbImage + 0);

#define VERBOSE 1
#if VERBOSE
    printf("KdpFakeOutPsLoadedModuleList (%p):\n"
           "  BootInfo at %p  MINIDUMP_HEADER at %p\n",
           &PsLoadedModuleList, g_pBootInfo, pHeader);
#endif

    // Need to build a data structure which looks like NT's PsLoadedModuleList
    InitializeListHead(&PsLoadedModuleList);

    PMINIDUMP_DIRECTORY pDir
        = (PMINIDUMP_DIRECTORY)(pbImage + pHeader->StreamDirectoryRva);

    for (UINT i = 0; i < pHeader->NumberOfStreams; i++) {
        if (pDir[i].StreamType == ModuleListStream) {
            KdMinidumpModuleList = (PMINIDUMP_MODULE_LIST)(pbImage + pDir[i].Location.Rva);
            break;
        }
    }
    if (KdMinidumpModuleList == NULL) {
#if VERBOSE
        printf("Couldn't find module list.\n");
#endif
        return;
    }

    KdVersionBlock.KernBase = (ULONG64)KdMinidumpModuleList->Modules[0].BaseOfImage;

#if VERBOSE
    printf("MODULE_LIST: %d entries\n", KdMinidumpModuleList->NumberOfModules);
#endif

#if 1
    // We manual include these modules to allow debugging very early in the boot cycle.
    for (UINT m = 0; m < KdMinidumpModuleList->NumberOfModules &&
             m < (sizeof(KdModuleKernelEntry)/sizeof(KdModuleKernelEntry[0])); m++) {

        PMINIDUMP_MODULE pModule = &KdMinidumpModuleList->Modules[m];
        KLDR_DATA_TABLE_ENTRY_WITH_NAME *pEntry = &KdModuleKernelEntry[KdModuleKernelUsed++];

        WCHAR *name = trim((WCHAR *)((pbImage + pModule->ModuleNameRva)+4));
        USHORT nlen = (USHORT)2 * wcslen(name);
        if (nlen > sizeof(pEntry->wzName)) {
            nlen = sizeof(pEntry->wzName);
        }

        pEntry->DllBase = (PVOID *)pModule->BaseOfImage;
        pEntry->CheckSum = pModule->CheckSum;
        pEntry->TimeDateStamp = pModule->TimeDateStamp;
        pEntry->LoadCount = 1;
        pEntry->SizeOfImage = pModule->SizeOfImage;
        memcpy(pEntry->wzName, name, nlen);
        RtlInitUnicodeString(&pEntry->BaseDllName, pEntry->wzName, nlen);
        RtlInitUnicodeString(&pEntry->FullDllName, pEntry->wzName, nlen); // Write back on self.

#if VERBOSE
        printf("%4d: BaseOfImage %8lx SizeOfImage %8x ModuleNameRva %8x\n",
               m,
               pModule->BaseOfImage,
               pModule->SizeOfImage,
               pModule->ModuleNameRva);
        printf("      ModuleName: %ls (%p)\n",
               pEntry->BaseDllName.Buffer,
               pEntry);
#endif

        InsertTailList(&PsLoadedModuleList, &pEntry->InLoadOrderLinks);
    }
#endif
}

VOID
KdpSysGetVersion(
    PDBGKD_GET_VERSION64 Version
    )
    //  Routine Description:
    //      This function returns to the caller a general information packet
    //      that contains useful information to a debugger.  This packet is also
    //      used for a debugger to determine if the writebreakpointex and
    //      readbreakpointex APIs are available.
    //
    //  Arguments:
    //      Version - Supplies the structure to fill in
    //
    //  Return Value:
    //      None.
{
    *Version = KdVersionBlock;
}


static
VOID
KdpGetVersion(
    IN PDBGKD_MANIPULATE_STATE64 m
    )
    //  Routine Description:
    //      This function returns to the caller a general information packet
    //      that contains useful information to a debugger.  This packet is also
    //      used for a debugger to determine if the writebreakpointex and
    //      readbreakpointex APIs are available.
    //
    //  Arguments:
    //      m - Supplies the state manipulation message.
    //
    //  Return Value:
    //      None.
{
    STRING messageHeader;

    messageHeader.Length = sizeof(*m);
    messageHeader.Buffer = (PCHAR)m;

    KdpSysGetVersion(&m->u.GetVersion64);

    //
    // the usual stuff
    //
    m->ReturnStatus = STATUS_SUCCESS;
    m->ApiNumber = DbgKdGetVersionApi;

    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &messageHeader,
                 NULL,
                 &KdpContext
                 );

    return;
} // KdGetVersion

static inline ULONG min(ULONG a, ULONG b)
{
    return a < b ? a : b;
}

static
VOID
KdpReadControlSpace(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData,
    IN Struct_Microsoft_Singularity_X86_ThreadContext * x86Context
    )
    //  Routine Description:
    //      This function is called in response of a read control space state
    //      manipulation message.  Its function is to read implementation
    //      specific system data.
    //
    //  Arguments:
    //      m - Supplies the state manipulation message.
    //      AdditionalData - Supplies any additional data for the message.
    //      Context - Supplies the current context.
    //
    //  Return Value:
    //      None.
{
    PDBGKD_READ_MEMORY64 a = &m->u.ReadMemory;
    STRING MessageHeader;
    ULONG Length;

    MessageHeader.Length = sizeof(*m);
    MessageHeader.Buffer = (PCHAR)m;

    ASSERT(AdditionalData->Length == 0);

    Length = min(min(a->TransferCount,
                     PACKET_MAX_SIZE - sizeof(DBGKD_MANIPULATE_STATE64)),
                 sizeof(KPROCESSOR_STATE) - (ULONG)a->TargetBaseAddress);

    if ((a->TargetBaseAddress + Length <= sizeof(KPROCESSOR_STATE)) &&
        (m->Processor < (ULONG)KeNumberProcessors)) {
        PKPROCESSOR_STATE ProcessorState = &KdpProcessorState[m->Processor];
        if (a->TargetBaseAddress < sizeof(CONTEXT)) {
            // Need to update the thread context information.
            KdSingularityToWindbgContext(x86Context, &ProcessorState->ContextFrame);
        }
        if (a->TargetBaseAddress + Length > sizeof(CONTEXT)) {
            // Need to update the processor context information.
            // ASSERT(m->Processor == 0); // MP support broken.

            ProcessorState->SpecialRegisters.Cr2 = x86Context->cr2;
            ProcessorState->SpecialRegisters.Cr3 = (ULONG)x86Context->cr3;
            KSPECIAL_REGISTERS *pksp = &ProcessorState->SpecialRegisters;

            __asm {
                mov ebx, pksp;

                mov eax, cr0;
                mov [ebx].Cr0, eax;
                mov eax, cr3;
                mov [ebx].Cr3, eax;
                _emit 0x0f;  // mov eax,cr4
                _emit 0x20;
                _emit 0xe0;
                mov [ebx].Cr4, eax;

                // XXX save TR should save segment regs as well.
                str ax;
                mov [ebx].Tr, ax;
#if 0
                mov eax, dr0;
                mov [ebx].KernelDr0, eax;
                mov eax, dr1;
                mov [ebx].KernelDr1, eax;
                mov eax, dr2;
                mov [ebx].KernelDr2, eax;
                mov eax, dr3;
                mov [ebx].KernelDr3, eax;
                mov eax, dr6;
                mov [ebx].KernelDr6, eax;
                mov eax, dr7;
                mov [ebx].KernelDr7, eax;
#endif
            }

            const Struct_Microsoft_Singularity_CpuInfo* cpuInfo = &g_pBootInfo->Cpu0 + m->Processor;
            ProcessorState->SpecialRegisters.Gdtr.Pad = cpuInfo->GdtPtr.pad;
            ProcessorState->SpecialRegisters.Gdtr.Limit = cpuInfo->GdtPtr.limit;
            ProcessorState->SpecialRegisters.Gdtr.Base = cpuInfo->GdtPtr.addr;

            ProcessorState->SpecialRegisters.Idtr.Pad = g_idt.pad;
            ProcessorState->SpecialRegisters.Idtr.Limit = g_idt.limit;
            ProcessorState->SpecialRegisters.Idtr.Base = g_idt.addr;
        }

        m->ReturnStatus = KdpCopyToPtr(AdditionalData->Buffer,
                                       ((uint8*)ProcessorState) + a->TargetBaseAddress,
                                       Length,
                                       &Length);
    }
    else {
        m->ReturnStatus = STATUS_UNSUCCESSFUL;
        Length = 0;
    }

    AdditionalData->Length = (USHORT)Length;
    a->ActualBytesRead = Length;

    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 AdditionalData,
                 &KdpContext);
}

static
VOID
KdpWriteControlSpace(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData,
    IN Struct_Microsoft_Singularity_X86_ThreadContext * x86Context
    )
    //  Routine Description:
    //      This function is called in response of a write control space state
    //      manipulation message.  Its function is to write implementation
    //      specific system data.
    //
    //  Arguments:
    //      m - Supplies the state manipulation message.
    //      AdditionalData - Supplies any additional data for the message.
    //      x86Context - Supplies the current context.
    //
    //  Return Value:
    //      None.
{
    PDBGKD_WRITE_MEMORY64 a = &m->u.WriteMemory;
    ULONG Length;
    STRING MessageHeader;

    MessageHeader.Length = sizeof(*m);
    MessageHeader.Buffer = (PCHAR)m;

    Length = AdditionalData->Length;

    if (((a->TargetBaseAddress + Length) <= sizeof(KPROCESSOR_STATE)) &&
        (m->Processor < (ULONG)KeNumberProcessors)) {
        PKPROCESSOR_STATE ProcessorState = &KdpProcessorState[m->Processor];
        if (a->TargetBaseAddress < sizeof(CONTEXT)) {
            // Need to update the thread context information.
            KdWindbgToSingularityContext(&ProcessorState->ContextFrame, x86Context);
        }
        if (a->TargetBaseAddress + Length > sizeof(CONTEXT)) {
            // Need to update the processor context information.
            KDDBG("   Writing SpecialRegisters.\n");
            ASSERT(m->Processor == 0);  // MP support broken.

#if 0
            // We don't support separate kernel data breakpoints as
            // Singularity uses a single address space.
            kdprintf("  dr0=%p dr1=%p dr2=%p dr3=%p dr6=%p dr7=%p\n",
                     (PVOID)(windbg->Dr0),
                     (PVOID)(windbg->Dr1),
                     (PVOID)(windbg->Dr2),
                     (PVOID)(windbg->Dr3),
                     (PVOID)(windbg->Dr6),
                     (PVOID)(windbg->Dr7));

            KSPECIAL_REGISTERS *pksp = &ProcessorState->SpecialRegisters;
            kdprintf("  cr0=%p cr2=%p cr3=%p cr4=%p\n",
                     pksp->Cr0, pksp->Cr2, pksp->Cr3, pksp->Cr4);

            __asm {
                mov ebx, pksp;

#if 0
                mov eax, [ebx].Cr0;
                mov cr0, eax;
                mov eax, [ebx].Cr2;
                mov cr2, eax;
                mov eax, [ebx].Cr2;
                mov cr3, eax;
                mov eax, [ebx].Cr4;
                _emit 0x0f;  // mov cr4,eax
                _emit 0x22;
                _emit 0xe0;
#endif

                mov eax, [ebx].KernelDr0;
                mov dr0, eax;
                mov eax, [ebx].KernelDr1;
                mov dr1, eax;
                mov eax, [ebx].KernelDr2;
                mov dr2, eax;
                mov eax, [ebx].KernelDr3;
                mov dr3, eax;
                mov eax, [ebx].KernelDr6;
                mov dr6, eax;
                mov eax, [ebx].KernelDr7;
                mov dr7, eax;
            }
            kdprintf("  dr0=%p dr1=%p dr2=%p dr3=%p dr6=%p dr7=%p\n",
                     (PVOID)(pksp->KernelDr0),
                     (PVOID)(pksp->KernelDr1),
                     (PVOID)(pksp->KernelDr2),
                     (PVOID)(pksp->KernelDr3),
                     (PVOID)(pksp->KernelDr6),
                     (PVOID)(pksp->KernelDr7));
            kdprintf("  rr0=%p rr1=%p rr2=%p rr3=%p rr6=%p rr7=%p\n",
                     (PVOID)(pksp->Reserved[0]),
                     (PVOID)(pksp->Reserved[1]),
                     (PVOID)(pksp->Reserved[2]),
                     (PVOID)(pksp->Reserved[3]),
                     (PVOID)(pksp->Reserved[4]),
                     (PVOID)(pksp->Reserved[5]));
#endif
        }

        m->ReturnStatus = KdpCopyFromPtr(((uint8*)ProcessorState) + a->TargetBaseAddress,
                                         AdditionalData->Buffer,
                                         Length,
                                         &Length);
    } else {
        m->ReturnStatus = STATUS_UNSUCCESSFUL;
        Length = 0;
    }

    a->ActualBytesWritten = Length;

    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 AdditionalData,
                 &KdpContext);
}

static
VOID
KdpSetContext(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData,
    IN Struct_Microsoft_Singularity_X86_ThreadContext * x86Context
    )
    //  Routine Description:
    //      This function is called in response of a set context state
    //      manipulation message.  Its function is set the current
    //      context.
    //
    //  Arguments:
    //      m - Supplies the state manipulation message.
    //      AdditionalData - Supplies any additional data for the message.
    //      x86Context - Supplies the current context.
    //
    //  Return Value:
    //      None.
{
    STRING MessageHeader;

    MessageHeader.Length = sizeof(*m);
    MessageHeader.Buffer = (PCHAR)m;

    ASSERT(AdditionalData->Length == sizeof(CONTEXT));

    ASSERT(m->Processor == 0);  // MP support broken.

    if ((m->Processor >= (USHORT)KeNumberProcessors) ||
        (KdpContextSent == FALSE)) {
        m->ReturnStatus = STATUS_UNSUCCESSFUL;
    }
    else {
        m->ReturnStatus = STATUS_SUCCESS;
        KdWindbgToSingularityContext((CONTEXT*)AdditionalData->Buffer, x86Context);
    }

    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 NULL,
                 &KdpContext);
}

static
VOID
KdpGetContext(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData,
    IN Struct_Microsoft_Singularity_X86_ThreadContext *x86Context
    )
    //  Routine Description:
    //      This function is called in response of a get context state
    //      manipulation message.  Its function is to return the current
    //      context.
    //
    //  Arguments:
    //      m - Supplies the state manipulation message.
    //      AdditionalData - Supplies any additional data for the message.
    //      x86Context - Supplies the current context.
    //
    //  Return Value:
    //      None.
{
    STRING MessageHeader;

    MessageHeader.Length = sizeof(*m);
    MessageHeader.Buffer = (PCHAR)m;

    ASSERT(AdditionalData->Length == 0);

    ASSERT(m->Processor == 0);  // MP support broken.

    if (m->Processor >= (USHORT)KeNumberProcessors) {
        m->ReturnStatus = STATUS_UNSUCCESSFUL;
    }
    else {
        m->ReturnStatus = STATUS_SUCCESS;
        AdditionalData->Length = sizeof(CONTEXT);

        KdSingularityToWindbgContext(x86Context, (CONTEXT*)AdditionalData->Buffer);
        KdpContextSent = TRUE;
    }

    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 AdditionalData,
                 &KdpContext);
}


ULONG
KdpAddBreakpoint(
    IN PVOID Address
    )
    //  Routine Description:
    //      This routine adds an entry to the breakpoint table and returns a handle
    //      to the breakpoint table entry.
    //
    //  Arguments:
    //      Address - Supplies the address where to set the breakpoint.
    //
    //  Return Value:
    //
    //      A value of zero is returned if the specified address is already in the
    //      breakpoint table, there are no free entries in the breakpoint table, the
    //      specified address is not correctly aligned, or the specified address is
    //      not valid. Otherwise, the index of the assigned breakpoint table entry
    //      plus one is returned as the function value.
{
    ULONG Index;
    KDP_BREAKPOINT_TYPE Content;
    BOOL Accessible;

    KDDBG2("KdpAddBreakpoint(%p)\n", Address);

    for (Index = 0; Index < BREAKPOINT_TABLE_SIZE; Index++) {
        if (KdpBreakpointTable[Index].Flags  == 0) break;
    }
    if (Index == BREAKPOINT_TABLE_SIZE) {
        KDDBG("KD: ran out of breakpoints!\n");
        return 0;
    }

    Accessible = NT_SUCCESS(KdpCopyFromPtr(&Content,
                                           Address,
                                           sizeof(KDP_BREAKPOINT_TYPE),
                                           NULL));
    KDDBG("KD: memory %saccessible\n", Accessible ? "" : "in");

    if (Accessible) {
        KdpBreakpointTable[Index].Address = Address;
        KdpBreakpointTable[Index].Content = Content;
        KdpBreakpointTable[Index].Flags = KD_BREAKPOINT_IN_USE;

        if (!NT_SUCCESS(KdpCopyToPtr(Address,
                                     &KdpBreakpointInstruction,
                                     sizeof(KDP_BREAKPOINT_TYPE),
                                     NULL))) {
            KDDBG("KD: Unable to write BP!\n");
        }
    }
    else {
        return 0;
    }

    return Index+1;
}

BOOLEAN
KdpDeleteBreakpoint(
    IN ULONG Handle
    )
{
    ULONG Index = Handle - 1;
    KDDBG2("KD: Delete Breakpoint %d\n", Handle);

    if ((Handle == 0) || (Handle > BREAKPOINT_TABLE_SIZE)) {
        KDDBG("KD: Breakpoint %d invalid.\n", Index);
        return FALSE;
    }

    //
    // Replace the instruction contents.
    //
    if (!NT_SUCCESS(KdpCopyToPtr(KdpBreakpointTable[Index].Address,
                                 &KdpBreakpointTable[Index].Content,
                                 sizeof(KDP_BREAKPOINT_TYPE),
                                 NULL))) {
        KDDBG("KD: Breakpoint at 0x%p; unable to clear, flag set.\n",
                  KdpBreakpointTable[Index].Address);
        return FALSE;
    }
    else {
        KDDBG2("KD: Breakpoint at 0x%p cleared.\n",
               KdpBreakpointTable[Index].Address);
        KdpBreakpointTable[Index].Flags = 0;
    }

    return TRUE;
}

VOID
KdpWriteBreakpoint(
    IN PDBGKD_MANIPULATE_STATE64 m
    )
    //  Routine Description:
    //      This function is called in response of a write breakpoint state
    //      manipulation message.  Its function is to write a breakpoint
    //      and return a handle to the breakpoint.
    //
    //  Arguments:
    //      m - Supplies the state manipulation message.
    //      AdditionalData - Supplies any additional data for the message.
    //      x86Context - Supplies the current context.
    //
    //  Return Value:
    //      None.
{
    PDBGKD_WRITE_BREAKPOINT64 a = &m->u.WriteBreakPoint;
    STRING MessageHeader;

    MessageHeader.Length = sizeof(*m);
    MessageHeader.Buffer = (PCHAR)m;

    ASSERT(AdditionalData->Length == 0);

    a->BreakPointHandle = KdpAddBreakpoint((PVOID)(ULONG_PTR)a->BreakPointAddress);
    if (a->BreakPointHandle != 0) {
        m->ReturnStatus = STATUS_SUCCESS;
    }
    else {
        m->ReturnStatus = STATUS_UNSUCCESSFUL;
    }
    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 NULL,
                 &KdpContext);
}

VOID
KdpRestoreBreakpoint(
    IN PDBGKD_MANIPULATE_STATE64 m,
    IN PSTRING AdditionalData
    )
    //  Routine Description:
    //      This function is called in response of a restore breakpoint state
    //      manipulation message.  Its function is to restore a breakpoint
    //      using the specified handle.
    //
    //  Arguments:
    //      m - Supplies the state manipulation message.
    //      AdditionalData - Supplies any additional data for the message.
    //      Context - Supplies the current context.
    //
    //  Return Value:
    //      None.
{
    PDBGKD_RESTORE_BREAKPOINT a = &m->u.RestoreBreakPoint;
    STRING MessageHeader;

    MessageHeader.Length = sizeof(*m);
    MessageHeader.Buffer = (PCHAR)m;

    ASSERT(AdditionalData->Length == 0);
    if (KdpDeleteBreakpoint(a->BreakPointHandle)) {
        m->ReturnStatus = STATUS_SUCCESS;
    }
    else {
        m->ReturnStatus = STATUS_UNSUCCESSFUL;
    }
    KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE,
                 &MessageHeader,
                 NULL,
                 &KdpContext);
}

static
VOID
KdpGetStateChange(
    IN PDBGKD_MANIPULATE_STATE64 ManipulateState,
    IN Struct_Microsoft_Singularity_X86_ThreadContext *x86Context
    )
    //  Routine Description:
    //      Extract continuation control data from Manipulate_State message
    //
    //  Arguments:
    //      ManipulateState - supplies pointer to Manipulate_State packet
    //      x86Context - Supplies a pointer to a context record.
    //
    //  Return Value:
    //      None.
{
    //PKPRCB Prcb;
    //ULONG  Processor;

    if (NT_SUCCESS(ManipulateState->u.Continue2.ContinueStatus)) {
        //
        // If NT_SUCCESS returns TRUE, then the debugger is doing a
        // continue, and it makes sense to apply control changes.
        // Otherwise the debugger is saying that it doesn't know what
        // to do with this exception, so control values are ignored.
        //

        if (ManipulateState->u.Continue2.ControlSet.TraceFlag == TRUE) {
            KDDBG2("KD: Warning - trace flag set\n");
            x86Context->efl |= Struct_Microsoft_Singularity_X86_EFlags_TF;
        }
        else {
            x86Context->efl &= ~Struct_Microsoft_Singularity_X86_EFlags_TF;
        }

        UINT32 _dr7 = ManipulateState->u.Continue2.ControlSet.Dr7;
        x86Context->dr7 = ManipulateState->u.Continue2.ControlSet.Dr7;

#if 0
        __asm {
            mov eax, 0;
            mov dr6, eax;
            mov eax, _dr7;
            mov dr7, eax;
        }
#endif
#if 0
        for (Processor = 0; Processor < (ULONG)KeNumberProcessors; Processor++) {
            Prcb = KiProcessorBlock[Processor];

            Prcb->ProcessorState.SpecialRegisters.KernelDr7 =
                ManipulateState->u.Continue2.ControlSet.Dr7;

            Prcb->ProcessorState.SpecialRegisters.KernelDr6 = 0L;
        }
        if (ManipulateState->u.Continue2.ControlSet.CurrentSymbolStart != 1) {
            KdpCurrentSymbolStart = ManipulateState->u.Continue2.ControlSet.CurrentSymbolStart;
            KdpCurrentSymbolEnd = ManipulateState->u.Continue2.ControlSet.CurrentSymbolEnd;
        }
#endif
    }
}

KCONTINUE_STATUS
KdpSendWaitContinue(
    IN ULONG OutPacketType,
    IN PSTRING OutMessageHeader,
    IN PSTRING OutMessageData OPTIONAL,
    IN OUT Struct_Microsoft_Singularity_X86_ThreadContext *x86Context
    )
    //  Routine Description:
    //      This function sends a packet, and then waits for a continue message.
    //      BreakIns received while waiting will always cause a resend of the
    //      packet originally sent out.  While waiting, manipulate messages
    //      will be serviced.
    //      A resend always resends the original event sent to the debugger,
    //      not the last response to some debugger command.
    //
    //  Arguments:
    //      OutPacketType - Supplies the type of packet to send.
    //      OutMessageHeader - Supplies a pointer to a string descriptor that describes
    //          the message information.
    //      OutMessageData - Supplies a pointer to a string descriptor that describes
    //          the optional message data.
    //      x86Record - Exception context
    //
    //  Return Value:
    //      A value of TRUE is returned if the continue message indicates
    //      success, Otherwise, a value of FALSE is returned.
{

    ULONG Length;
    STRING MessageData;
    STRING MessageHeader;
    DBGKD_MANIPULATE_STATE64 ManipulateState;
    ULONG ReturnCode;
    //    NTSTATUS Status;
    //    KCONTINUE_STATUS ContinueStatus;

    //
    // Loop servicing state manipulation message until a continue message
    // is received.
    //

    MessageHeader.MaximumLength = sizeof(DBGKD_MANIPULATE_STATE64);
    MessageHeader.Buffer = (PCHAR)&ManipulateState;
    MessageData.MaximumLength = arrayof(KdpMessageBuffer);
    MessageData.Buffer = KdpMessageBuffer;
    KdpContextSent = FALSE;

  ResendPacket:

    //
    // Send event notification packet to debugger on host.  Come back
    // here any time we see a breakin sequence.
    //

    KdSendPacket(OutPacketType,
                 OutMessageHeader,
                 OutMessageData,
                 &KdpContext);

    //
    // After sending packet, if there is no response from debugger
    // AND the packet is for reporting symbol (un)load, the debugger
    // will be declared to be not present.  Note If the packet is for
    // reporting exception, the KdSendPacket will never stop.
    //

    if (KdDebuggerNotPresent) {
        return ContinueSuccess;
    }

    for (;;) {
        //
        // Wait for State Manipulate Packet without timeout.
        //

        KDDBG("KdpSendWait::\r");

        do {
            ReturnCode = KdReceivePacket(
                                         PACKET_TYPE_KD_STATE_MANIPULATE,
                                         &MessageHeader,
                                         &MessageData,
                                         &Length,
                                         &KdpContext
                                        );
            KDDBG2("KdReceivePacket returned %d\n", ReturnCode);
            if (ReturnCode == KDP_PACKET_RESEND) {
                goto ResendPacket;
            }
        } while (ReturnCode == KDP_PACKET_TIMEOUT);

        KDDBG2("KdpSendWaitContinue: ManipulateState.ApiNumber=0x%x\n", ManipulateState.ApiNumber);

        //
        // Switch on the return message API number.
        //

        switch (ManipulateState.ApiNumber) {

          case DbgKdReadVirtualMemoryApi:
            KDDBG("KdpSendWait::KdReadVirtualMemory (%8p..%8p) (%d bytes)\n",
                  (ULONG_PTR)ManipulateState.u.ReadMemory.TargetBaseAddress,
                  (ULONG_PTR)ManipulateState.u.ReadMemory.TargetBaseAddress +
                  ManipulateState.u.ReadMemory.TransferCount,
                  ManipulateState.u.ReadMemory.TransferCount);
            KdpReadVirtualMemory(&ManipulateState,&MessageData);
            break;

#if 0
          case DbgKdReadVirtualMemory64Api:
            KdpReadVirtualMemory64(&ManipulateState,&MessageData);
            break;
#endif
          case DbgKdWriteVirtualMemoryApi:
            KDDBG("KdpSendWait::KdWriteVirtualMemory(%8p..%8p)\n",
                  (ULONG_PTR)ManipulateState.u.WriteMemory.TargetBaseAddress,
                  (ULONG_PTR)ManipulateState.u.WriteMemory.TargetBaseAddress +
                  ManipulateState.u.WriteMemory.TransferCount);
            KdpWriteVirtualMemory(&ManipulateState,&MessageData);
            break;
#if 0
          case DbgKdWriteVirtualMemory64Api:
            KdpWriteVirtualMemory64(&ManipulateState,&MessageData);
            break;
#endif
          case DbgKdGetVersionApi:
            KDDBG("KdpSendWait::KdGetVersion()\n");
            KdpGetVersion(&ManipulateState);
            break;

          case DbgKdGetContextApi:
            KDDBG("KdpSendWait::KdGetContext(p=%x)\n", ManipulateState.Processor);
            KdpGetContext(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdReadControlSpaceApi:
            KDDBG("KdpSendWait::KdReadControlSpace  (%8p..%8p p=%x)\n",
                  (ULONG_PTR)ManipulateState.u.ReadMemory.TargetBaseAddress,
                  (ULONG_PTR)ManipulateState.u.ReadMemory.TargetBaseAddress +
                  ManipulateState.u.ReadMemory.TransferCount,
                  (ULONG_PTR)ManipulateState.Processor);
            KdpReadControlSpace(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdWriteControlSpaceApi:
            KDDBG("KdpSendWait::KdWriteControlSpace (%8p..%8p p=%x)\n",
                  (ULONG_PTR)ManipulateState.u.WriteMemory.TargetBaseAddress,
                  (ULONG_PTR)ManipulateState.u.WriteMemory.TargetBaseAddress +
                  ManipulateState.u.WriteMemory.TransferCount,
                  (ULONG_PTR)ManipulateState.Processor);
            KdpWriteControlSpace(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdSetContextApi:
            KDDBG("KdpSendWait::KdSetContext(p=%x)\n", ManipulateState.Processor);
            KdpSetContext(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdWriteBreakPointApi:
            KDDBG("KdpSendWait::KdWriteBreakPoint(%p)\n",
                  ManipulateState.u.WriteBreakPoint.BreakPointAddress);
            KdpWriteBreakpoint(&ManipulateState);
            break;

          case DbgKdRestoreBreakPointApi:
            if (ManipulateState.u.RestoreBreakPoint.BreakPointHandle < 0x8 ||
                ManipulateState.u.RestoreBreakPoint.BreakPointHandle > 0x1e) {
                KDDBG("KdpSendWait::KdRestoreBreakpoint(h=%x)\n",
                      ManipulateState.u.RestoreBreakPoint.BreakPointHandle);
            }
            KdpRestoreBreakpoint(&ManipulateState,&MessageData);
            break;

          case DbgKdContinueApi:
            KDDBG("KdpSendWait::KdContinue(ContinueStatus=%08x)\n",
                  ManipulateState.u.Continue.ContinueStatus);
            if (NT_SUCCESS(ManipulateState.u.Continue.ContinueStatus)) {
                return ContinueSuccess;
            }
            else {
                return ContinueError;
            }
            break;

          case DbgKdContinueApi2:
            KDDBG("KdpSendWait::KdContinue2(ContinueStatus=%08x)\n",
                  ManipulateState.u.Continue2.ContinueStatus);
            if (NT_SUCCESS(ManipulateState.u.Continue2.ContinueStatus)) {
                KDDBG("KdpSendWait::KdpGetStateChange()\n");
                KdpGetStateChange(&ManipulateState,x86Context);
                return ContinueSuccess;
            }
            else {
                KDDBG("KdpSendWait::ContinueError!\n");
                return ContinueError;
            }
            break;

          case DbgKdReadPhysicalMemoryApi:
            KDDBG("KdpSendWait::KdReadPhysicalMemory (%8p..%8p)\n",
                  (ULONG_PTR)ManipulateState.u.ReadMemory.TargetBaseAddress,
                  (ULONG_PTR)ManipulateState.u.ReadMemory.TargetBaseAddress +
                  ManipulateState.u.ReadMemory.TransferCount);
            // KdpReadPhysicalMemory(&ManipulateState,&MessageData,x86Context);
            KdpReadPhysicalMemory(&ManipulateState,&MessageData);
            break;

          case DbgKdWritePhysicalMemoryApi:
            KdpWritePhysicalMemory(&ManipulateState,&MessageData);
            KDDBG("KdpSendWait::KdWritePhysicalMemory (%8p..%8p)\n",
                  (ULONG_PTR)ManipulateState.u.WriteMemory.TargetBaseAddress,
                  (ULONG_PTR)ManipulateState.u.WriteMemory.TargetBaseAddress +
                  ManipulateState.u.ReadMemory.TransferCount);
            break;

          case DbgKdSwitchProcessor:
              {
                  // KdRestore(FALSE);
                  bool switched = Class_Microsoft_Singularity_MpExecution::g_SwitchFrozenProcessor((int32) ManipulateState.Processor);
                  return (switched == true) ? ContinueNextProcessor : ContinueSuccess;
                  // KdSave(FALSE);
              }
            break;

          case DbgKdReadMachineSpecificRegister:
            KdpReadMachineSpecificRegister(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdWriteMachineSpecificRegister:
            KdpWriteMachineSpecificRegister(&ManipulateState,&MessageData,x86Context);
            break;

#if 0 // Haven't implemented most of the protocol yet!
          case DbgKdCheckLowMemoryApi:
            KdpCheckLowMemory (&ManipulateState);
            break;

          case DbgKdReadControlSpaceApi:
            KdpReadControlSpace(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdWriteControlSpaceApi:
            KdpWriteControlSpace(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdReadIoSpaceApi:
            KdpReadIoSpace(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdWriteIoSpaceApi:
            KdpWriteIoSpace(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdReadIoSpaceExtendedApi:
            KdpReadIoSpaceExtended(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdWriteIoSpaceExtendedApi:
            KdpWriteIoSpaceExtended(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdGetBusDataApi:
            KdpGetBusData(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdSetBusDataApi:
            KdpSetBusData(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdRebootApi:
            HalReturnToFirmware(HalRebootRoutine);
            break;

#if defined(i386)
          case DbgKdSetSpecialCallApi:
            KdSetSpecialCall(&ManipulateState,x86Context);
            break;

          case DbgKdClearSpecialCallsApi:
            KdClearSpecialCalls();
            break;

          case DbgKdSetInternalBreakPointApi:
            KdSetInternalBreakpoint(&ManipulateState);
            break;

          case DbgKdGetInternalBreakPointApi:
            KdGetInternalBreakpoint(&ManipulateState);
            break;

          case DbgKdClearAllInternalBreakpointsApi:
            KdpNumInternalBreakpoints = 0;
            break;

#endif // i386

          case DbgKdCauseBugCheckApi:
            KdpCauseBugCheck(&ManipulateState);
            break;

          case DbgKdPageInApi:
            KdpNotSupported(&ManipulateState);
            break;

          case DbgKdWriteBreakPointExApi:
            Status = KdpWriteBreakPointEx(&ManipulateState,
                                          &MessageData,
                                          x86Context);
            if (Status) {
                ManipulateState.ApiNumber = DbgKdContinueApi;
                ManipulateState.u.Continue.ContinueStatus = Status;
                return ContinueError;
            }
            break;

          case DbgKdRestoreBreakPointExApi:
            KdpRestoreBreakPointEx(&ManipulateState,&MessageData,x86Context);
            break;

          case DbgKdSearchMemoryApi:
            KdpSearchMemory(&ManipulateState, &MessageData, x86Context);
            break;

          case DbgKdFillMemoryApi:
            KdpFillMemory(&ManipulateState, &MessageData, x86Context);
            break;

          case DbgKdQueryMemoryApi:
            KdpQueryMemory(&ManipulateState, x86Context);
            break;

            //
            // Invalid message.
            //
#endif // XXX Haven't implemented most of the protocol yet!

          default:
            kdprintf("KdpSendWaitContinue: unrecognized API number 0x%x\n", ManipulateState.ApiNumber);
            MessageData.Length = 0;
            ManipulateState.ReturnStatus = STATUS_UNSUCCESSFUL;
            KdSendPacket(PACKET_TYPE_KD_STATE_MANIPULATE, &MessageHeader, &MessageData, &KdpContext);
            break;
        }
    }
}
//
//////////////////////////////////////////////////////////////////////////////

BOOLEAN
KdPrintString(
    IN PSTRING Output,
    IN BOOLEAN Unicode
    )
    //  Routine Description:
    //      This routine prints a string.
    //
    //  Arguments:
    //      Output - Supplies a pointer to a string descriptor for the output string.
    //
    //  Return Value:
    //      TRUE if Control-C present in input buffer after print is done.
    //      FALSE otherwise.
{

    ULONG Length;
    STRING MessageData;
    STRING MessageHeader;
    DBGKD_DEBUG_IO DebugIo;

    if (KdDebuggerNotPresent) {
        return FALSE;
    }

    KdpLock();

    // Move the output string to the message buffer.
    //
    if (Unicode) {
        WCHAR *pBuffer = (WCHAR *)Output->Buffer;
        for (int i = 0; i < Output->Length; i++) {
            KdpMessageBuffer[i] = (char)pBuffer[i];
        }
        Length = Output->Length;
    }
    else {
        KdpCopyFromPtr(KdpMessageBuffer,
                       Output->Buffer,
                       Output->Length,
                       &Length);
    }

    // If the total message length is greater than the maximum packet size,
    // then truncate the output string.
    //
    if ((sizeof(DBGKD_DEBUG_IO) + Length) > PACKET_MAX_SIZE) {
        Length = PACKET_MAX_SIZE - sizeof(DBGKD_DEBUG_IO);
    }

    // Construct the print string message and message descriptor.
    //
    DebugIo.ApiNumber = DbgKdPrintStringApi;
    DebugIo.ProcessorLevel = KeProcessorLevel;
    DebugIo.Processor = (USHORT)GetCurrentProcessorNumber();
    DebugIo.u.PrintString.LengthOfString = Length;
    MessageHeader.Length = sizeof(DBGKD_DEBUG_IO);
    MessageHeader.Buffer = (PCHAR)&DebugIo;

    // Construct the print string data and data descriptor.
    //
    MessageData.Length = (USHORT)Length;
    MessageData.Buffer = (PCHAR) KdpMessageBuffer;

    // Send packet to the kernel debugger on the host machine.
    //
    KdSendPacket(PACKET_TYPE_KD_DEBUG_IO,
                 &MessageHeader,
                 &MessageData,
                 &KdpContext);

    KdpUnlock();

    return FALSE;
}


// This should be fixed.
VOID
KdPutChar(
    CHAR c
)
{
     static char _KdpDebugStringBuf[KDP_MESSAGE_BUFFER_SIZE];
     static STRING KdpDebugString = { 0, KDP_MESSAGE_BUFFER_SIZE, (PCHAR)&_KdpDebugStringBuf };

     if (KdpInDebugger) {
         koutput(NULL, c);
         return;
     }

     KdpDebugString.Buffer[KdpDebugString.Length++] = c;
     if (c == '\n' || KdpDebugString.Length == KdpDebugString.MaximumLength) {
          KdPrintString(&KdpDebugString, FALSE);
          KdpDebugString.Length = 0;
     }
}

/////////////////////////////////////////////////////// Methods Exposed to C#.
//
bool Class_Microsoft_Singularity_DebugStub::
g_Trap(Struct_Microsoft_Singularity_X86_ThreadContext *context, bool firstChance)
{
    EXCEPTION_RECORD64 er;
    bool handled;
    RtlZeroMemory(&er, sizeof(er));

    KdpLock();

    // Breakpoints:
    switch (context->num) {

        case Struct_Microsoft_Singularity_X86_EVectors_SingleStep:
            er.ExceptionCode = STATUS_SINGLE_STEP;
            er.ExceptionAddress = (ULONG64)context->eip;
            // context->efl &= ~Struct_Microsoft_Singularity_X86_EFlags_TF;
            break;

        case Struct_Microsoft_Singularity_X86_EVectors_Breakpoint:
            context->eip -= 1;
            er.ExceptionCode = STATUS_BREAKPOINT;
            er.NumberParameters = 1;
            er.ExceptionInformation[0] = BREAKPOINT_BREAK;
            er.ExceptionAddress = (ULONG64)context->eip;
            break;

        case Struct_Microsoft_Singularity_X86_EVectors_IllegalInstruction:
            er.ExceptionCode = STATUS_ILLEGAL_INSTRUCTION;
            break;

        case Struct_Microsoft_Singularity_X86_EVectors_PageFault:
            KDDBG("KD: 0x0E %d\n", context->num);
            er.ExceptionCode = STATUS_ACCESS_VIOLATION;
            er.ExceptionAddress = (UINT64)context->eip;
            er.NumberParameters = 1;
            er.ExceptionInformation[0] = context->cr2;
            break;

        case Struct_Microsoft_Singularity_X86_EVectors_FirstChanceException: {
            KdDebugTrapData *trapData = (KdDebugTrapData *) (context->eax);
            switch(trapData->tag) {
                case KdDebugTrapData::FIRST_CHANCE_EXCEPTION:
                    context->eax = trapData->u.firstChanceException.throwAddr;
                    KDDBG("KD: First chance C# exception\n");
                    // er.ExceptionCode = STATUS_CPP_EH_EXCEPTION; //0xe06d7363;
                    er.ExceptionCode = STATUS_VCPP_EXCEPTION; //0x8000ff1f;
                    er.ExceptionAddress = (UINT64)context->eip;
                    er.NumberParameters = 1;
                    er.ExceptionInformation[0] = BREAKPOINT_BREAK;
                    break;
                case KdDebugTrapData::LOADED_BINARY:
                    KDDBG("KD: Loaded binary\n");
                    KdpUnlock();
                    LoadedBinary(context);
                    return true;
                case KdDebugTrapData::UNLOADED_BINARY:
                    KDDBG("KD: Unloaded binary\n");
                    KdpUnlock();
                    UnloadedBinary(context);
                    return true;
                default:
                    KDDBG("KD: Unexpected interrupt %d\n", context->num);
                    er.ExceptionCode = 0x80000000 + context->num;
                    er.ExceptionAddress = (UINT64)context->eip;
                    break;
            }
            break;
        }

        case Struct_Microsoft_Singularity_X86_EVectors_SecondChanceException:
            KDDBG("KD: Second chance C# exception\n");
            er.ExceptionCode = STATUS_VCPP_EXCEPTION;
            er.ExceptionAddress = (UINT64)context->eip;
            break;

        case Struct_Microsoft_Singularity_X86_EVectors_DebuggerBreakRequest:
            KDDBG("KD: Debugger ctrl-break\n");
            er.ExceptionCode = STATUS_BREAKPOINT;
            er.ExceptionInformation[0] = BREAKPOINT_BREAK;
            er.ExceptionAddress = (UINT64)context->eip;
            break;

        default:
            KDDBG("KD: Unexpected interrupt %d\n", context->num);
            er.ExceptionCode = 0x80000000 + context->num;
            er.ExceptionAddress = (UINT64)context->eip;
            break;

    }

    KDDBG("Trap: Context at %p\n", context);
    KDDBG("  CXT=%08x  THR=%08x\n",
          context, context->_thread);
    KDDBG("  EIP=%08x  EFL=%08x  ERR=%08x  CR2=%08x\n",
          context->eip, context->efl, context->err, context->cr2);
    KDDBG("  EAX=%08x  EBX=%08x  ECX=%08x  EDX=%08x\n",
          context->eax, context->ebx, context->ecx, context->edx);
    KDDBG("  ESP=%08x  EBP=%08x  ESI=%08x  EDI=%08x\n",
          context->esp, context->ebp, context->esi, context->edi);

#if 0
    kdprintf("Exception EIPs = %08x", er.ExceptionAddress);
    uintptr ebp = context->ebp;
    for (int i = 0; i < 3 && ebp >= Struct_Microsoft_Singularity_BootInfo_PHYSICAL_DISABLED; i++) {

        uintptr next = ((uintptr *)ebp)[0];
        uintptr code = ((uintptr *)ebp)[1];

        kdprintf(" %08x", code);
        ebp = next;
    }
    kdprintf("\n");
#endif

    KdpEnter();

    handled = (KdpReportExceptionStateChange(&er, context, firstChance) == ContinueSuccess);
    if (context->num == Struct_Microsoft_Singularity_X86_EVectors_SingleStep &&
        (context->efl & Struct_Microsoft_Singularity_X86_EFlags_TF) == 0) {
#if 0
        kdprintf("Continuing to eip=%08x [efl=%08x]\n", context->eip, context->efl);
#endif

#if 0
        if (context->eip != 0 && *(uint8*)(context->eip) == 0x9c) {
            // we always override into single-step mode if the next instruction is pushfd.
            context->efl |= Struct_Microsoft_Singularity_X86_EFlags_TF;
            kdprintf("Continuing to eip=%08x [efl=%08x] ****\n", context->eip, context->efl);
#if 0
            kdprintf("  CXT=%08x  THR=%08x  REG=%02x\n",
                     context, context->_thread, context->regs);
            kdprintf("  EIP=%08x  EFL=%08x  ERR=%08x  CR2=%08x\n",
                     context->eip, context->efl, context->err, context->cr2);
            kdprintf("  EAX=%08x  EBX=%08x  ECX=%08x  EDX=%08x\n",
                     context->eax, context->ebx, context->ecx, context->edx);
            kdprintf("  ESP=%08x  EBP=%08x  ESI=%08x  EDI=%08x\n",
                     context->esp, context->ebp, context->esi, context->edi);
            kdprintf("  DR0=%08x  DR1=%08x  DR2=%08x  DR3=%08x\n",
                     context->dr0, context->dr1, context->dr2, context->dr3);
            kdprintf("  DR6=%08x  DR7=%08x  DR2=%08x  DR3=%08x\n",
                     context->dr6, context->dr7, context->dr2, context->dr3);
#endif
        }
#endif
    }

#if 0
    if (context->num == Struct_Microsoft_Singularity_X86_EVectors_SingleStep &&
        context->eip != 0 && *(uint8*)(context->eip) == 0x9c) {
        kdprintf("  CXT=%08x  THR=%08x  REG=%02x\n",
                 context, context->_thread, context->regs);
        kdprintf("  EIP=%08x  EFL=%08x  ERR=%08x  CR2=%08x\n",
                 context->eip, context->efl, context->err, context->cr2);
        kdprintf("  EAX=%08x  EBX=%08x  ECX=%08x  EDX=%08x\n",
                 context->eax, context->ebx, context->ecx, context->edx);
        kdprintf("  ESP=%08x  EBP=%08x  ESI=%08x  EDI=%08x\n",
                 context->esp, context->ebp, context->esi, context->edi);
        kdprintf("  DR0=%08x  DR1=%08x  DR2=%08x  DR3=%08x\n",
                 context->dr0, context->dr1, context->dr2, context->dr3);
        kdprintf("  DR6=%08x  DR7=%08x  DR2=%08x  DR3=%08x\n",
                 context->dr6, context->dr7, context->dr2, context->dr3);
    }
    kdprintf("-- Trap num=0x%02x eip=%08x\n", context->num, context->eip);
#endif

    KdpLeave();

    KdpUnlock();

    return handled;
}

bool Class_Microsoft_Singularity_DebugStub::
g_TrapForProcessorSwitch(Struct_Microsoft_Singularity_X86_ThreadContext *context)
{
    EXCEPTION_RECORD64 er;

    RtlZeroMemory(&er, sizeof(er));
    er.ExceptionCode    = STATUS_WAKE_SYSTEM_DEBUGGER;
    er.ExceptionRecord  = (ULONG64)&er;
    er.ExceptionAddress = (ULONG64)context->eip;

    // KdSave(FALSE);
    KCONTINUE_STATUS status = KdpReportExceptionStateChange(&er, context, true);
    // KdRestore(FALSE);
    return status == ContinueSuccess;
}

void Class_Microsoft_Singularity_DebugStub::g_AddProcessor(int cpuId)
{
    KdpLock();
    KeNumberProcessors = cpuId + 1;
    KdpUnlock();
}

void Class_Microsoft_Singularity_DebugStub::g_RevertToUniprocessor()
{
    KdpLock();
    KeNumberProcessors = 1;
    KdpUnlock();
}

bool Class_Microsoft_Singularity_DebugStub::g_PollForBreak()
{
    //    KdpLock();

    // Don't re-enter debugger if already debugging.
    if (KdpInDebugger) {
        return FALSE;
    }

    // If the debugger is enabled, see if a breakin by the kernel
    // debugger is pending.
    // We might want to enable retry here.  The transports support it.
    if (KdDebuggerNotPresent) {
        return false;
    }

    // Did we already record a break from the host?
    if (KdpContext.KdpControlCPending) {
        KdpContext.KdpControlCPending = FALSE;
        return true;
    }

    bool success = KdPollBreakIn();
    //    KdpUnlock();
    return success;
}

void Class_Microsoft_Singularity_DebugStub::g_Break()
{
    __asm int 3;
}

bool Class_Microsoft_Singularity_DebugStub::g_LoadedBinary(UIntPtr baseAddress,
                                                           UIntPtr bytes,
                                                           Class_System_String *name,
                                                           uint32 checksum,
                                                           uint32 timestamp,
                                                           bool silent)
{
    return g_LoadedBinary(baseAddress,
                          bytes,
                          (UIntPtr)&name->m_firstChar,
                          checksum,
                          timestamp,
                          silent);
}

bool Class_Microsoft_Singularity_DebugStub::g_LoadedBinary(UIntPtr baseAddress,
                                                           UIntPtr bytes,
                                                           UIntPtr name,
                                                           uint32 checksum,
                                                           uint32 timestamp,
                                                           bool silent)
{
    KdDebugTrapData trapData, *trapDataPtr = &trapData;
    trapData.tag = KdDebugTrapData::LOADED_BINARY;
    trapData.u.loadedBinary.baseAddress = baseAddress;
    trapData.u.loadedBinary.bytes = bytes;
    trapData.u.loadedBinary.name = name;
    trapData.u.loadedBinary.checksum = checksum;
    trapData.u.loadedBinary.timestamp = timestamp;
    trapData.u.loadedBinary.silent = silent;

    // Call LoadedBinary via an __asm int 29:
    __asm {
        mov eax, trapDataPtr;
        int 29;
    }

    return trapData.u.loadedBinary.ret;
}

static void LoadedBinary(Struct_Microsoft_Singularity_X86_ThreadContext *context)
{
    KdDebugTrapData *trapData = (KdDebugTrapData *) (context->eax);
    UIntPtr baseAddress = trapData->u.loadedBinary.baseAddress;
    UIntPtr bytes = trapData->u.loadedBinary.bytes;
    UIntPtr nameof = trapData->u.loadedBinary.name;
    uint32 checksum = trapData->u.loadedBinary.checksum;
    uint32 timestamp = trapData->u.loadedBinary.timestamp;
    bool silent = trapData->u.loadedBinary.silent;

    KLDR_DATA_TABLE_ENTRY_WITH_NAME *pEntry;
    bool good = false;
    WCHAR * name = trim((WCHAR *)nameof);
    USHORT nlen = (USHORT)2 * wcslen(name);
    if (nlen > sizeof(pEntry->wzName)) {
        nlen = sizeof(pEntry->wzName);
    }

    KdpLock();
#if 0 // Debug module names
    if (name != NULL) {
        kdprintf("LoadedBinary(%08lx: %ls)\n", (uint64)(uintptr)name, name);
    }
    else {
        kdprintf("LoadedBinary(%08lx)\n", (uint64)(uintptr)name);
    }
#endif

    for (int i = 0; i < ARRAYOF(KdModuleKernelEntry); i++) {
        pEntry = &KdModuleKernelEntry[i];

        if (pEntry->DllBase == 0) {
            pEntry->DllBase = (PVOID *)baseAddress;
            pEntry->CheckSum = checksum;
            pEntry->TimeDateStamp = timestamp;
            pEntry->LoadCount = 1;
            pEntry->SizeOfImage = (uintptr)bytes;
            memcpy(pEntry->wzName, name, nlen);
            RtlInitUnicodeString(&pEntry->FullDllName, name, nlen);
            RtlInitUnicodeString(&pEntry->BaseDllName, name, nlen);

#if 0
            printf("----: BaseOfImage %8lx SizeOfImage %8x ModuleName %8x\n",
                   (uint64)(uintptr)baseAddress,
                   (uintptr)bytes,
                   (uintptr)pEntry->BaseDllName.Buffer);
            printf("      ModuleName: %ls (%p)\n",
                   pEntry->BaseDllName.Buffer,
                   pEntry);
#endif

            // We should insert in the right order in the list...
            InsertTailList(&PsLoadedModuleList, &pEntry->InLoadOrderLinks);
            good = true;
            break;
        }
    }

    if (!silent) {
        if (good) {
            KdpReportLoadSymbolsStateChange(pEntry->BaseDllName.Buffer,
                                            pEntry->BaseDllName.Length,
                                            (ULONG64)baseAddress,
                                            (ULONG)0,
                                            checksum,
                                            (LONG)bytes,
                                            FALSE,
                                            context);
        }
        else {
            KdpReportLoadSymbolsStateChange(NULL,
                                            0,
                                            (ULONG64)baseAddress,
                                            (ULONG)0,
                                            checksum,
                                            (LONG)bytes,
                                            FALSE,
                                            context);
        }
    }
    KdpUnlock();

    trapData->u.loadedBinary.ret = good;
}

bool Class_Microsoft_Singularity_DebugStub::
g_UnloadedBinary(UIntPtr baseAddress, bool silent)
{
    KdDebugTrapData trapData, *trapDataPtr = &trapData;
    trapData.tag = KdDebugTrapData::UNLOADED_BINARY;
    trapData.u.unloadedBinary.baseAddress = baseAddress;
    trapData.u.unloadedBinary.silent = silent;

    // Call UnloadedBinary via an __asm int 29:
    __asm {
        mov eax, trapDataPtr;
        int 29;
    }

    return trapData.u.unloadedBinary.ret;
}

static void UnloadedBinary(Struct_Microsoft_Singularity_X86_ThreadContext *context)
{
    KdDebugTrapData *trapData = (KdDebugTrapData *) (context->eax);
    UIntPtr baseAddress = trapData->u.unloadedBinary.baseAddress;
    bool silent = trapData->u.unloadedBinary.silent;

    bool good = false;

    KdpLock();
    KLDR_DATA_TABLE_ENTRY_WITH_NAME *pEntry = NULL;
    for (int i = 0; i < ARRAYOF(KdModuleKernelEntry); i++) {
        pEntry = &KdModuleKernelEntry[i];

        if (pEntry->DllBase == (PVOID*)baseAddress) {
            RemoveEntryList(&pEntry->InLoadOrderLinks);
            good = true;
            break;
        }
    }

    if (good) {
        if (!silent) {
            // Only tell debugger if we found an image name.
            // The debugger ignores unload requests that lack a processId (not us)
            // or a path name.

            KdpReportLoadSymbolsStateChange(pEntry->BaseDllName.Buffer,
                                            pEntry->BaseDllName.Length,
                                            (ULONG64)baseAddress,
                                            (ULONG)0,
                                            0,
                                            0,
                                            TRUE,
                                            context);
        }
        RtlZeroMemory(pEntry, sizeof(*pEntry));
    }
    KdpUnlock();

    trapData->u.unloadedBinary.ret = good;
}

bool Class_Microsoft_Singularity_DebugStub::g_IsDebuggerPresent()
{
    return !KdDebuggerNotPresent;
}

//////////////////////////////////////////////////////////////////////////////
// Note: Leaves the lock held by the client code, so it had better be trustworthy.
void Class_Microsoft_Singularity_DebugStub::g_PrintBegin(WCHAR **buffer, int *length)
{
    if (KdDebuggerNotPresent) {
        *buffer = NULL;
        *length = 0;
        return;
    }
    else {
        KdpLock();

        *buffer = (WCHAR *)KdpMessageBuffer;
        *length = sizeof(KdpMessageBuffer) / sizeof(WCHAR);
    }
}

// Note: Assumes the lock is held, so the client code had better be trustworthy.
void Class_Microsoft_Singularity_DebugStub::g_PrintComplete(WCHAR *buffer, int length)
{
    if (KdDebuggerNotPresent) {
        return;
    }

    CHAR *out = KdpMessageBuffer;
    if (length > arrayof(KdpMessageBuffer)) {
        length = arrayof(KdpMessageBuffer);
    }

    for (int i = 0; i < length; i++) {
        *out++ = (CHAR)*buffer++;
    }

    // If the total message length is greater than the maximum packet size,
    // then truncate the output string.
    //
    if ((sizeof(DBGKD_DEBUG_IO) + length) > PACKET_MAX_SIZE) {
        length = PACKET_MAX_SIZE - sizeof(DBGKD_DEBUG_IO);
    }

    //
    // Construct the print string message and message descriptor.
    //
    DBGKD_DEBUG_IO DebugIo;
    DebugIo.ApiNumber = DbgKdPrintStringApi;
    DebugIo.ProcessorLevel = KeProcessorLevel;
    DebugIo.Processor = (USHORT)GetCurrentProcessorNumber();
    DebugIo.u.PrintString.LengthOfString = length;

    STRING MessageHeader;
    MessageHeader.Length = sizeof(DBGKD_DEBUG_IO);
    MessageHeader.Buffer = (PCHAR)&DebugIo;

    //
    // Construct the print string data and data descriptor.
    //
    STRING MessageData;
    MessageData.Length = (USHORT)length;
    MessageData.Buffer = KdpMessageBuffer;

    //
    // Send packet to the kernel debugger on the host machine.
    //
    KdSendPacket(PACKET_TYPE_KD_DEBUG_IO,
                 &MessageHeader,
                 &MessageData,
                 &KdpContext);

    KdpUnlock();
}

void Class_Microsoft_Singularity_DebugStub::g_Print(WCHAR *buf, int len)
{
    if (KdDebuggerNotPresent) {
        return;
    }

    WCHAR *buffer;
    int length;

    g_PrintBegin(&buffer, &length);
    g_PrintComplete(buf, len);
}

void Class_Microsoft_Singularity_DebugStub::g_Print(WCHAR *buf)
{
    int len = 0;

    while (buf[len] != '\0') {
        len++;
    }
    g_Print(buf, len);
}

//
///////////////////////////////////////////////////////////////// End of File.
