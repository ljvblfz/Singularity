//////////////////////////////////////////////////////////////////////////////
//
//  usbdevice.cpp - Access Fat16 USB volumes from SINGLDR
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#include "usbdevice.h"
#include "fnames.h"

#pragma warning(disable: 4505) // Compiler generated constructor unreferenced

//////////////////////////////////////////////////////////////////////////////
//
// Private functions for reading the USB disk

// read the FAT to find the cluster that follows currentCluster
uint32 UsbDevice::CalcNextCluster(uint32 currentCluster) __far
{
    uint32 SectorToRead, OffsetInSector;
    uint32 result32;
    uint16 result16;

    OffsetInSector = (currentCluster*FatOffsetMultiplier) % (BytesPerSec);
    SectorToRead = ((currentCluster*FatOffsetMultiplier) / (BytesPerSec)) + (RsvdSecs);

    uint8 __far * buffer = FatBuffer;

    BiosDiskReadCHS(buffer, SectorToRead, BootDrive, 1, SecsPerTrack, NumHeads);

    result16 = *((uint16 __far *)(buffer+OffsetInSector));
    result32 = result16; // implicit cast
    return result32;
}

// read cluster # ClusterNum into a (pre-allocated) buffer
// due to CHS limitations, we have to read it one sector at a time...
void UsbDevice::ReadCluster(uint32 ClusterNum, uint8 __far * buffer) __far
{
    uint8 __far * buff = buffer;
    int sectorcounter;

    // figure out the first sector:
    uint32 startingSector = ((ClusterNum - 2) * (SecsPerClus)) + (FirstDataSec);

    for (sectorcounter = 0; sectorcounter < SecsPerClus; sectorcounter++) {
        // read one sector
        BiosDiskReadCHS(buff, startingSector, BootDrive, 1, SecsPerTrack, NumHeads);

        // advance to next sector
        startingSector++;

        // advance the buffer
        buff += BytesPerSec;
    }
}

//////////////////////////////////////////////////////////////////////////////
//
// Private Method for matching filenames
//   (uses public funcs from FatDevice code)

// Search from the first cluster of a directory entry to find the Fat
// encoding of a filename.  We accomplish this through a FSM.
// Fat16RootDir is a special flag  the root directory is not a cluster chain,
// but all other directories are.
int UsbDevice::DirLookup(LPCHAR filename,
                         uint8  /* len */,
                         FilePtr directory,
                         FilePtr file,
                         int Fat16RootDir) __far
{
    // points into the data we've loaded from disk:
    uint8 __far * buffer;

    // we will use the generic term "block" to refer to a readable entity
    // on the disk, since the Fat16 root dir is in sectors while the rest
    // of Fat is in clusters
    uint16 totalBlocks, blockCounter = 0;
    uint16 nextBlock, entriesPerBlock, entryCounter = 0;

    if (Fat16RootDir) {
        totalBlocks = RootDirSecs;
        entriesPerBlock = BytesPerSec / 32;
        nextBlock = (uint16)RootStartSec;
    }
    else {
        totalBlocks = 2;  // value doesn't matter; make sure totalBlocks>blockCounter
        entriesPerBlock = (uint16)DirEntriesPerClus;
        nextBlock = (uint16)directory->FirstBlock;
    }

    // "segment" will refer to the portion of the LongFileName
    // about which we care, since it will be in 13-char chunks
    uint8 totalSegs, segCounter, segsMatched = 0;
    totalSegs = FullFNameLength(filename);
    totalSegs = (uint8) ((totalSegs + 12) / 13);
    segCounter = totalSegs;

    // long file name verification requires a checksum, as well as matching a
    // special "entry signature" in byte[0] of the directory entry
    uint8 checksum = 0, signature = 0;

    // the primary loop fetches data to check
    while (blockCounter < totalBlocks) {
        // read some data and set a counter for when we need more data
        if (Fat16RootDir) {
            BiosDiskReadCHS(FatBuffer, nextBlock, BootDrive, 1, SecsPerTrack, NumHeads);
            entryCounter = BytesPerSec / 32;
        }
        else {
            ReadCluster(nextBlock, FatBuffer);
            entryCounter = (uint16)DirEntriesPerClus;
        }
        buffer = FatBuffer;

        // the inner loop checks entries
        while (entryCounter>0) {
            // failure condition #1:  if the first bit is zero,
            // there are no more directory entries.
            if (buffer[0] == 0) {
                return -1;
            }

            // success condition #1:  do we match on 8.3?
            // success condition #2:  if segCounter == 0, does checksum match?
            if ((FatShortNameMatch(buffer, filename) == 1) ||
                (segCounter == 0 && FatChecksum(buffer) == checksum))
            {
                file->FirstBlock = (*((uint16 __far *)(buffer+20))<<16) +
                                   *((uint16 __far *)(buffer+26));
                file->Size = *((uint32 __far *)(buffer+28));
                return 0;
            }

            // progress condition: do we match on the current segment of
            // long file name?
            // first calc checksum and the signature
            if (segCounter == totalSegs) {
                signature = (uint8) (segCounter | 0x40);
                checksum = buffer[13];
            }
            else {
                signature = segCounter;
            }
            // now do the check
            if (signature == FatLongNamePartialMatch(buffer, filename, signature, checksum)) {
                // advance to next directory entry and earlier segment in the filename
                segCounter--;
                buffer += 32;
                entryCounter--;
            }
            else {
                // if we were checking the first LFN entry then it's time
                // to advance the counter
                if (segCounter == totalSegs) {
                    buffer += 32;
                    entryCounter--;
                }
                else {
                    // otherwise this might just be the start of
                    // the correct entry, so don't advance the counter,
                    // just reset to the first segment of the filename
                    segCounter = totalSegs;
                }
            }
        }
        // calculate next data block
        if (Fat16RootDir == 1) {
            nextBlock++;
            blockCounter++;
        }
        else {
            nextBlock = (uint16) CalcNextCluster(nextBlock);
            if (nextBlock >= (EndOfClusterMarker)) {
                blockCounter = 2;
            }
        }
    }
    return -1;
}

//////////////////////////////////////////////////////////////////////////////
//
// Public Functions

int UsbDevice::OpenDevice() __far
{
    int entrycounter = 4;   // up to 4 entries in partition table

    // allocate memory for reading a sector (assume 512-byte sector size)
    BootSectorBuffer = (uint8 __far *) alloc(512, 0);

    // Read the BootSector
    // we are in a bind here: we don't know the SecsPerTrack or the NumHeads
    // but we need them to do the read!
    BiosDiskReadCHS(BootSectorBuffer, 0, BootDrive, 1, 1, 1);

    // temp to make using far pointers easier
    uint8 __far * buffer = BootSectorBuffer;

    // read data that should always be in the same place, no matter what
    BytesPerSec = *((uint16 __far *)(buffer+11));
    SecsPerClus = buffer[13];
    SecsPerTrack = *((uint16 __far *)(buffer+24));
    NumHeads = *((uint16 __far *)(buffer+26));
    RsvdSecs = *((uint16 __far *)(buffer+14));
    NumFats = buffer[16];
    RootDirEntries = *((uint16 __far *)(buffer+17));
    HiddenSecs = *((uint32 __far *)(buffer+28));

    // "compute" the NumFatSecs and TotalSecs fields
    // (i.e. read one value, if it is zero, read another)
    uint16 tmp16;
    tmp16 = *((uint16 __far *)(buffer+19));
    if (tmp16 == 0) {
        TotalSecs = *((uint32 __far *)(buffer+32));
    }
    else {
        TotalSecs = tmp16;
    }

    tmp16 = *((uint16 __far *)(buffer+22));
    if (tmp16 == 0) {
        NumFatSecs = *((uint32 __far *)(buffer+36));
    }
    else {
        NumFatSecs = tmp16;
    }

    // now we may compute RootDirSectors =
    // ((BPB_RootEntCnt*32) + (BPB_BytsPerSec-1))/BPB_BytesPerSec
    RootDirSecs = ((RootDirEntries*32) + (BytesPerSec-1))/BytesPerSec;

    // Next compute the total data sectors in the volume:
    TotalDataSecs = TotalSecs - (RsvdSecs + (NumFats*NumFatSecs) + RootDirSecs);

    // Finally, compute the # of clusters in the volume:
    ClusterCount = TotalDataSecs / SecsPerClus;

    // Do the verification:
    if (!(ClusterCount >= 4085 && ClusterCount<65525)) {
        printf("USB: File System Type is not supported\n");
        return -1; // the partition table did not match the boot sector
                   // we can't trust this filesystem.
    }
    FatType = 16;

    // now we can set fields that are FAT-type specific:
    EndOfClusterMarker = 0xFFF8;
    BadClusterMarker = 0xFFF7;
    FatOffsetMultiplier = 2;
    RootStartSec = (RsvdSecs + (NumFats*NumFatSecs));

    // final computations:
    FirstDataSec = (NumFats*NumFatSecs) + RsvdSecs + RootDirSecs;
    BytesPerClus = SecsPerClus * BytesPerSec;
    DirEntriesPerClus = BytesPerClus/32;

    // allocate data for remaining fs buffers:
    FatBuffer = (uint8 __far *) alloc((uint16)BytesPerClus, 0); // exactly the size of a cluster
    FileBuffer = (uint8 __far *) alloc(0x7FFF, 0);    // 32KB

    // display results
#if 0
    printf("\nUsb Configuration\n");
    printf("------------------------------\n");
    printf("Boot Drive = %2xh\n", BootDrive);
    printf("Bytes/Sector = %4xh\n", BytesPerSec);
    printf("\nFileSystem Configuration\n");
    printf("------------------------------\n");
    printf("Fat Type = FAT%d\n", FatType);
    printf("Sectors/Cluster  = %2xh\n", SecsPerClus);
    printf("Reserved Sectors = %4xh\n", RsvdSecs);
    printf("Number of Fats = %2xh\n", NumFats);
    printf("Hidden Sectors = %8lxh\n", HiddenSecs);
    printf("Sectors per Fat = %8lxh\n", NumFatSecs);
    printf("First Data Sector in Partition = %8lxh\n", FirstDataSec);
    printf("Bytes/Cluster = %8lxh\n", BytesPerClus);
    printf("Dir Entries/Cluster = %8lxh\n", DirEntriesPerClus);
#endif

    return 0;
}

int UsbDevice::CloseDevice() __far
{
    return 0;
}

int UsbDevice::GetFileProperties(LPCHAR filename,
                                 FilePtr file,
                                 FilePtr directory) __far
{
    LPCHAR fname = filename;
    uint8 len;
    int result;
    char c;
    int isRootDir = 1; // the Fat16 Root Directory is special.
                       // Flag the first Fat16 read (but not subsequent reads)

    // we'll continually recycle the File and Directory structs
    file->Size = 0;
    file->FirstBlock = 0;

    // traverse through the filename, identifying tokens and looking them
    // up in the current context
    bool done = 0;
    while (!done) {
        // consume the leading '/'
        while (fname[0] == '/') {
            fname++;
        }

        // failure condition:  whitespace
        if (IsEndToken(fname[0])) {
            printf("USB: Invalid filename: ");
            PutFName(filename);
            printf("\n");
            return -1;
        }

        // find the next separator in the filename ('/' or whitespace),
        // store it, and replace it with 0
        len = ShortFNameLength(fname);
        c = fname[len];
        fname[len] = 0;

        // look it up and put the results into File
        result = DirLookup(fname, len, directory, file, isRootDir);

        // undo the change we made to the filename:
        fname[len] = c;

        // now shut off the isRootDir flag for subsequent directory scans
        isRootDir = 0;
        if (result == -1) {
            printf("USB: File not found: ");
            PutFName(filename);
            printf("\n");
            return -1;
        }

        // check loop termination condition
        if (c != '/') {
            done = 1;
        }
        else {
            fname += len;
            // transfer results from File into Directory
            directory->FirstBlock = file->FirstBlock;
            directory->Size = file->Size;
            file->Size = 0;
            file->FirstBlock = 0;
        }
    }
    return 0;
}

INT16 UsbDevice::ReadFileLow(LPCHAR /* filename */,
                             FilePtr file,
                             uint8 __far * buffer) __far
{
    uint32 bytesread = 0;
    uint8 __far * destination = buffer;
    uint32 currentcluster = file->FirstBlock;

    while (bytesread < file->Size) {
        // read the current cluster
        if (currentcluster == BadClusterMarker) {
            printf("USB: Bad Cluster encountered\n");
            return -1;
        }
        ReadCluster(currentcluster, destination);
        currentcluster = CalcNextCluster(currentcluster);
        bytesread += BytesPerClus;
        destination = (uint8 __far *) _MK_FP(_FP_SEG(destination), (_FP_OFF(destination)+BytesPerClus));
    }
    return 0;
}

uint32 UsbDevice::ReadFileHigh(
                               LPCHAR /* filename */,
                               FilePtr file,
                               uint32 destinationAddress,
                               uint32 /* cbDestinationAddress */
                              ) __far
{
    uint32 bytesread = 0;
    uint32 nextdestination = destinationAddress;
    uint32 currentcluster = file->FirstBlock;
    uint32 sector;

    uint32 bufferaddress = PointerToUint32(FileBuffer);

    while (bytesread < file->Size) {
        // ensure cluster is valid
        if (currentcluster == BadClusterMarker) {
            printf("USB: Bad Cluster encountered\n");
            return bytesread;
        }

        // calc true sector for this cluster
        sector = ((currentcluster-2) * SecsPerClus)+FirstDataSec;

        // read one cluster
        BiosDiskReadCHS(FileBuffer, sector, BootDrive, SecsPerClus, SecsPerTrack, NumHeads);

        // move the data into extended memory
        PModeTransfer(bufferaddress, nextdestination, BytesPerClus);

        // get next cluster number
        currentcluster = CalcNextCluster(currentcluster);

        // update the count of bytes read
        bytesread += BytesPerClus;

        // and update the destination address
        nextdestination += BytesPerClus;
    }

    // since we read full sectors at the bios level, we should
    // trim our count back down a bit here
    if (bytesread>file->Size) {
        bytesread = file->Size;
    }
    return bytesread;
}
