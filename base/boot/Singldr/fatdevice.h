//////////////////////////////////////////////////////////////////////////////
//
//  fatdevice.h - implementation of BootDevice class for Fat16/32 volumes
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#ifndef __FATDEVICE_H__
#define __FATDEVICE_H__

#include "singldr.h"
#include "bootdevice.h"

//////////////////////////////////////////////////////////////////////////////
//
// Linked-in Functions (not declared in any headers)

// from singldr0.asm
extern "C" uint16 __cdecl
BiosDiskRead(uint8 __far * addr,
             uint32 diskblock,
             uint16 blocks,
             uint16 driveid);

extern "C" uint16 __cdecl
PModeTransfer(uint32 StartAddress,
              uint32 DestinationAddress,
              uint32 bytes);

//////////////////////////////////////////////////////////////////////////////
//
// Declaration for class FatDevice

#pragma pack(4)
struct __near FatDevice : BootDevice
{
private:
    // disk characteristics
    uint8   BootDrive;

    // partition characteristics
    uint32  LBAStart;
    uint32  LBASize;

    // Fat characteristics (read from boot sector, same for all FAT types)
    uint16  BytesPerSec;
    uint8   SecsPerClus;
    uint16  RsvdSecs;
    uint8   NumFats;
    uint16  RootDirEntries;
    uint32  HiddenSecs;

    // Fat characteristics (location in boot sector differs by FAT type)
    uint32  TotalSecs;
    uint32  NumFatSecs;

    // Fat characteristics (computed)
    uint16  RootDirSecs;
    uint32  TotalDataSecs;
    uint32  ClusterCount;
    uint8   FatType;
    uint32  RootStartClus;  // for fat32
    uint32  RootStartSec;   // for fat16
    uint32  FirstDataSec;
    uint32  BytesPerClus;
    uint32  DirEntriesPerClus;
    uint32  EndOfClusterMarker;
    uint32  BadClusterMarker;
    uint8   FatOffsetMultiplier;

    // data buffers -- these are reused frequently.  Be careful
    //                 about what you assume is in them.
    uint8 __far * MbrBuffer;
    uint8 __far * FatBuffer;
    uint8 __far * FileBuffer;

    uint32 __near CalcNextCluster(uint32 currentCluster) __far;

    void __near ReadCluster(uint32 ClusterNum, uint8 __far * buffer) __far;

    int __near DirLookup(LPCHAR filename,
                         uint8 len,
                         FilePtr directory,
                         FilePtr file,
                         int Fat16RootDir) __far;

public:

    // constructor
    FatDevice(uint8 driveId, uint8 expectedType) __far
    {
        // set the boot drive and expected type
        this->BootDrive = driveId;
        this->FatType = expectedType;

        // since this is global, define it as part of constructing the object
        // for maximum compatibility, we just allocate it and leave it
        // null (alloc zeros it)
        this->CmdLine = (uint16 __far *)alloc(2048, 2);
    }

    // implement the virtual functions from the base class
    int __near OpenDevice() __far;

    int __near CloseDevice() __far;

    int __near GetFileProperties(LPCHAR  filename,
                                 FilePtr file,
                                 FilePtr directory) __far;

    INT16 __near ReadFileLow(LPCHAR  filename,
                             FilePtr file,
                             uint8 __far * buffer) __far;

    UINT32 __near ReadFileHigh(LPCHAR  filename,
                               FilePtr file,
                               uint32  buffer,
                               uint32 cbBuffer) __far;
};

#pragma pack()
#endif
