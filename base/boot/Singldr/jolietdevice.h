//////////////////////////////////////////////////////////////////////////////
//
//  jolietdevice.h - implementation of BootDevice class for Joliet volumes
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#ifndef __JOLIETDEVICE_H__
#define __JOLIETDEVICE_H__

#include "singldr.h"
#include "bootdevice.h"

//////////////////////////////////////////////////////////////////////////////
//
// Linked-in Functions (not declared in any headers)

// from singldr0.asm
extern "C" uint16 __cdecl BiosDiskRead(uint8 __far * addr, uint32 diskblock, uint16 blocks, uint16 driveid);
extern "C" uint16 __cdecl PModeTransfer(uint32 StartAddress, uint32 DestinationAddress, uint32 bytes);

//////////////////////////////////////////////////////////////////////////////
//
// Declaration for class JolietDevice

#pragma pack(4)
struct __near JolietDevice : BootDevice
{
private:
    // Drive Characteristics
    uint8   BootDrive;

    // Volume Characteristics
    uint32  BlockSize;

    // Root Directory Characteristics
    uint32  RootStartBlock;
    uint32  RootSize;

    // data buffer  -- this is reused frequently.  Be careful
    //                 about what you assume is in it.
    uint8 __far * FileBuffer;

    // method for looking an entry up in a directory
    int __near DirLookup(LPCHAR filename,
                         uint8 len,
                         FilePtr Directory,
                         FilePtr File) __far;
public:
    // constructor -- Hack alert - the complier claims that:
    //   "2 overloads have similar conversions" when the second
    // parameter isn't given.
    JolietDevice(uint8 driveId, uint8 /* nullParam */) __far
    {
        this->BootDrive = driveId;

        // since this is public, define it as part of constructing the object
        // for maximum compatibility, we just allocate it and leave it
        // null (alloc zeros it)
        this->CmdLine = (uint16 __far *)alloc(2048, 2);

    }

    // implement the virtual functions from the base class
    int __near OpenDevice() __far;

    int __near CloseDevice() __far;

    int __near GetFileProperties(LPCHAR filename,
                                 FilePtr file,
                                 FilePtr directory) __far;

    INT16 __near ReadFileLow(LPCHAR filename,
                             FilePtr file,
                             uint8 __far * buffer) __far;

    UINT32 __near ReadFileHigh(LPCHAR  filename,
                               FilePtr file,
                               uint32  buffer,
                               uint32  cbBuffer) __far;
};
#pragma pack()
#endif
