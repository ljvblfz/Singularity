//////////////////////////////////////////////////////////////////////////////
//
//  singldr.h - SINGLDR Related Information.
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#ifndef __SINGLDR_H__
#define __SINGLDR_H__

//////////////////////////////////////////////////////////////////////////////
//
// Type declarations

typedef char CHAR;
typedef char INT8;
typedef short INT16;
typedef long INT32;
typedef unsigned char UINT8;
typedef unsigned short UINT16;
typedef unsigned long UINT32;
typedef unsigned long ADDR32;
typedef void __far * LPVOID;

typedef INT16 (__far * FPENTRY)(INT16 api, LPVOID args);
typedef int INT;
typedef char INT8, __far *LPINT8;
typedef short INT16, __far *LPINT16;
typedef long INT32, __far *LPINT32;
typedef long LARGEST;
typedef long BOOL;

typedef signed long LARGEST;
typedef unsigned long ULARGEST;

typedef unsigned int UINT;
typedef unsigned char UINT8, *PUINT8, __far *LPUINT8;
typedef unsigned short UINT16, __far *LPUINT16;
typedef unsigned long UINT32, *PUINT32, __far *LPUINT32;
typedef unsigned long ULARGEST;
typedef void __far * LPVOID;
typedef char CHAR, __far * LPCHAR;
typedef const char __far * LPCSTR;

//////////////////////////////////////////////////////////////////////////////
//
// Macro constants

#define NULL 0
#define DEBUG_VESA_MODES 0

//////////////////////////////////////////////////////////////////////////////
//
// Core types used by runtime system.

typedef signed char         bool;

typedef unsigned short      bartok_char;

typedef signed char         int8;
typedef signed short        int16;
typedef signed long         int32;

typedef struct
{
    unsigned long lo;
    unsigned long hi;
} int64;

typedef unsigned char       uint8;
typedef unsigned short      uint16;
typedef unsigned long       uint32;

typedef struct
{
    unsigned long lo;
    unsigned long hi;
} uint64;

typedef float               float32;
typedef double              float64;

typedef long                intptr;
typedef unsigned long       uintptr;

typedef unsigned int uint;

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

//////////////////////////////////////////////////////////////////////////////
//
// Global Functions
//
LPVOID alloc(uint16 cbSize, uint16 cbPad);
int printf(const char *pszFmt, ...);
uint32 PointerToUint32(LPVOID vp);
void memzero(LPVOID vp, uint32 cb);
void strcpy(LPCHAR dst, LPCHAR src);
void PutChar(char cOut);

//////////////////////////////////////////////////////////////////////////////
//
// File Type used by bootable devices

typedef struct
{
    uint32  Size;
    uint32  FirstBlock;
    //char    Name[11];
} FileData;
typedef FileData __far *FilePtr;

//////////////////////////////////////////////////////////////////////////////
//
//  MACROS to break C "far" pointers into their segment and offset components

#define _FP_SEG(fp) (* ((uint16 *)&(fp)+1) )
#define _FP_OFF(fp) (* ((uint16 *)&(fp)) )
#define _MK_FP(seg,offset)  ((LPVOID)(((uint32)(seg)<<16) | (uint32)(offset)))

//////////////////////////////////////////////////////////////////////////////
//
// more / misc defines

#define ARRAYOF(a)  (sizeof(a)/sizeof(a[0]))
#define OFFSETOF(s,m)   ((uint16)((uint8*)&((s *)0)->m - (uint8*)0))

#if USE_STRUCT_FIXED_ARRAYS
#define ELEMENT_PTR(array, index) &(array)[index]
#else
#define ELEMENT_PTR(array, index) &(array) + (index)
#endif

#define STATIC_ASSERT(condition)

#define CMOS_SELECT     0x70
#define CMOS_DATA       0x71

//////////////////////////////////////////////////////////////////////////////
//
// PNP / BIOS types

#pragma pack(1)
typedef struct _PNP_NODE {
    uint16  Size;                                       // 0x00:
    uint8   Node;                                       // 0x02:
    uint8   ProductId[4];                               // 0x03:
    uint8   DeviceType[3];                              // 0x07:
    uint16  DeviceAttributes;                           // 0x0a:
    // 0x0c:
    // followed by AllocatedResourceBlock, PossibleResourceBlock
    // and CompatibleDeviceId
} PNP_NODE, far *LPPNP_NODE;

typedef struct _PNP_ISACONFIG {
    uint8   Revision;                                   // 0x00:
    uint8   TotalCSNs;                                  // 0x01
    uint16  IsaReadDataPort;                            // 0x02
    uint16  Reserved;                                   // 0x04
} PNP_ISACONFIG, far *LPPNP_ISACONFIG;

typedef int16 (__far * PNP_FN)(int function, ...);

typedef struct _PNP_ROOT {
    uint8   Signature[4];                   // 0x00: $PnP (ascii)
    uint8   Revision;                       // 0x04:
    uint8   Length;                         // 0x05:
    uint16  ControlField;                   // 0x06:
    uint8   Checksum;                       // 0x08:
    uint32  EventFlagAddress;               // 0x09: Physical address
    PNP_FN  RealModeEntry;
    uint16  ProtectedModeEntryOffset;       // 0x11:
    uint32  ProtectedModeCodeBaseAddress;   // 0x13:
    uint32  OemDeviceId;                    // 0x17:
    uint16  RealModeDataSegment;            // 0x1b:
    uint32  ProtectedModeDataAddress;       // 0x1d
} PNP_ROOT, far *LPPNP_ROOT;

typedef struct _SMBIOS_ROOT
{
    uint8   Signature[4];                        // _SM_ (ascii)
    uint8   Checksum;
    uint8   Length;
    uint8   MajorVersion;
    uint8   MinorVersion;
    uint16  MaximumStructureSize;
    uint8   EntryPointRevision;
    uint8   Reserved[5];
    uint8   Signature2[5];                      // _DMI_ (ascii)
    uint8   IntermediateChecksum;
    uint16  StructureTableLength;
    uint32  StructureTableAddress;
    uint16  NumberStructures;
    uint8   Revision;
} SMBIOS_ROOT, far *LPSMBIOS_ROOT;

typedef struct _DMIBIOS_ROOT
{
    uint8   Signature2[5];                      // _DMI_ (ascii)
    uint8   IntermediateChecksum;
    uint16  StructureTableLength;
    uint32  StructureTableAddress;
    uint16  NumberStructures;
    uint8   Revision;
} DMIBIOS_ROOT, far *LPDMIBIOS_ROOT;

#pragma pack()

//////////////////////////////////////////////////////////////////////////////
//
// HAL and Pentium special registers

#pragma pack(4)
#include "halclass.h"
#pragma pack()

//////////////////////////////////////////////////////////////////////////////
//
// Vesa

#if DEBUG_VESA_MODES

typedef struct _VESA_INFO
{
    uint8   Signature[4];
    uint16  Version;
    LPCHAR  Oem;
    uint8   Capabilities[4];
    LPuint16    Modes;
    uint16  Memory;

    // VBE 2.0+
    uint16  OemVersion;
    LPCHAR  OemVendor;
    LPCHAR  OemProduct;
    LPCHAR  OemRevision;
} VESA_INFO, __far *LPVESA_INFO;

typedef struct _VESA_MODE
{
    uint16  Attributes;
    uint8   WindowA;
    uint8   WindowB;
    uint16  Granularity;
    uint16  Size;
    uint16  WindowASegment;
    uint16  WindowBSegment;
    uint8 __far * WindowFuncPtr;
    uint16  BytesPerLine;
    uint16  XRes;
    uint16  YRes;
    uint8   XCharSize;
    uint8   YCharSize;
    uint8   Planes;
    uint8   BitsPerPixel;
    uint8   Banks;
    uint8   MemoryModel;
    uint8   BankSize;
    uint8   ImagePages;
    uint8   Reserved;
    uint8   RedMaskSize;
    uint8   RedFieldPos;
    uint8   GreenMaskSize;
    uint8   GreenFieldPos;
    uint8   BlueMaskSize;
    uint8   BlueFieldPos;
    uint8   ReservedMaskSize;
    uint8   ReservedFieldPos;
    uint8   DirectColorInfo;

    // VBE 2.0
    uint32  PhysBasePtr;
    uint32  Reserved1;
    uint16  Reserved2;

    // VBE 3.0
    uint16  LinearBytesPerLine;
    uint8   BankImagePages;
    uint16  LinearImagePages;
    uint8   LinearRedMaskSize;
    uint8   LinearRedFieldPos;
    uint8   LinearGreenMaskSize;
    uint8   LinearGreenFieldPos;
    uint8   LinearBlueMaskSize;
    uint8   LinearBlueFieldPos;
    uint8   LinearReservedMaskSize;
    uint8   LinearReservedFieldPos;
    uint32  MaxPixelClock;
} VESA_MODE, __far *LPVESA_MODE;
#endif

#endif
