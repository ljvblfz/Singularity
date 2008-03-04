//////////////////////////////////////////////////////////////////////////////
//
//  usbdevice.h - implementation of BootDevice class for Usb Fat16 volumes
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

// NB - this is very similar to the Fat code (it started with copy/paste)
//      However, since Fat32 and Partitions are completely unsupported,
//      and since Int13 extensions are not supported, this differs enough
//      to warrant its own implementation, even if the two implementations
//      are quite similar.

#ifndef __USBDEVICE_H__
#define __USBDEVICE_H__

#include "singldr.h"
#include "bootdevice.h"

//////////////////////////////////////////////////////////////////////////////
//
// Linked-in Functions (not declared in any headers)

// from singldr0.asm
extern "C" uint16 __cdecl BiosDiskReadCHS(uint8 __far * addr, uint32 diskblock, uint16 driveid, uint16 sectors, uint16 secpertrack, uint16 numheads);
extern "C" uint16 __cdecl PModeTransfer(uint32 StartAddress, uint32 DestinationAddress, uint32 bytes);

// from fatdevice.cpp
int FatShortNameMatch(uint8 __far * buffer, LPCHAR filename);
int FatLongNamePartialMatch(uint8 __far * buffer, LPCHAR filename, uint8 signature, uint8 checksum);
uint8 FatChecksum(uint8 __far * buffer);

//////////////////////////////////////////////////////////////////////////////
//
// Declaration for class UsbDevice

#pragma pack(4)
struct __near UsbDevice : BootDevice
{
private:
    // disk characteristics
    uint8   BootDrive;

    // Fat characteristics (read from boot sector)
    uint16  BytesPerSec;
    uint8   SecsPerClus;
    uint16  SecsPerTrack;
    uint16  NumHeads;
    uint16  RsvdSecs;
    uint8   NumFats;
    uint16  RootDirEntries;
    uint32  HiddenSecs;
    uint32  TotalSecs;
    uint32  NumFatSecs;

    // Fat characteristics (computed)
    uint16  RootDirSecs;
    uint32  TotalDataSecs;
    uint32  ClusterCount;
    uint8   FatType;
    uint32  RootStartSec;
    uint32  FirstDataSec;
    uint32  BytesPerClus;
    uint32  DirEntriesPerClus;
    uint32  EndOfClusterMarker;
    uint32  BadClusterMarker;
    uint8   FatOffsetMultiplier;

    // data buffers -- these are reused frequently.  Be careful
    //                 about what you assume is in them.
    uint8 __far * BootSectorBuffer;
    uint8 __far * FatBuffer;
    uint8 __far * FileBuffer;

    // Search the Fat for the cluster that follows the current one
    uint32 __near CalcNextCluster(uint32 currentCluster) __far;

    // read a cluster into a buffer
    void __near ReadCluster(uint32 ClusterNum, uint8 __far * buffer) __far;

    // Directory Lookup
    int __near DirLookup(LPCHAR filename,
                         uint8 len,
                         FilePtr Directory,
                         FilePtr File,
                         int Fat16RootDir) __far;

public:
    // constructor -- Hack alert - the complier claims that:
    //   "2 overloads have similar conversions" when the second
    // parameter isn't given.
    UsbDevice(uint8 driveId, uint8 /* nullParam */) __far{

        // set private fields based on params
        this->BootDrive = driveId;

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

    INT16 __near ReadFileLow(LPCHAR        filename,
                             FilePtr       file,
                             uint8 __far * buffer) __far;

    UINT32 __near ReadFileHigh(LPCHAR  filename,
                               FilePtr file,
                               uint32  buffer,
                               uint32  cbBuffer) __far;
};

#pragma pack()
#endif
