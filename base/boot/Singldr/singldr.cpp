//////////////////////////////////////////////////////////////////////////////
//
//  singldr.cpp - Singularity PXE Boot Loader.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#pragma data_seg("_TEXT")

//////////////////////////////////////////////////////////////////////////////
//
#pragma warning(disable: 4103)

#include "singldr.h"
#include "fnames.h"
#include "inifile.h"
#include "pxe.h"
#include "fatdevice.h"
#include "jolietdevice.h"
#include "usbdevice.h"
#include "printf.cpp"
#include "debug.cpp"
#include "pci.h"

//////////////////////////////////////////////////////////////////////////////
//

extern "C" void __cdecl BootGetBiosInfo(Struct_Microsoft_Singularity_BootInfo __far *);
extern "C" void __cdecl BootPhase2(Struct_Microsoft_Singularity_BootInfo __far *);
extern "C" void __cdecl StopPhase0(void);
extern "C" void __cdecl StopPhase3();
extern "C" int _cdecl BootGetSMAP(Struct_Microsoft_Singularity_SMAPINFO __far * pSmap, uint32 __far * pNext);
extern "C" void __cdecl BootHalt(void);
extern "C" void __cdecl Reset(void);

extern "C" void __cdecl MpEnter(void);
extern "C" void __cdecl MpBootPhase2(Struct_Microsoft_Singularity_BootInfo __far *, Struct_Microsoft_Singularity_CpuInfo __far *);
extern "C" uint16 MpStartupLock;

extern "C" void __cdecl IdtEnter0(void);
extern "C" void __cdecl IdtEnter1(void);
extern "C" void __cdecl IdtEnterN(void);
extern "C" uint32 IdtTarget;
extern "C" uint8 undump_dat[];;

extern "C" void IoSpaceWrite8(uint16 port, uint8 value);
extern "C" void IoSpaceWrite32(uint16 port, uint32 value);
extern "C" uint8 IoSpaceRead8(uint16 port);
extern "C" uint32 IoSpaceRead32(uint16 port);

//////////////////////////////////////////////////////////////////////////////
//
#pragma warning(disable: 4704)

/////////////////////////////////////////////////////////////////////// Video.
//
static uint16 __far * pwScreen = (uint16 __far *)_MK_FP(0xb800, 0);
static uint16 nCursor = 0;
static uint16 s_wAttr = 0x1f00;
static BOOL fDebuggerInitialized = FALSE;

void Cls()
{
    for (uint16 n = 0; n < 4000; n++)
    {
        pwScreen[n] = s_wAttr | ' ';
    }

    nCursor = 0;
}

void VPutChar(char cOut)
{
    if (cOut == '\r')
    {
        nCursor -= nCursor % 80;
    }
    else if (cOut == '\n')
    {
        do {
            pwScreen[nCursor++] = s_wAttr | ' ';
        } while ((nCursor % 80) != 0);
    }
    else
    {
        pwScreen[nCursor++] = (uint16)(s_wAttr | cOut);
    }

    while (nCursor >= 4000)
    {
        for (uint16 n = 0; n < 4000 - 80; n++)
        {
            pwScreen[n] = pwScreen[n + 80];
        }
        for (; n < 4000; n++)
        {
            pwScreen[n] = s_wAttr | ' ';
        }
        nCursor -= 80;
    }

    IoSpaceWrite8(0x3d4, 0xe);
    IoSpaceWrite8(0x3d5, (uint8)(nCursor >> 8));
    IoSpaceWrite8(0x3d4, 0xf);
    IoSpaceWrite8(0x3d5, (uint8)(nCursor & 0xff));
}

void PutChar(char cOut)
{
    VPutChar(cOut);

    // If the debugger is not initialized, don't attempt
    // to output over the connection
    if (!fDebuggerInitialized) {
        return;
    }

    static CHAR szBuffer[128];
    static int nBuffer = 0;

    szBuffer[nBuffer++] = cOut;
    if (cOut == '\n' || nBuffer >= sizeof(szBuffer) - 1) {
        BdPrintString(szBuffer, nBuffer);
        nBuffer = 0;
    }
}

void VideoInit()
{
    __asm {
        mov     ax, 1202h;           // LINES_400_CONFIGURATION
        mov     bx, 0301h;           // SELECT_SCAN_LINE
        int     10h;

        mov     ax, 3h;              // SET_80X25_16_COLOR_MODE
        mov     bx, 0h;              // PAGE0
        int     10h;

        mov     ax, 1112h;           // LOAD_8X8_CHARACTER_SET
        mov     bx, 0h;
        int     10h;

        mov     ax, 1003h;           // Disable BLINK mode, enable background intensity.
        mov     bx, 0h;
        int     10h;

        mov     ax, 0200h;           // Set Cursor position to 0, 0
        mov     bx, 0h;
        mov     dx, 0h;
        int     10h;
    }
    Cls();
}

////////////////////////////////////////////////////////////////////// Memory.
//
void strcpy(LPCHAR dst, LPCHAR src)
{
    while (*src) {
        *dst++ = *src++;
    }
    *dst = '\0';
}

void memzero(LPVOID vp, uint32 cb)
{
    uint8 __far * bp = (uint8 __far *)vp;
    while (cb-- > 0) {
        *bp++ = 0;
    }
}

void memset(LPVOID vp, uint8 value, uint32 cb)
{
    uint8 __far * bp = (uint8 __far *)vp;
    while (cb-- > 0) {
        *bp++ = value;
    }
}

void memcopy(LPVOID dvp, LPVOID svp, int cb)
{
    uint8 __far * dp = (uint8 __far *)dvp;
    uint8 __far * sp = (uint8 __far *)svp;
    while (cb-- > 0) {
        *dp++ = *sp++;
    }
}

uint8 Sum(uint8 __far * pbData, int cbData)
{
    uint8 sum = 0;

    while (cbData-- > 0) {
        sum = (UINT8) (sum + *pbData++);
    }
    return sum;
}

int Checksum(uint8 __far * pbData, int cbData)
{
    return (Sum(pbData, cbData) == 0);
}

uint32 PointerToUint32(LPVOID vp)
{
    return (((uint32)_FP_SEG(vp)) << 4) + ((uint32)_FP_OFF(vp));
}

// warning:  alloc() behaves improperly if you
// request a segment of size 0xFFFF
static uint16 npHeapTop = 0x1800;
static const uint16 npHeapMax = 0x6000;

LPVOID alloc(uint16 cbSize, uint16 cbPad)
{
    cbPad = (cbPad != 0) ? (cbPad + 0xf) >> 4 : 0x10;
    npHeapTop = (npHeapTop + (cbPad - 1)) & ~(cbPad - 1);

    uint16 pbData = npHeapTop;

    cbSize = (cbSize != 0) ? (cbSize + 0xf) >> 4 : 1;
    npHeapTop += cbSize;
    if (npHeapTop > npHeapMax)
    {
        npHeapTop = pbData;
        return NULL;
    }

    LPVOID pvData = _MK_FP(pbData, 0);

    memzero(pvData, cbSize << 4);

    return pvData;
}

LPVOID allocpages(uint16 cPages)
{
    return alloc(Struct_Microsoft_Singularity_BootInfo_PAGE_SIZE * cPages,
                 Struct_Microsoft_Singularity_BootInfo_PAGE_SIZE);
}

void __far * operator new(uint cbSize)
{
    return alloc((uint16)cbSize, (uint16)0x10);
}

/////////////////////////////////////////////////////////////////////// Debug.
//
void dump(uint8 __far * pbData, uint cbData)
{
    for (uint n = 0; n < cbData; n += 16)
    {
        printf("    %08lx ", PointerToUint32(pbData) + n);

        for (uint s = n; s < n + 16; s++)
        {
            if (s % 4 == 0)
            {
                printf(" ");
            }
            if (s < cbData)
            {
                printf("%02x", pbData[s]);
            }
            else
            {
                printf("  ");
            }
        }
        printf(" ");
        for (s = n; s < n + 16; s++)
        {
            if (s % 4 == 0)
            {
                printf(" ");
            }
            if (s < cbData)
            {
                if (pbData[s] >= ' ' && pbData[s] < 127)
                {
                    printf("%c", pbData[s]);
                }
                else
                {
                    printf(".");
                }
            }
            else
            {
                printf(" ");
            }
        }
        printf("\n");
    }
}

const char * ToString(LPCHAR pszIn)
{
    char szBuffer[128];
    char *psz = szBuffer;

    while (*psz) {
        *psz++ = *pszIn++;
    }
    return szBuffer;
}

///////////////////////////////////////////////////////////////////////// APM.
//
void ApmPowerOff()
{
    uint16 version = 0;
    uint16 signature = 0;
    uint16 flags = 0;
    uint16 connection = 0;

    uint8  error = 0;
    uint16 which = 0;
    uint16 good = 0;

    __asm {
        mov ax, 0x5300;
        mov which, ax;
        mov bx, 0;
        int 0x15;   // APM Check.
        jc ifailed;
        mov version, ax;
        mov signature, bx;
        mov flags, cx;
        mov good, 1;
        jmp idone;
ifailed:
        mov error, ah;
        mov good, 0;
idone:
    }

    if (!good) {
        goto exit;
    }

    printf("  APM: %c%c %x.%02x, fl=%04x\n",
        signature >> 8, signature & 0xff,
        version >> 8, version & 0xff,
        flags);

    __asm {
        mov ax, 0x5301;
        mov which, ax;
        mov bx, 0;
        int 0x15;   // Real Mode Connect.
        jc failed;

        mov ax, 0x530e;
        mov which, ax;
        mov bx, 0;
        mov cx, 0x0102;
        int 0x15;   // Set APM Driver (i.e. our code) Version.
        jc failed;
        mov connection, ax;
    }

    printf("  APM: Connect %x.%02x\n",
        connection >> 8, connection & 0xff);

    __asm {
        mov ax, 0x530d;
        mov which, ax;
        mov bx, 1;
        mov cx, 1;
        int 0x15;   // Enable Power Management
        jc failed;

        mov ax, 0x530f;
        mov which, ax;
        mov bx, 1;
        mov cx, 1;
        int 0x15;   // Engage Power Management
        jc failed;

        mov ax, 0x5307;
        mov which, ax;
        mov bx, 1;
        mov cx, 3;
        int 0x15;   // Power Off
        jc failed;

        mov good, 1;
        jmp done;

failed:
        mov error, ah;
        mov good, 0;

done:
    }

exit:
    if (!good || signature != 'PM') {
        printf("APM Call (%04x) failed: %02x\n", which, error);
        return;
    }

    printf("  APM: Power Off\n");
    BootHalt();
}

///////////////////////////////////////////////////////////////////// Globals.
//

static Struct_Microsoft_Singularity_BootInfo __far *g_bi;
static Struct_Microsoft_Singularity_MpBootInfo __far *g_mbi;
static uint16 __far *g_CmdLine;

//////////////////////////////////////////////////////////// PCI Mechanism #1.
//

UINT PciReadConfig(UINT8 nBus, UINT8 nDev, UINT8 nFun, void * pvData, UINT cbData)
{
    PCI_CONFIG_BITS sel;

    sel.bits.enable = 1;
    sel.bits.bus = nBus;
    sel.bits.device = nDev;
    sel.bits.function = nFun;

    for (UINT cbDone = 0; cbDone < cbData; cbDone += sizeof(UINT32)) {
        sel.bits.offset = cbDone;
        IoSpaceWrite32(PCI_ADDR_PORT, sel.value);
        *((UINT32*&)pvData)++ = IoSpaceRead32(PCI_DATA_PORT);
    }
    return cbDone;
}

UINT32 PciGetSize(UINT8 nBus, UINT8 nDev, UINT8 nFun, UINT8 nIgnore, UINT32 nOffset)
{
    PCI_CONFIG_BITS sel;
    UINT32 saved, size;

    sel.bits.enable   = 1;
    sel.bits.bus      = nBus;
    sel.bits.device   = nDev;
    sel.bits.function = nFun;
    sel.bits.offset   = nOffset;

    // Save existing address
    IoSpaceWrite32(PCI_ADDR_PORT, sel.value);
    saved = IoSpaceRead32(PCI_DATA_PORT);

    // Write size
    IoSpaceWrite32(PCI_ADDR_PORT, sel.value);
    IoSpaceWrite32(PCI_DATA_PORT, 0xffffffff);

    // Read size (masking off reserved lower bits)
    IoSpaceWrite32(PCI_ADDR_PORT, sel.value);
    size = IoSpaceRead32(PCI_DATA_PORT) & ~((1 << nIgnore) - 1);
    size = ~size + 1;

    // Restore saved address
    IoSpaceWrite32(PCI_ADDR_PORT, sel.value);
    IoSpaceWrite32(PCI_DATA_PORT, saved);

    return size;
}

UINT32 PciGetBarSize(UINT8 nBus, UINT8 nDev, UINT8 nFun, UINT32 nOffset)
{
    return PciGetSize(nBus, nDev, nFun, 4, nOffset);
}

UINT32 PciGetRomSize(UINT8 nBus, UINT8 nDev, UINT8 nFun, UINT32 nOffset)
{
    return PciGetSize(nBus, nDev, nFun, 10, nOffset);
}

#if DEBUG_PCI
#define pciprintf(...) printf(__VA_ARGS__)
#else
#define pciprintf(...)
#endif

struct PCI_MEMORY_RANGE32 {
    // We could store cacheability state
    // here, e.g. ROM ranges can be cached.
    UINT32 base;
    UINT32 length;
};

UINT16 ScanPci(const Struct_Microsoft_Singularity_BootInfo _far *bi,
               struct PCI_MEMORY_RANGE32 _far                   *lpPciMemRanges,
               UINT16                                            nMaxPciMemRangesCount)
{
    UINT   nFound    = 0;
    UINT16 nMemCount = 0;

    ///////////////////////////////////////////////// Check for Compatibility.
    //
    pciprintf("Searching for PCI devices [%d busses].\n", bi->PciBiosCX + 1);

    if ((bi->PciBiosAX >> 8) != 0 || (bi->PciBiosEDX != 0x20494350)) {
        pciprintf("Hardware does not support PCI V2.x.\n");
        pciprintf("PCI V2.x: AX:%04x, BX:%04x, CX:%04x, EDX:%08x\n",
                  bi->PciBiosAX, bi->PciBiosBX, bi->PciBiosCX, bi->PciBiosEDX);
        return 0;
    }
    if (!(bi->PciBiosAX & 0x01)) {
        pciprintf("Hardware does not support multiple PCI buses.\n");
        pciprintf("PCI V2.x: AX:%04x, BX:%04x, CX:%04x, EDX:%08x\n",
                  bi->PciBiosAX, bi->PciBiosBX, bi->PciBiosCX, bi->PciBiosEDX);
        return 0;
    }

    for (UINT8 nBus = 0; nBus <= bi->PciBiosCX; nBus++) {
        for (UINT8 nDev = 0; nDev < PCI_MAX_DEVICES; nDev++) {
            BOOL bIsMultiFunction;
            PCI_COMMON_CONFIG config;

            config.VendorID = PCI_INVALID_VENDORID;
            PciReadConfig(nBus, nDev, 0, &config, PCI_FIXED_HDR_LENGTH);
            bIsMultiFunction = config.HeaderType & PCI_MULTIFUNCTION;

            for (UINT8 nFun = 0; nFun < PCI_MAX_FUNCTION; nFun++) {
                if (nFun > 0 && !bIsMultiFunction) {
                    break;
                }

                if (nMemCount == nMaxPciMemRangesCount) {
                    printf("PCI address ranges saturated storage.");
                    return nMaxPciMemRangesCount;
                }

                // Read configuration header.
                //
                config.VendorID = PCI_INVALID_VENDORID;
                PciReadConfig(nBus, nDev, nFun, &config, sizeof(config));

                if (config.VendorID == PCI_INVALID_VENDORID) {
                    continue;
                }

                pciprintf("%2d.%2d.%1d: class %02x-%02x device %04x-%04x-%04x-%04x-%02x %02x il=%02x ip=%02x\n",
                          nBus, nDev, nFun,
                          config.BaseClass,
                          config.SubClass,
                          config.VendorID,
                          config.DeviceID,
                          config.type0.SubVendorID,
                          config.type0.SubSystemID,
                          config.RevisionID,
                          config.HeaderType,
                          config.type0.InterruptLine,
                          config.type0.InterruptPin);

                switch (config.HeaderType & ~PCI_MULTIFUNCTION) {
                  case PCI_DEVICE_TYPE:
                    if (config.type0.BaseAddresses[0] ||
                        config.type0.BaseAddresses[1] ||
                        config.type0.BaseAddresses[2] ||
                        config.type0.BaseAddresses[3] ||
                        config.type0.BaseAddresses[4] ||
                        config.type0.BaseAddresses[5] ||
                        config.type0.ROMBaseAddress) {
                        pciprintf("         A0=%08lx A1=%08lx A2=%08lx A3=%08lx A4=%08lx "
                                  "A5=%08lx RM=%08lx\n",
                                  config.type0.BaseAddresses[0],
                                  config.type0.BaseAddresses[1],
                                  config.type0.BaseAddresses[2],
                                  config.type0.BaseAddresses[3],
                                  config.type0.BaseAddresses[4],
                                  config.type0.BaseAddresses[5],
                                  config.type0.ROMBaseAddress);
                        for (int i = 0; i < PCI_TYPE0_ADDRESSES; i++) {
                            if (config.type0.BaseAddresses[i] & PCI_BAR_TYPE_IO_SPACE) {
                                continue;
                            }
                            else if (config.type0.BaseAddresses[i] & PCI_BAR_MEMORY_TYPE_64BIT) {
                                i++;
                                continue;
                            }
                            else if ((config.type0.BaseAddresses[i] & PCI_BAR_ADDRESS_MASK) == 0) {
                                continue;
                            }
                            lpPciMemRanges[nMemCount].base   =
                                config.type0.BaseAddresses[i] & PCI_BAR_ADDRESS_MASK;
                            lpPciMemRanges[nMemCount].length =
                                PciGetBarSize(nBus, nDev, nFun, 0x10 + i * 4);
                            nMemCount++;
                        }
                        if (config.type0.ROMBaseAddress & PCI_ROMADDRESS_MASK) {
                            lpPciMemRanges[nMemCount].base   =
                                config.type0.ROMBaseAddress & PCI_ROMADDRESS_MASK;
                            lpPciMemRanges[nMemCount].length =
                                PciGetRomSize(nBus, nDev, nFun, 0x30);
                            nMemCount++;
                        }
                    }
                    break;
                  case PCI_BRIDGE_TYPE:
                    pciprintf("         BUS=%02x/%02x/%02x IO=%02x/%02x "
                              "A0=%08lx A1=%08lx RM=%08x\n",
                              config.type1.PrimaryBus,
                              config.type1.SecondaryBus,
                              config.type1.SubordinateBus,
                              config.type1.IOBase,
                              config.type1.IOLimit,
                              config.type1.BaseAddresses[0],
                              config.type1.BaseAddresses[1],
                              config.type1.ROMBaseAddress);
                    for (int i = 0; i < PCI_TYPE1_ADDRESSES; i++) {
                        if (config.type0.BaseAddresses[i] & PCI_BAR_TYPE_IO_SPACE) {
                            continue;
                        }
                        else if (config.type0.BaseAddresses[i] & PCI_BAR_MEMORY_TYPE_64BIT) {
                            i++;
                            continue;
                        }
                        else if ((config.type0.BaseAddresses[i] & PCI_BAR_ADDRESS_MASK) == 0) {
                            continue;
                        }
                        lpPciMemRanges[nMemCount].base   = config.type0.BaseAddresses[i];
                        lpPciMemRanges[nMemCount].length = PciGetBarSize(nBus, nDev, nFun,
                                                                          0x10 + i * 4);
                        nMemCount++;
                    }
                    if (config.type0.ROMBaseAddress & PCI_ROMADDRESS_MASK) {
                        lpPciMemRanges[nMemCount].base   = config.type0.ROMBaseAddress;
                        lpPciMemRanges[nMemCount].length = PciGetRomSize(nBus, nDev, nFun, 0x38);
                        nMemCount++;
                    }
                    break;
                }
                nFound++;
            }
        }
    }
    pciprintf("\n");
    return nMemCount;
}

/////////////////////////////////////////////// BootPhase1 - main entry point.
//
extern "C" int BootPhase1(PXE __far *pxe, PXENV __far * pxenv, uint32 diskid)
{
    uint16 port = 0;                // debug port
    BootDevice __far *bootDevice;   // base class ptr

    // allocate 32KB for the ini file:
    uint8 __far * IniFileBuffer = (uint8 __far *) alloc(0x7FFF, 0);

    // constant, hard-coded filename
    LPCHAR initfile = "/Singularity/Singboot.ini";

    VideoInit();
    VPutChar('1');

    //
    // Initialize the Debugger
    //
    if (BdInitDebugger(COM2_PORT)) {
        VPutChar('a');
        port = COM2_PORT;
        fDebuggerInitialized = TRUE;
    }
    else {
        VPutChar('A');
        if (BdInitDebugger(COM1_PORT)) {
            VPutChar('b');
            port = COM1_PORT;
            fDebuggerInitialized = TRUE;
        }
        else {
            VPutChar('B');
            if (BdInitDebugger(COM2_PORT)) {
                fDebuggerInitialized = TRUE;
                port = COM2_PORT;
            }
        }
    }
    VPutChar('2');

    //
    // Get the BIOS info
    //
    Struct_Microsoft_Singularity_BootInfo __far *bi =
        (Struct_Microsoft_Singularity_BootInfo __far *)
        alloc(sizeof(Struct_Microsoft_Singularity_BootInfo), 0x10);
    g_bi = bi;
    VPutChar('3');

    BootGetBiosInfo(bi);
    bi->Info16 = (uint32)bi;
    VPutChar('4');

    const UINT16 nMaxPciRanges = 64;
    struct PCI_MEMORY_RANGE32 __far *lpPciRanges = (struct PCI_MEMORY_RANGE32 __far*)
        alloc(nMaxPciRanges * sizeof(struct PCI_MEMORY_RANGE32), 0);
    UINT16 nPciMemoryRanges = ScanPci(bi, lpPciRanges, nMaxPciRanges);

    //
    // Try to find a PCI 1394 connection for the debugger.
    //
    for (UINT8 nBus = 0; nBus <= bi->PciBiosCX; nBus++) {
        for (UINT8 nDev = 0; nDev < PCI_MAX_DEVICES; nDev++) {
            BOOL bIsMultiFunction;
            PCI_COMMON_CONFIG config;

            config.VendorID = PCI_INVALID_VENDORID;
            PciReadConfig(nBus, nDev, 0, &config, sizeof(config));
            bIsMultiFunction = config.HeaderType & PCI_MULTIFUNCTION;

            for (UINT8 nFun = 0; nFun < PCI_MAX_FUNCTION; nFun++) {
                if (nFun > 0 && !bIsMultiFunction) {
                    break;
                }

                if (config.BaseClass == 0x0c && config.SubClass == 0x00 &&
                    bi->Ohci1394Base.lo == 0) {
                    // Found firewire.

                    printf("%2d.%2d.%1d: 1394 device %04x-%04x-%04x-%04x-%02x\n",
                           nBus, nDev, nFun,
                           config.VendorID,
                           config.DeviceID,
                           config.type0.SubVendorID,
                           config.type0.SubSystemID,
                           config.RevisionID);
                    printf("          %08lx %08lx %08lx %08lx %08lx %08lx\n",
                           config.type0.BaseAddresses[0],
                           config.type0.BaseAddresses[1],
                           config.type0.BaseAddresses[2],
                           config.type0.BaseAddresses[3],
                           config.type0.BaseAddresses[4],
                           config.type0.BaseAddresses[5]);

                    bi->Ohci1394Base.lo = config.type0.BaseAddresses[0] & ~0xflu;
                    bi->Ohci1394BufferAddr32.lo = PointerToUint32(allocpages(3));
                    bi->Ohci1394BufferSize32
                        = 3 * Struct_Microsoft_Singularity_BootInfo_PAGE_SIZE;
                    break;
                }
            }
        }
    }

    // figure out the boot device
    if (pxe->Signature[0] == '!' &&
        pxe->Signature[1] == 'P' &&
        pxe->Signature[2] == 'X' &&
        pxe->Signature[3] == 'E') {
        bootDevice = new __far PxeDevice(pxe, pxenv);
    }
    else if (pxenv->Signature[0] == 'P' &&
             pxenv->Signature[1] == 'X' &&
             pxenv->Signature[2] == 'E' &&
             pxenv->Signature[3] == 'N' &&
             pxenv->Signature[4] == 'V' &&
             pxenv->Signature[5] == '+') {
        bootDevice = new __far PxeDevice(pxe, pxenv);
    }
    else if (PointerToUint32(pxe) == diskid &&
             PointerToUint32(pxenv) == 0x4803) {
        bootDevice = new __far FatDevice((uint8)diskid, (uint8)32);
    }
    else if (PointerToUint32(pxe) == diskid &&
             PointerToUint32(pxenv) == 0x4806) {
        bootDevice = new __far FatDevice((uint8)diskid, (uint8)16);
    }
    else if (PointerToUint32(pxe) == diskid &&
             PointerToUint32(pxenv) == 0x4344) {
        bootDevice = new __far JolietDevice((uint8)diskid, (uint8)0);
    }
    else if (PointerToUint32(pxe) == diskid &&
             PointerToUint32(pxenv) == 0x5544) {
        bootDevice = new __far UsbDevice((uint8)diskid, (uint8)0);
    }
    else {
        printf("Error:  Invalid Boot Medium:  diskid=%lx, pxe=%lx, pxenv=%lx",
               diskid,
               PointerToUint32(pxe),
               PointerToUint32(pxenv));
        BootHalt();
    }

    VPutChar('5');
    BdPrintString("-------------------------------------------------------------------------------\n", 80);
    VPutChar('6');

    //
    // print welcome message:
    //

    printf("16-bit Singularity Boot Loader [%s %s] (bi=%d) [com2]\n",
           __DATE__, __TIME__, sizeof(Struct_Microsoft_Singularity_BootInfo));
    VPutChar('7');
    VPutChar('.');

    //
    // partial verify of the bios data
    //
    if (sizeof(Struct_Microsoft_Singularity_BootInfo) != bi->RecSize) {
        printf("sizeof(Struct_Microsoft_Singularity_BootInfo)=%d, bi->RecSize=%ld\n",
               sizeof(Struct_Microsoft_Singularity_BootInfo),
               bi->RecSize);
        BootHalt();
    }

    //
    // Initialize the boot device
    //
    if (bootDevice->OpenDevice()==-1) {
        BootHalt();
    }

    // read these globals from the boot device, to use in adjusting the
    // debug port
    g_CmdLine = bootDevice->CmdLine;
    bi->CmdLine32.lo = PointerToUint32(bootDevice->CmdLine);

    printf("\n");

    //
    // Adjust Debug Port.
    //
    bi->DebugBasePort = port;

    for (uint16 __far * pwz = bootDevice->CmdLine; *pwz != '\0'; pwz++) {
        if ((pwz[0] == 'd' || pwz[0] == 'D') &&
            (pwz[1] == 'b' || pwz[1] == 'B') &&
            (pwz[2] == 'g' || pwz[2] == 'G') &&
            pwz[3] == '=') {
            *pwz++ = ' ';   // Remove 'd'
            *pwz++ = ' ';   // Remove 'b'
            *pwz++ = ' ';   // Remove 'g'
            *pwz++ = ' ';   // Remove '='

            bi->DebugBasePort = 0;
            while (*pwz != '\0' && *pwz != ' ') {
                if (*pwz >= 'a' && *pwz <= 'f') {
                    bi->DebugBasePort = bi->DebugBasePort * 0x10 + (*pwz - 'a') + 10;
                    *pwz++ = ' ';
                }
                else if (*pwz >= 'A' && *pwz <= 'F') {
                    bi->DebugBasePort = bi->DebugBasePort * 0x10 + (*pwz - 'A') + 10;
                    *pwz++ = ' ';
                }
                else if (*pwz >= '0' && *pwz <= '9') {
                    bi->DebugBasePort = bi->DebugBasePort * 0x10 + (*pwz - '0');
                    *pwz++ = ' ';
                }
                else {
                    break;
                }
            }
        }
    }

    if (bi->DebugBasePort < 0x100) {
        printf("Using default debug port until 1394 starts [%04x com1=%04x com2=%04x].\n",
               port, COM1_PORT, COM2_PORT);
    }
    else if (bi->DebugBasePort != port) {
        printf("Changing to debug port %x.\n", bi->DebugBasePort);
        BdInitDebugger(bi->DebugBasePort);
        BdPrintString("-------------------------------------------------------------------------------\n", 80);

        printf("16-bit Singularity Boot Loader [%s %s] (bi=%d) [dbg=%x]\n",
               __DATE__, __TIME__,
               sizeof(Struct_Microsoft_Singularity_BootInfo),
               bi->DebugBasePort);
    }
    else {
        printf("Default debug port [%04x com1=%04x com2=%04x].\n",
               port, COM1_PORT, COM2_PORT);
        bi->DebugBasePort = port;
    }

    //
    // Set up Memory Map
    //
    Struct_Microsoft_Singularity_SMAPINFO __far *sm =
        (Struct_Microsoft_Singularity_SMAPINFO __far *)
        alloc(sizeof(Struct_Microsoft_Singularity_SMAPINFO) * 128, 0x10);
    bi->SmapData32.lo = PointerToUint32(sm);

    // Read the system memory map.
    for (uint32 index = 0; bi->SmapCount < 128;) {
        uint32 next = index;

        int ax = BootGetSMAP(&sm[bi->SmapCount], &next);
        if (ax != 0x4150) {
            break;
        }
        bi->SmapCount++;

        if (next == 0) {
            break;
        }
        index = next;
    }

    //
    // Drop SMAP entries above supported limit
    //
    for (uint i = 0; i < bi->SmapCount; i++) {
        if (sm[i].type != Struct_Microsoft_Singularity_SMAPINFO_AddressTypeFree) {
            continue;
        }

        uint32 startKB = (sm[i].addr.lo >> 10) + (sm[i].addr.hi << 22);
        uint32 sizeKB  = (sm[i].size.lo >> 10) + (sm[i].size.hi << 22);

        if ((sm[i].addr.hi != 0ul) ||
            sm[i].addr.lo >= Struct_Microsoft_Singularity_BootInfo_MAX_VIRTUAL_ADDR) {
            sm[i] = sm[--bi->SmapCount];
        }
        else if (startKB + sizeKB >= Struct_Microsoft_Singularity_BootInfo_MAX_VIRTUAL_ADDR / 1024ul) {
            sm[i].size.hi = 0;
            sm[i].size.lo = Struct_Microsoft_Singularity_BootInfo_MAX_VIRTUAL_ADDR - startKB * 1024;
        }
    }

    // Sort the system memory map.
  sortagain:
    for (i = 0; i < bi->SmapCount - 1; i++) {
        if ((sm[i].addr.hi > sm[i + 1].addr.hi) ||
            (sm[i].addr.hi == sm[i + 1].addr.hi &&
             sm[i].addr.lo > sm[i + 1].addr.lo)) {

            Struct_Microsoft_Singularity_SMAPINFO s = sm[i];
            sm[i] = sm[i+1];
            sm[i+1] = s;

            goto sortagain;
        }
    }

    //
    // Make a reasonable guess at the top of physical memory.
    //
    uint32 memoryKB = 0;
    for (i = 0; i < bi->SmapCount; i++) {
        uint32 limitKB = (sm[i].addr.lo >> 10) + (sm[i].size.lo >> 10);
        switch (sm[i].type) {
            case Struct_Microsoft_Singularity_SMAPINFO_AddressTypeFree:
            // case Struct_Microsoft_Singularity_SMAPINFO_AddressTypeACPI:
            // case Struct_Microsoft_Singularity_SMAPINFO_AddressTypeNVS:
                if (limitKB > memoryKB) {
                    memoryKB = limitKB;
                }
                break;
            default:
                break;
        }
    }
    printf("Physical memory detected: %8luKB\n", memoryKB);

    //
    // Save the EBDA.
    //
    uint16 __far * pEbdaSegment = (uint16 __far *)_MK_FP(0x40, 0x0e);
    uint8 __far * pEbdaRegion = (uint8 __far *)_MK_FP(*pEbdaSegment, 0);

    bi->Ebda32 = PointerToUint32(pEbdaRegion);


    //
    // Scan for PNP.
    //
    LPPNP_ROOT pPnp = NULL;
    LPSMBIOS_ROOT pSmbios = NULL;
    LPDMIBIOS_ROOT pDmi = NULL;

    for (uint segment = 0xf000; segment < 0xffff; segment++) {

        LPPNP_ROOT ppr = (LPPNP_ROOT)_MK_FP(segment, 0);

        if (pPnp == NULL &&
            ppr->Signature[0] == '$' &&
            ppr->Signature[1] == 'P' &&
            ppr->Signature[2] == 'n' &&
            ppr->Signature[3] == 'P' &&
            Checksum((uint8 __far *)ppr, ppr->Length)) {

            pPnp = ppr;
        }

        LPSMBIOS_ROOT psr = (LPSMBIOS_ROOT)ppr;
        if (pSmbios == NULL &&
            psr->Signature[0] == '_' &&
            psr->Signature[1] == 'S' &&
            psr->Signature[2] == 'M' &&
            psr->Signature[3] == '_' &&
            psr->Signature2[0] == '_' &&
            psr->Signature2[1] == 'D' &&
            psr->Signature2[2] == 'M' &&
            psr->Signature2[3] == 'I' &&
            psr->Signature2[4] == '_' &&
            Checksum((uint8 __far *)psr, psr->Length)) {

            pSmbios = psr;
            pDmi = (LPDMIBIOS_ROOT)&psr->Signature2[0];
        }

        LPDMIBIOS_ROOT pdr = (LPDMIBIOS_ROOT)ppr;
        if (pDmi == NULL &&
            pdr->Signature2[0] == '_' &&
            pdr->Signature2[1] == 'D' &&
            pdr->Signature2[2] == 'M' &&
            pdr->Signature2[3] == 'I' &&
            pdr->Signature2[4] == '_' &&
            Checksum((uint8 __far *)pdr, sizeof(DMIBIOS_ROOT))) {

            pDmi = pdr;
        }
    }

    if (pPnp != NULL) {
        printf("Found PnP at %lp\n", pPnp);
        printf("    Rev=%02x, Len=%02x/%02x/%02x, Ctl=%04x Evt=%08lx\n",
               pPnp->Revision,
               pPnp->Length,
               sizeof(*pPnp),
               Sum((uint8 __far *)pPnp, pPnp->Length),
               pPnp->ControlField,
               pPnp->EventFlagAddress);
        printf("    RealMode: entry=%lp data=%04x [oem=%08lx]\n",
               pPnp->RealModeEntry,
               pPnp->RealModeDataSegment,
               pPnp->OemDeviceId);

        PNP_FN pfPnp = pPnp->RealModeEntry;
        int err;
        uint8 cNodes = 0;
        uint16 cbNode = 0;

        err = pfPnp(0, (uint8 __far *)&cNodes, (uint16 __far *)&cbNode, pPnp->RealModeDataSegment);
        if (err != 0) {
            printf("Get Number of System Device Nodes failed: %d\n", err);
        }

        LPPNP_NODE pNode = (LPPNP_NODE)alloc(cbNode, 4);

        uint16 cbNodes = 0;
        for (uint8 n = 0; n < 0xff;) {
            err = pfPnp(1, (uint8 __far *)&n, pNode, 1, pPnp->RealModeDataSegment);
            if (err != 0) {
                break;
            }
            cbNodes += pNode->Size;
        }

        uint8 __far * pNodes = (uint8 __far *)alloc(cbNodes, 4);
        bi->PnpNodesAddr32.lo = PointerToUint32(pNodes);
        bi->PnpNodesSize32 = cbNodes;

        for (n = 0; n < 0xff;) {
            pNode = (LPPNP_NODE)pNodes;
            err = pfPnp(1, (uint8 __far *)&n, pNode, 1, pPnp->RealModeDataSegment);
            if (err != 0) {
                printf("Get Number of System Device Node failed: %d\n", err);
                n = 0xff;
                continue;
            }

            pNodes += pNode->Size;
        }

        pNode = (LPPNP_NODE)pNodes;
        pNode->Size = 0;

        printf("\n");

        PNP_ISACONFIG isaConfig;
        isaConfig.Revision = 1;

        err = pfPnp(0x40, (LPPNP_ISACONFIG)&isaConfig, pPnp->RealModeDataSegment);
        if (err != 0) {
            printf("Get ISA Config failed: %d\n", err);
        }
        else {
            bi->IsaCsns = isaConfig.TotalCSNs;
            bi->IsaReadPort = isaConfig.IsaReadDataPort;

            printf("ISA PnP Revision: %d, TotalCSNs: %d, IsaReadDatPort: %04x\n",
                   isaConfig.Revision,
                   isaConfig.TotalCSNs,
                   isaConfig.IsaReadDataPort);
        }
    }

    if (pSmbios != NULL) {
        printf("Found SMBIOS at %lp\n", pSmbios);

        printf("    Version=%02x.%02x Len=%02x/%02x/%02x Max=%04x EPR=%02x\n",
               pSmbios->MajorVersion,
               pSmbios->MinorVersion,
               pSmbios->Length,
               sizeof(*pSmbios),
               Sum((uint8 __far *)pSmbios, pSmbios->Length),
               pSmbios->MaximumStructureSize,
               pSmbios->EntryPointRevision);
        printf("\n");
        bi->SmbiosRoot32.lo = PointerToUint32(pSmbios);
    }
    if (pDmi != NULL) {
        printf("Found DMI at %lp\n", pDmi);

        printf("    Rev=%02x Len=%04x Num=%04x Addr=%08lx\n",
               pDmi->Revision,
               pDmi->StructureTableLength,
               pDmi->NumberStructures,
               pDmi->StructureTableAddress);
        printf("\n");
        bi->DmiRoot32.lo = PointerToUint32(pDmi);
    }

    //
    // Load dump images
    //
    bi->DumpSize32 = 0;

    // Since we don't want to allocate much memory, we'll
    // recycle these 2 pointers to file structs for all filesystem ops
    FileData directory;
    FilePtr pDirectory = (FilePtr) &directory;
    FileData file;
    FilePtr pFile = (FilePtr) &file;

    pFile->Size       = 0x7fff;
    pFile->FirstBlock = 0;

    // Zero the IniFileBuffer
    memzero((LPVOID)IniFileBuffer, 0x8000);

    // Find the .ini file and get file metadata for loaders that cache it
    if (bootDevice->GetFileProperties(initfile, pFile, pDirectory) == -1) {
        // the device-specific function already printed something, just halt
        BootHalt();
    }

    if (pFile->Size > 0x8000) {
        printf(".ini file too big for SINGLDR.\n");
        BootHalt();
    }

    // Load the .ini file
    if (bootDevice->ReadFileLow(initfile, pFile, IniFileBuffer) == -1) {
        // the device-specific function already printed something, just halt
        BootHalt();
    }

    IniFile iniFile(IniFileBuffer, pFile->Size);

    uint16 nMaxImages = iniFile.GetFileCount();

    Struct_Microsoft_Singularity_Io_FileImage __far* fileImage =
        (Struct_Microsoft_Singularity_Io_FileImage __far*)
        alloc((uint16)(sizeof(Struct_Microsoft_Singularity_Io_FileImage) *
                       nMaxImages), 0x10);

    iniFile.Rewind();

    for (i = 0; i < nMaxImages; i++) {
        LPUINT8 nextFname = iniFile.GetCurrentFileName();
        if (bootDevice->GetFileProperties((LPCHAR)nextFname,
                                          pFile, pDirectory) == -1) {
            BootHalt();
        }
        fileImage[i].Size     = iniFile.GetCurrentFileSize();
        fileImage[i].Address  = pFile->FirstBlock;
        bi->DumpSize32       += fileImage[i].Size;

        iniFile.MoveNext();
    }

    // Pad size by one byte to work around
    // a bug in a popular pxeboot disk that has an off by one
    // buffer overflow check and wrongly refuses to copy last block
    // of file if provided buffer is exactly the right size.
    bi->DumpSize32 += 1;

    //
    // Find memory to load image.
    //
    uint32 dumpRegionSize = (bi->DumpSize32 + 0xffff) & 0xffff0000;
    bi->DumpAddr32.lo = 0;

    printf("System Memory Map:\n");
    // Find a suitable region of memory.
    for (i = 0; i < bi->SmapCount; i++) {
        if (sm[i].type == 1 && sm[i].size.lo > dumpRegionSize) {
            // round down to nearest 2MB boundary (to start on super page boundary).
            uint32 target = (sm[i].addr.lo + sm[i].size.lo - dumpRegionSize) & 0xffe00000;
            if (target < sm[i].addr.lo) {
                continue;
            }
            if (bi->DumpAddr32.lo < target) {
                bi->DumpAddr32.lo = target;
                dumpRegionSize = sm[i].size.lo;
            }
        }
        printf("  %08lx..%08lx %ld.%ld\n",
               sm[i].addr.lo,
               sm[i].addr.lo + sm[i].size.lo,
               sm[i].type,
               sm[i].extendedAttributes
               );
    }

    printf("\n");
    printf("Editing the SMAP to protect the dump area...\n");

    for (i = 0; i < bi->SmapCount; i++) {
        // Does this region straddle the start of the dump area?
        // if so, truncate it
        if (sm[i].type == 1 &&
            sm[i].addr.lo < bi->DumpAddr32.lo &&
            sm[i].addr.lo + sm[i].size.lo > bi->DumpAddr32.lo) {

            sm[i].size.lo = bi->DumpAddr32.lo - sm[i].addr.lo;
            printf("  %08lx..%08lx %d (truncated)\n",
                   sm[i].addr.lo,
                   sm[i].addr.lo + sm[i].size.lo,
                   sm[i].type);
        }

        if (sm[i].type == 1 &&
            sm[i].addr.lo >= bi->DumpAddr32.lo) {

            sm[i].type = 2; // Arbitrary non-free value
            printf("  %08lx..%08lx %d (marked unavailable)\n",
                   sm[i].addr.lo,
                   sm[i].addr.lo + sm[i].size.lo,
                   sm[i].type);
        }
    }

    printf("\n");
    printf("Loading image at %08lx..%08lx in %d files.\n",
           bi->DumpAddr32.lo, bi->DumpAddr32.lo + bi->DumpSize32, nMaxImages);

    //
    // Download image
    //
    bi->DumpSize32 = 0;

    iniFile.Rewind();

    uint32 bytesread; // to ensure we read right amount for each file

    printf("Loading files");
    // request each file, in order
    for (i = 0; i < nMaxImages; i++) {
        uint32 destination = bi->DumpAddr32.lo + bi->DumpSize32;
        LPUINT8 nextFname = iniFile.GetCurrentFileName();

        // get the first block and size of the file
        pFile->FirstBlock = fileImage[i].Address;
        pFile->Size       = fileImage[i].Size;

        // some media use the file name, others use the FilePtr object.  We
        // send both to the function, and leave it up to the media to use
        // what it needs.
        bytesread = bootDevice->ReadFileHigh((LPCHAR) nextFname,
                                             pFile,
                                             destination,
                                             dumpRegionSize);
        if (bytesread != fileImage[i].Size) {
            printf("\n    Read wrong # of bytes: %ld != %ld.\n",
                   bytesread, fileImage[i].Size);
            BootHalt();
        }
        else {
            printf(".");
        }

        // Change from address of first sector to physical memory address
        fileImage[i].Address  = destination;
        bi->DumpSize32       += fileImage[i].Size;
        dumpRegionSize       -= fileImage[i].Size;

        iniFile.MoveNext();
    }
    printf("\n");

    bi->FileImageTableBase32.lo  = PointerToUint32(fileImage);
    bi->FileImageTableEntries = nMaxImages;

    //
    // Close the Boot Medium:
    //
    bootDevice->CloseDevice();

    //
    // Allocate MpBootInfo structure
    //
    g_mbi = (Struct_Microsoft_Singularity_MpBootInfo __far *)
        alloc(sizeof(Struct_Microsoft_Singularity_MpBootInfo), 0x04);
    bi->MpBootInfo32.lo    = PointerToUint32(g_mbi);
    bi->MpCpuCount         = 0;
    bi->MpStartupLock32.lo = PointerToUint32(&MpStartupLock);

    //
    // Allocate pages for commonly referenced data
    //

    // 2 Pages (1 RW, 1 RO) for processor context
    LPVOID pfs = allocpages(2);

    // 2 Pages (1 RW, 1 RO) for thread
    LPVOID pgs = allocpages(2);

    uint32 pfsBase = PointerToUint32(pfs);
    uint32 pgsBase = PointerToUint32(pgs);

    //
    // Set up the GDT
    //
    bi->Cpu0.GdtPtr.addr = PointerToUint32(&bi->Cpu0.GdtNull);
    bi->Cpu0.GdtPtr.limit = (OFFSETOF(Struct_Microsoft_Singularity_CpuInfo, GdtEnd) -
                             OFFSETOF(Struct_Microsoft_Singularity_CpuInfo, GdtNull));

    bi->Cpu0.GdtRS.base0_15 = Struct_Microsoft_Singularity_BootInfo_REAL_CODE_BASE;
    bi->Cpu0.GdtRS.base16_23 = Struct_Microsoft_Singularity_BootInfo_REAL_CODE_BASE >> 16;
    bi->Cpu0.GdtRS.base24_31 = Struct_Microsoft_Singularity_BootInfo_REAL_CODE_BASE >> 24;

    bi->Cpu0.GdtRC.base0_15 = Struct_Microsoft_Singularity_BootInfo_REAL_CODE_BASE;
    bi->Cpu0.GdtRC.base16_23 = Struct_Microsoft_Singularity_BootInfo_REAL_CODE_BASE >> 16;
    bi->Cpu0.GdtRC.base24_31 = Struct_Microsoft_Singularity_BootInfo_REAL_CODE_BASE >> 24;

    bi->Cpu0.GdtPF.base0_15  = (uint16)pfsBase;
    bi->Cpu0.GdtPF.base16_23 = (uint8)(pfsBase >> 16);
    bi->Cpu0.GdtPF.base24_31 = (uint8)(pfsBase >> 24);

    bi->Cpu0.GdtPG.base0_15  = (uint16)pgsBase;
    bi->Cpu0.GdtPG.base16_23 = (uint8)(pgsBase >> 16);
    bi->Cpu0.GdtPG.base24_31 = (uint8)(pgsBase >> 24);

    bi->Cpu0.GdtRS.limit = 0xffff;
    bi->Cpu0.GdtRC.limit = 0xffff;
    bi->Cpu0.GdtPC.limit = 0xffff;
    bi->Cpu0.GdtPD.limit = 0xffff;
    bi->Cpu0.GdtUC.limit = 0xffff;
    bi->Cpu0.GdtUD.limit = 0xffff;
    bi->Cpu0.GdtPF.limit = 0x1;  // 2 Pages (1 RW, 1 RO)
    bi->Cpu0.GdtPG.limit = 0x1;  // 2 Pages (1 RW, 1 RO)

    bi->Cpu0.GdtRS.access = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                             Struct_Microsoft_Singularity_X86_GDTE_RING0 |
                             Struct_Microsoft_Singularity_X86_GDTE_USER |
                             Struct_Microsoft_Singularity_X86_GDTE_WRITEABLE |
                             Struct_Microsoft_Singularity_X86_GDTE_ACCESSED);
    bi->Cpu0.GdtRC.access = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                             Struct_Microsoft_Singularity_X86_GDTE_RING0 |
                             Struct_Microsoft_Singularity_X86_GDTE_USER |
                             Struct_Microsoft_Singularity_X86_GDTE_READABLE |
                             Struct_Microsoft_Singularity_X86_GDTE_CODE |
                             Struct_Microsoft_Singularity_X86_GDTE_ACCESSED);
    bi->Cpu0.GdtPC.access = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                             Struct_Microsoft_Singularity_X86_GDTE_RING0 |
                             Struct_Microsoft_Singularity_X86_GDTE_USER |
                             Struct_Microsoft_Singularity_X86_GDTE_READABLE |
                             Struct_Microsoft_Singularity_X86_GDTE_CODE |
                             Struct_Microsoft_Singularity_X86_GDTE_ACCESSED);
    bi->Cpu0.GdtPD.access = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                             Struct_Microsoft_Singularity_X86_GDTE_RING0 |
                             Struct_Microsoft_Singularity_X86_GDTE_USER |
                             Struct_Microsoft_Singularity_X86_GDTE_WRITEABLE |
                             Struct_Microsoft_Singularity_X86_GDTE_ACCESSED);
    bi->Cpu0.GdtUC.access = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                             Struct_Microsoft_Singularity_X86_GDTE_RING3 |
                             Struct_Microsoft_Singularity_X86_GDTE_USER |
                             Struct_Microsoft_Singularity_X86_GDTE_READABLE |
                             Struct_Microsoft_Singularity_X86_GDTE_CODE |
                             Struct_Microsoft_Singularity_X86_GDTE_ACCESSED);
    bi->Cpu0.GdtUD.access = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                             Struct_Microsoft_Singularity_X86_GDTE_RING3 |
                             Struct_Microsoft_Singularity_X86_GDTE_USER |
                             Struct_Microsoft_Singularity_X86_GDTE_WRITEABLE |
                             Struct_Microsoft_Singularity_X86_GDTE_ACCESSED);
    bi->Cpu0.GdtPF.access = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                             Struct_Microsoft_Singularity_X86_GDTE_RING3 | // for the moment, share UF and PF
                             Struct_Microsoft_Singularity_X86_GDTE_USER |
                             Struct_Microsoft_Singularity_X86_GDTE_WRITEABLE |
                             Struct_Microsoft_Singularity_X86_GDTE_ACCESSED);
    bi->Cpu0.GdtPG.access = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                             Struct_Microsoft_Singularity_X86_GDTE_RING0 |
                             Struct_Microsoft_Singularity_X86_GDTE_USER |
                             Struct_Microsoft_Singularity_X86_GDTE_WRITEABLE |
                             Struct_Microsoft_Singularity_X86_GDTE_ACCESSED);

    bi->Cpu0.GdtRS.granularity = 0;
    bi->Cpu0.GdtRC.granularity = 0;
    bi->Cpu0.GdtPC.granularity = (Struct_Microsoft_Singularity_X86_GDTE_PAGES |
                                  Struct_Microsoft_Singularity_X86_GDTE_IS32BIT |
                                  Struct_Microsoft_Singularity_X86_GDTE_LIMIT20);
    bi->Cpu0.GdtPD.granularity = (Struct_Microsoft_Singularity_X86_GDTE_PAGES |
                                  Struct_Microsoft_Singularity_X86_GDTE_IS32BIT |
                                  Struct_Microsoft_Singularity_X86_GDTE_LIMIT20);
    bi->Cpu0.GdtUC.granularity = (Struct_Microsoft_Singularity_X86_GDTE_PAGES |
                                  Struct_Microsoft_Singularity_X86_GDTE_IS32BIT |
                                  Struct_Microsoft_Singularity_X86_GDTE_LIMIT20);
    bi->Cpu0.GdtUD.granularity = (Struct_Microsoft_Singularity_X86_GDTE_PAGES |
                                  Struct_Microsoft_Singularity_X86_GDTE_IS32BIT |
                                  Struct_Microsoft_Singularity_X86_GDTE_LIMIT20);
    bi->Cpu0.GdtPF.granularity = (Struct_Microsoft_Singularity_X86_GDTE_PAGES |
                                  Struct_Microsoft_Singularity_X86_GDTE_IS32BIT);
    bi->Cpu0.GdtPG.granularity = (Struct_Microsoft_Singularity_X86_GDTE_PAGES |
                                  Struct_Microsoft_Singularity_X86_GDTE_IS32BIT);

    Struct_Microsoft_Singularity_X86_TSS __far* ptss;
    ptss = (Struct_Microsoft_Singularity_X86_TSS __far*)allocpages(1);
    ptss->ss0 = (OFFSETOF(Struct_Microsoft_Singularity_CpuInfo, GdtPD) -
                 OFFSETOF(Struct_Microsoft_Singularity_CpuInfo, GdtNull));
    ptss->esp0 = PointerToUint32(ptss) + Struct_Microsoft_Singularity_BootInfo_PAGE_SIZE-0x10;
    ptss->io_bitmap_offset = sizeof(*ptss);

    uint32 tssaddr = PointerToUint32(ptss);
    bi->Cpu0.GdtTSS.base0_15  = (uint16)((tssaddr) & 0xffff);
    bi->Cpu0.GdtTSS.base16_23 = (uint8)((tssaddr >> 16) & 0xff);
    bi->Cpu0.GdtTSS.base24_31 = (uint8)((tssaddr >> 24) & 0xff);
    bi->Cpu0.GdtTSS.limit     = sizeof(*ptss) - 1;
    bi->Cpu0.GdtTSS.access    = (Struct_Microsoft_Singularity_X86_GDTE_PRESENT |
                                 Struct_Microsoft_Singularity_X86_GDTE_RING0 |
                                 Struct_Microsoft_Singularity_X86_GDTE_Tss32Free);

#if !MAP_ZERO_PAGE
    //
    // Create a simple PAE page table for the double-fault handler.
    //

    uint64 __far * pdpe = (uint64 __far *) allocpages(1);   // Page-directory-pointer table
    uint64 __far * pde = (uint64 __far *) allocpages(4);   // Page-directory table
    uint64 __far * pte = (uint64 __far *) allocpages(1);   // Page table

    uint32 pdptBase = PointerToUint32(pdpe);
    uint32 pdtsBase = PointerToUint32(pde);
    uint32 pteBase = PointerToUint32(pte);

    // Create page-directory-pointer entries
    for (i = 0; i < 4; i++) {
        pdpe[i].lo = (pdtsBase + (((uint32)i) << 12)) |
            (Struct_Microsoft_Singularity_X86_PE_VALID);
    }

    // Create PDE entries for full 4GB of addressable RAM.
    for (i = 0; i < 2048; i++) {
        pde[i].lo = (((uint32)i) << 21) |
            (Struct_Microsoft_Singularity_X86_PE_IS2MB |
             Struct_Microsoft_Singularity_X86_PE_ACCESSED |
             Struct_Microsoft_Singularity_X86_PE_WRITEABLE);
    }

    // Create a small-page entry for the low 2MB.
    pde[0].lo = (pteBase) |
        (Struct_Microsoft_Singularity_X86_PE_ACCESSED |
         Struct_Microsoft_Singularity_X86_PE_WRITEABLE);

    // Create the page table for the low 2MB page.
    for (i = 0; i < 512; i++) {
        pte[i].lo = (((uint32)i) << 12) |
            (Struct_Microsoft_Singularity_X86_PE_ACCESSED |
             Struct_Microsoft_Singularity_X86_PE_WRITEABLE);
    }

    // Map from 16KB to 2MB.
    for (i = 4; i < 512; i++) {
        pte[i].lo |= Struct_Microsoft_Singularity_X86_PE_VALID;
    }

    // Map physical memory pages (rounding up to 2MB)
    uint32 pages = (memoryKB + 0x7ff) >> 11;
    for (i = 0; i < pages; i++) {
        pde[i].lo |= Struct_Microsoft_Singularity_X86_PE_VALID;
    }

    // Make sure all SMAP addresses below 4GB are mapped.  ACPI
    // data structures may be mapped higher than supported
    // physical memory (2GB), and we need to be able to read
    // these.
    for (i = 0; i < bi->SmapCount; i++) {
        if (sm[i].addr.hi == 0) {
            uint32 startKB = sm[i].addr.lo / 1024ul;
            uint32 sizeKB  = sm[i].size.lo / 1024ul;
            uint32 currKB  = startKB;
            while (currKB < startKB + sizeKB) {
                pde[currKB / 2048ul].lo |= Struct_Microsoft_Singularity_X86_PE_VALID;
                currKB += 2048ul;
            }
        }
    }

    // Mark the cached images as read-only.
    for (uint32 addr = bi->DumpAddr32.lo;
         addr < bi->DumpAddr32.lo + bi->DumpSize32;
         addr += 0x200000) {
        pde[addr >> 21].lo &= ~Struct_Microsoft_Singularity_X86_PE_WRITEABLE;
        printf("  %08lx..%08lx readonly\n", addr, addr + 0x1fffff);
    }

    // Map PCI memory based I/O ranges
    for (i = 0; i < nPciMemoryRanges; i++) {
        UINT32 start = lpPciRanges[i].base;
        UINT32 end   = (start + lpPciRanges[i].length);
        printf("  %08lx..%08lx write-through, cache disabled (PCI memory)\n",
               start, end);
        start >>= 21;
        end   >>= 21;
        do {
            pde[start].lo |=
                (Struct_Microsoft_Singularity_X86_PE_CACHEDISABLE |
                 Struct_Microsoft_Singularity_X86_PE_WRITETHROUGH |
                 Struct_Microsoft_Singularity_X86_PE_VALID);
            start ++;
        } while (start < end);
    }

    // Hack for I/O apics on NUMA box.  We should be extracting
    // this from ACPI / MP Resources. (0xd0000000 >> 21)
    pde[0xd000 >> 5].lo |=
        (Struct_Microsoft_Singularity_X86_PE_CACHEDISABLE |
         Struct_Microsoft_Singularity_X86_PE_WRITETHROUGH |
         Struct_Microsoft_Singularity_X86_PE_VALID);

    // The last 64MB is mapped with caching disabled to allow
    // access to the APICs.
    for (i = 2016; i < 2048; i++) {
        pde[i].lo |=
            (Struct_Microsoft_Singularity_X86_PE_CACHEDISABLE |
             Struct_Microsoft_Singularity_X86_PE_WRITETHROUGH |
             Struct_Microsoft_Singularity_X86_PE_VALID);
    }
    bi->Pdpt32.lo = pdptBase;
#endif

    //
    // Set up the IDT and some function vectors
    //
    bi->Info32.lo = PointerToUint32(bi);
    bi->Cpu0.Fs32 = pfsBase;
    bi->Cpu0.Gs32 = pgsBase;
    bi->Kill32.lo = PointerToUint32((LPVOID)StopPhase0);
    bi->KillAction = 0;
    bi->Undump.lo    = PointerToUint32((LPVOID)undump_dat);
    bi->IdtEnter0.lo = PointerToUint32((LPVOID)IdtEnter0);
    bi->IdtEnter1.lo = PointerToUint32((LPVOID)IdtEnter1);
    bi->IdtEnterN.lo = PointerToUint32((LPVOID)IdtEnterN);
    bi->IdtTarget.lo = PointerToUint32((LPVOID)&IdtTarget);
    bi->MpEnter32.lo = PointerToUint32((LPVOID)MpEnter);

    uint32 __far * pWarmReset = (uint32 __far *)_MK_FP(0x40, 0x67);
    bi->BiosWarmResetVector = *pWarmReset;

    IoSpaceWrite8(CMOS_SELECT, 0xf);
    bi->BiosWarmResetCmos = IoSpaceRead8(CMOS_DATA);

    printf("  Warm Reset CMOS   = %8x          (before)\n", bi->BiosWarmResetCmos);
    printf("  Warm Reset Vector = %8lx %8lx (before)\n",
           bi->BiosWarmResetVector,
           (bi->BiosWarmResetVector >> 12) + (bi->BiosWarmResetVector & 0xffff));

    *pWarmReset = (uint32)(LPVOID)MpEnter;
    IoSpaceWrite8(CMOS_SELECT, 0xf);
    IoSpaceWrite8(CMOS_DATA, 0xa);

    IoSpaceWrite8(CMOS_SELECT, 0xf);
    uint8 cmos = IoSpaceRead8(CMOS_DATA);

    printf("  Warm Reset CMOS   = %8x          (pre-boot)\n", cmos);
    printf("  Warm Reset Vector = %8lx %8lx (pre-boot)\n",
           *pWarmReset,
           (*pWarmReset >> 12) + (*pWarmReset & 0xffff));

    //
    // Save the heap pointer.
    //
    bi->Heap32.lo = ((uint32)npHeapTop) << 4;

    //
    // Move to next phase
    //
    printf("Calling 32-bit code.\n");
    BootPhase2(bi);

    return 0;
}

extern "C" void __cdecl StopPhase3()
{
    Struct_Microsoft_Singularity_BootInfo __far *bi = g_bi;

    Cls();
    BdInitDebugger(g_bi->DebugBasePort);
    printf("\n");
    Cls();
    printf("Singularity Boot Loader: Kernel Terminated w/ 0x%08x\n", bi->KillAction);

    uint32 __far * pWarmReset = (uint32 __far *)_MK_FP(0x40, 0x67);

    *pWarmReset = bi->BiosWarmResetVector;
    IoSpaceWrite8(CMOS_SELECT, 0xf);
    IoSpaceWrite8(CMOS_DATA, bi->BiosWarmResetCmos);

    IoSpaceWrite8(CMOS_SELECT, 0xf);
    uint8 cmos = IoSpaceRead8(CMOS_DATA);

    printf("  Warm Reset CMOS   = %8x          (restored)\n", cmos);
    printf("  Warm Reset Vector = %8lx %8lx (restored)\n",
        *pWarmReset,
        (*pWarmReset >> 12) + (*pWarmReset & 0xffff));

again:
    switch (bi->KillAction) {
        case Struct_Microsoft_Singularity_BootInfo_EXIT_AND_RESTART:
            printf("Reset via 8042.\n");
            Reset();
            BootHalt();
            break;
        case Struct_Microsoft_Singularity_BootInfo_EXIT_AND_SHUTDOWN:
            printf("Power-off via APM. (idt=%08lx..%08lx)\n",
                bi->BiosIdtPtr.addr,
                bi->BiosIdtPtr.addr + bi->BiosIdtPtr.limit);
            ApmPowerOff();
            BootHalt();
            break;
        case Struct_Microsoft_Singularity_BootInfo_EXIT_AND_WARMBOOT:
            printf("Error: Warm boot requested, but wasn't handled by HAL.\n");
            BootHalt();
            break;
        case Struct_Microsoft_Singularity_BootInfo_EXIT_AND_HALT:
            printf("Halt requested.\n");
            BootHalt();
            break;
        default:
            printf("Unknown KillAction=%x\n", bi->KillAction);
            if (g_CmdLine[0] != '\0') {
                printf("Forcing to EXIT_AND_WARMBOOT\n");
                bi->KillAction = Struct_Microsoft_Singularity_BootInfo_EXIT_AND_WARMBOOT;
                goto again;
            }
            BootHalt();
            break;
    }
}

//////////////////////////////////////////////////////////////////////////////
//
// MP Specific Routines
//
extern "C" void MpBootPhase1()
{
    Struct_Microsoft_Singularity_MpBootInfo __far *p_mbi = g_mbi;

    g_bi->MpStatus32 = Struct_Microsoft_Singularity_MpBootStatus_Phase1Entry;
    if (p_mbi->signature != Struct_Microsoft_Singularity_MpBootInfo_Signature) {
        printf("Bad MP Signature");
        g_bi->MpStatus32 = Struct_Microsoft_Singularity_MpBootStatus_BadSignature;
        BootHalt();
    }

    int cpuId = (int)p_mbi->TargetCpu;
    if (cpuId >= Struct_Microsoft_Singularity_MpBootInfo_MAX_CPU) {
        printf("Stopping processor (%d >= %d)\n",
               cpuId,
               Struct_Microsoft_Singularity_MpBootInfo_MAX_CPU);
        g_bi->MpStatus32 = Struct_Microsoft_Singularity_MpBootStatus_ConfigLimit;
        BootHalt();
    }

    //
    // Allocate pages for FS and GS data
    //

    // 2 Pages (1 RW, 1 RO) for processor context
    LPVOID pfs = (LPVOID) allocpages(2);
    if (pfs == 0) {
        g_bi->MpStatus32 = Struct_Microsoft_Singularity_MpBootStatus_AllocFailure;
        BootHalt;
    }

    // 2 Pages (1 RW, 1 RO) for processor context
    LPVOID pgs = (LPVOID) allocpages(2);
    if (pgs == 0) {
        g_bi->MpStatus32 = Struct_Microsoft_Singularity_MpBootStatus_AllocFailure;
        BootHalt();
    }

    //
    // Copy CpuInfo of bootstrap processor and modify copy to suit
    //
    Struct_Microsoft_Singularity_CpuInfo __far* bsp = &g_bi->Cpu0;
    Struct_Microsoft_Singularity_CpuInfo __far* cpu = bsp + cpuId;
    memcopy(cpu, bsp, sizeof(Struct_Microsoft_Singularity_CpuInfo));

    cpu->GdtPtr.addr = PointerToUint32(&cpu->GdtNull);
    cpu->Fs32 = PointerToUint32(pfs);
    cpu->GdtPF.base0_15  = (uint16)(cpu->Fs32);
    cpu->GdtPF.base16_23 = (uint8)(cpu->Fs32 >> 16);
    cpu->GdtPF.base24_31 = (uint8)(cpu->Fs32 >> 24);
    cpu->Gs32 = PointerToUint32(pgs);
    cpu->GdtPG.base0_15  = (uint16)cpu->Gs32;
    cpu->GdtPG.base16_23 = (uint8)(cpu->Gs32 >> 16);
    cpu->GdtPG.base24_31 = (uint8)(cpu->Gs32 >> 24);

    cpu->KernelStackBegin = p_mbi->KernelStackBegin;
    cpu->KernelStack      = p_mbi->KernelStack;
    cpu->KernelStackLimit = p_mbi->KernelStackLimit;

    cpu->ProcessorId = cpuId;

    g_bi->MpCpuCount = cpuId;
    g_bi->MpStatus32 = Struct_Microsoft_Singularity_MpBootStatus_Phase2Entry;

    MpBootPhase2(g_bi, cpu);
}
