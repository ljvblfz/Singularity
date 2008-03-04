//////////////////////////////////////////////////////////////////////////////
//
//  fatdevice.cpp - Access Fat16/32 volumes from SINGLDR
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#include "fatdevice.h"
#include "fnames.h"

#pragma warning(disable: 4505) // Compiler generated constructor unreferenced

//////////////////////////////////////////////////////////////////////////////
//
// Private Functions for reading the disk

// read the FAT to find the cluster that follows currentCluster
uint32 FatDevice::CalcNextCluster(uint32 currentCluster) __far
{
    uint32 SectorToRead, OffsetInSector;
    uint32 result32;
    uint16 result16;

    OffsetInSector = (currentCluster*FatOffsetMultiplier) % (BytesPerSec);
    SectorToRead = ((currentCluster*FatOffsetMultiplier) / (BytesPerSec)) + (RsvdSecs) + (LBAStart);

    uint8 __far * buffer = FatBuffer;
    BiosDiskRead(buffer, SectorToRead, 1, BootDrive);
    if (FatType == 32) {
        result32 = *((uint32 __far *)(buffer+OffsetInSector));
        result32 = result32 & 0x0FFFFFFF; // make it 28-bits
    }
    else {
        result16 = *((uint16 __far *)(buffer+OffsetInSector));
        result32 = result16; // implicit cast
    }
    return result32;
}

// read cluster # ClusterNum into a (pre-allocated) buffer
void FatDevice::ReadCluster(uint32 ClusterNum, uint8 __far * buffer) __far
{
    // figure out the first sector:
    uint32 startingSector = ((ClusterNum - 2) * SecsPerClus) + FirstDataSec + LBAStart;

    // do the read
    BiosDiskRead(buffer, startingSector, SecsPerClus, BootDrive);
}

//////////////////////////////////////////////////////////////////////////////
//
// Private Functions for matching filenames

// match a short filename (8.3, dot implicit)
int FatShortNameMatch(uint8 __far * buffer, LPCHAR filename)
{
    int counter = 0;

    // check up to the first dot or 8 chars, whatever comes first
    while (counter < 8) {
        if (*filename == '.' || *filename == 0) {
            break;
        }
        if (UCase(buffer[counter]) != UCase(*filename)) {
            return 0;
        }
        counter++;
        filename++;
    }

    // pad out to 8 chars with spaces
    while (counter < 8) {
        if (UCase(buffer[counter++]) != ' ') {
            return 0;
        }
    }

    if (*filename == '.') {
        filename++;
    }
    // check the file extension
    while (counter < 11) {
        if (*filename == 0) {
            break;
        }
        if (UCase(buffer[counter]) != UCase(*filename)) {
            return 0;
        }
        filename++;
        counter++;
    }

    // pad out to 11 chars with spaces
    while (counter < 11) {
        if (UCase(buffer[counter++]) != ' ') {
            return 0;
        }
    }
    return 1;
}

// match a piece of a long filename.  This is case insensitive,
// and assumes ascii (high byte of 2-byte strings=0)
int FatLongNamePartialMatch(uint8 __far * buffer,
                            LPCHAR filename,
                            uint8 signature,
                            uint8 checksum)
{
    // We don't really check 13 consecutive positions.
    // These are the places to check in the dir entry.
    int positions[] = {1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30};
    int counter = 0;

    // check entry signature in position 0
    if (buffer[0] != signature) {
        return 0;
    }

    // check FAT LFN signature in positions 11, 26, 27
    if (buffer[11] != 0x0F || buffer[26] != 0 || buffer[27] != 0) {
        return 0;
    }

    // verify checksum
    if (buffer[13] != checksum) {
        return 0;
    }

    // advance the filename to the current "segment" as indicated by signature
    filename += (13 * ((signature & ~0x40)-1));

    // compare the characters of the filename, and pad it out to 13
    // with 0x0000 and 0xFFFF's
    while (*filename != 0 && counter < 13) {
        // if this char doesn't match, fail instantly
        if ((UCase(buffer[positions[counter]]) != UCase(*filename)) ||
            (buffer[positions[counter]+1] != 0))
        {
            return 0;
        }
        counter++;
        filename++;
    }

    // all that's left is to check the padding
    if (counter < 13) {
        if ((buffer[positions[counter]] != 0) ||
            buffer[positions[counter]+1] != 0)
        {
            return 0;
        }
        counter++;

        while (counter < 13) {
            if ((buffer[positions[counter]] != 0xFF) ||
                buffer[positions[counter]+1] != 0xFF)
            {
                return 0;
            }
            counter++;
        }
    }
    return signature;
}

// this is straight out of the Fat docs:  compute the checksum for
// an 8.3 filename
uint8 FatChecksum(uint8 __far * buffer)
{
    uint8 checksum = 0;
    for (int i = 0; i < 11; i++) {
        checksum = (uint8)
            (((checksum & 1) ? 0x80 : 0) + (checksum>>1) + buffer[i]);
    }
    return checksum;
}

// Search from the first cluster of a directory entry to find the Fat
// encoding of a filename.  We accomplish this through a FSM.  Fat16RootDir
// is a special flag because Fat16 and Fat32 differ on the representation
// of a root directory.  The Fat32 code treats the root directory like any
// other.  The Fat16 code does not.
int FatDevice::DirLookup(LPCHAR filename,
                         uint8  /* len */,
                         FilePtr Directory,
                         FilePtr File,
                         int Fat16RootDir) __far
{
    // points into the data we've loaded from disk:
    uint8 __far * buffer;

    // we will use the generic term "block" to refer to a readable
    // entity on the disk, since the Fat16 root dir is in sectors while
    // the rest of Fat is in clusters
    uint32 totalBlocks, blockCounter = 0;
    uint32 nextBlock, entriesPerBlock, entryCounter = 0;

    if (Fat16RootDir) {
        totalBlocks = RootDirSecs;
        entriesPerBlock = BytesPerSec / 32;
        nextBlock = RootStartSec  + LBAStart;
    }
    else {
        totalBlocks = 2;  // keep this less than blockCounter
        entriesPerBlock = DirEntriesPerClus;
        nextBlock = Directory->FirstBlock;
    }

    // "segment" will refer to the portion of the LongFileName
    // about which we care, since it will be in 13-char chunks
    uint8 totalSegs, segCounter, segsMatched = 0;
    totalSegs = FullFNameLength(filename);
    totalSegs = (uint8) ((totalSegs + 12) / 13);
    segCounter = totalSegs;

    // long file name verification requires a checksum, as well
    // as matching a special "entry signature" in byte[0] of the
    // directory entry
    uint8 checksum = 0, signature = 0;

    // the primary loop fetches data to check
    while (blockCounter < totalBlocks) {
        // read some data and set a counter for when we need more data
        if (Fat16RootDir) {
            BiosDiskRead(FatBuffer, nextBlock, 1, BootDrive);
            entryCounter = BytesPerSec / 32;
        }
        else {
            ReadCluster(nextBlock, FatBuffer);
            entryCounter = DirEntriesPerClus;
        }
        buffer = FatBuffer;

        // the inner loop checks entries
        while (entryCounter > 0) {
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
                File->FirstBlock = (*((uint16 __far *)(buffer+20))<<16) +
                                   *((uint16 __far *)(buffer+26));
                File->Size = *((uint32 __far *)(buffer+28));
                return 0;
            }

            // progress condition: do we match on the current
            // segment of long file name?
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
                // advance to next directory entry and earlier
                // segment in the filename
                segCounter--;
                buffer += 32;
                entryCounter--;
            }
            else {
                // if we were checking the first LFN entry then it's
                // time to advance the counter
                if (segCounter == totalSegs) {
                    buffer += 32;
                    entryCounter--;
                }
                else {
                    // otherwise this might just be the start of the
                    // correct entry, so don't advance the counter,
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
            nextBlock = CalcNextCluster(nextBlock);
            if (nextBlock >= (EndOfClusterMarker))
                blockCounter = 2;
        }
    }
    return -1;
}

//////////////////////////////////////////////////////////////////////////////
//
// Public Functions

int FatDevice::OpenDevice() __far
{
    uint8 expectedType = FatType;
    int entrycounter = 4;   // up to 4 entries in partition table

    // allocate memory for reading a sector (assume 512-byte sector size)
    MbrBuffer = (uint8 __far *) alloc(512, 0);

    // Read the MBR
    BiosDiskRead(MbrBuffer, 0, 1, BootDrive);

    // temp to make using far pointers easier
    uint8 __far * buffer = MbrBuffer;

    // search the partition table for a partition whose
    // type matches our desired type
    buffer += 446;  // first entry is at offset 446
    while (entrycounter > 0) {
        // does the partition type match expectedtype
        if (buffer[4] == 0x0c && expectedType == 32) {
            FatType = 32;
            break;
        }
        if (buffer[4] == 0x0e && expectedType == 16) {
            FatType = 16;
            break;
        }// try the next entry
        entrycounter--;
        buffer += 16;
    }
    // exceptional condition, should never happen...
    if (entrycounter == 0) {
        printf("USB: Valid Fat partition not found\n");
        return -1;
    }

    // now get the first sector of the active partition, and the
    // partition size
    LBAStart = *((uint32 __far *)(buffer+8));
    LBASize  = *((uint32 __far *)(buffer+12));

    // we are done with the MBR.  now we need to read the boot sector
    // to get the FAT parameters
    BiosDiskRead(MbrBuffer, LBAStart, 1, BootDrive);
    buffer = MbrBuffer;

    // Verify the FileSystem type:
    // the Microsoft Fat32 documentation is very specific about how this ought to be done.
    // However, this is rather ugly, because it requires information that may not be in the bootsector.
    // For example, suppose that the volume is Fat16, and the volume is in very bad shape.
    // we may read 0 for a 16-bit value, and then look in an invalid portion of the boot sector for a 32-bit value.
    // UGH!  Well, we'll do it nonetheless:

    // read data that should always be in the same place, no matter what
    BytesPerSec = *((uint16 __far *)(buffer+11));
    SecsPerClus = buffer[13];
    RsvdSecs = *((uint16 __far *)(buffer+14));
    NumFats = buffer[16];
    RootDirEntries = *((uint16 __far *)(buffer+17));
    HiddenSecs = *((uint32 __far *)(buffer+28));

    // compute the NumFatSecs and TotalSecs fields
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
    if (!(ClusterCount >= 4085 && ClusterCount < 65525 && FatType == 16) && !(ClusterCount >= 65525 && FatType == 32)) {
        printf("USB: File System Type does not match Partition Table\n");
        return -1; // the partition table did not match the boot sector.
                   // we can't trust this filesystem.
    }

    // now we can set fields that are FAT-type specific:
    if (FatType == 16) {
        RootStartClus = 0;
        EndOfClusterMarker = 0xFFF8;
        BadClusterMarker = 0xFFF7;
        FatOffsetMultiplier = 2;
        RootStartSec = (RsvdSecs + (NumFats*NumFatSecs));
    }
    else {
        RootStartClus = *((uint32 __far *)(buffer+44));
        EndOfClusterMarker = 0x0FFFFFF8;
        BadClusterMarker = 0x0FFFFFF7;
        FatOffsetMultiplier = 4;
    }

    // final computations:
    FirstDataSec = (NumFats*NumFatSecs) + RsvdSecs + RootDirSecs;
    BytesPerClus = SecsPerClus * BytesPerSec;
    DirEntriesPerClus = BytesPerClus/32;

    // allocate data for remaining fs buffers:
    FatBuffer = (uint8 __far *) alloc((uint16)BytesPerClus, 0); // exactly the size of a cluster
    FileBuffer = (uint8 __far *) alloc(0x7FFF, 0);    // 32KB

    // display results
#if 0
    printf("\nDisk Configuration\n");
    printf("------------------------------\n");
    printf("Boot Drive = %2xh\n", BootDrive);
    printf("First Sector of Active Partition = %8lxh\n", LBAStart);
    printf("Sectors in Partition = %8lxh\n", LBASize);
    printf("Bytes/Sector = %4xh\n", BytesPerSec);
    printf("\nFileSystem Configuration\n");
    printf("------------------------------\n");
    printf("Fat Type = FAT%d\n", FatType);
    printf("Sectors/Cluster  = %2xh\n", SecsPerClus);
    printf("Reserved Sectors = %4xh\n", RsvdSecs);
    printf("Number of Fats = %2xh\n", NumFats);
    printf("Hidden Sectors = %8lxh\n", HiddenSecs);
    printf("Sectors per Fat = %8lxh\n", NumFatSecs);
    printf("First Cluster of Root Dir = %8lxh\n", RootStartClus);
    printf("First Data Sector in Partition = %8lxh\n", FirstDataSec);
    printf("Bytes/Cluster = %8lxh\n", BytesPerClus);
    printf("Dir Entries/Cluster = %8lxh\n", DirEntriesPerClus);
#endif

    return 0;
}

int FatDevice::CloseDevice() __far
{
    return 0;
}

int FatDevice::GetFileProperties(LPCHAR filename,
                                 FilePtr file,
                                 FilePtr directory) __far
{
    LPCHAR fname = filename;
    uint8 len;
    int result;
    char c;
    int Fat16RootDir = (FatType == 16); // Fat16 Root Dir is special.
                                        // Flag 1st Fat16 read

    // we'll continually recycle the File and Directory structs
    file->Size = 0;
    file->FirstBlock = 0;
    directory->FirstBlock = RootStartClus;

    // traverse through the filename, identifying tokens and
    // looking them up in the current context
    bool done = 0;
    while (!done) {
        // consume the leading '/'
        while (fname[0] == '/') {
            fname++;
        }

        // failure condition:  whitespace
        if (IsEndToken(fname[0])) {
            printf("FAT: Invalid filename: ");
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
        result = DirLookup(fname, len, directory, file, Fat16RootDir);

        // undo the change we made to the filename:
        fname[len] = c;

        // now shut off the Fat16RootDir flag for subsequent directory scans
        Fat16RootDir = 0;
        if (result == -1) {
            printf("FAT: File not found: ");
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

INT16 FatDevice::ReadFileLow(LPCHAR /* filename */,
                             FilePtr file,
                             uint8 __far * buffer) __far
{
    uint32 bytesread = 0;
    uint8 __far * destination = buffer;
    uint32 currentcluster = file->FirstBlock;

    while (bytesread < file->Size) {
        // read the current cluster
        if (currentcluster == BadClusterMarker) {
            printf("FAT: Bad Cluster encountered\n");
            return -1;
        }
        ReadCluster(currentcluster, destination);
        currentcluster = CalcNextCluster(currentcluster);
        bytesread += BytesPerClus;
        destination = (uint8 __far *) _MK_FP(_FP_SEG(destination), (_FP_OFF(destination)+BytesPerClus));
    }
    return 0;
}

UINT32 FatDevice::ReadFileHigh(LPCHAR  /* filename */,
                               FilePtr file,
                               uint32 destinationAddress,
                               uint32 /* cbDestination */) __far
{
    uint32 bytesread = 0;
    uint32 nextdestination = destinationAddress;
    uint32 currentcluster = file->FirstBlock;
    uint32 sector;

    uint32 bufferaddress = PointerToUint32(FileBuffer);

    while (bytesread < file->Size) {
        // ensure cluster is valid
        if (currentcluster == BadClusterMarker) {
            printf("FAT: Bad Cluster encountered\n");
            return bytesread;
        }

        // calc true sector for this cluster
        sector = ((currentcluster-2) * SecsPerClus)+ FirstDataSec + LBAStart;

        // do the read
        BiosDiskRead(FileBuffer, sector, SecsPerClus, BootDrive);

        // move the data into extended memory
        PModeTransfer(bufferaddress, nextdestination, BytesPerClus);

        // get next cluster number
        currentcluster = CalcNextCluster(currentcluster);

        // update the count of bytes read
        bytesread += BytesPerClus;

        // and update the destination address
        nextdestination += BytesPerClus;
    }

    // since we read full sectors at the bios level,
    // we should trim our count back down a bit here
    if (bytesread > file->Size)
        bytesread = file->Size;
    return bytesread;
}
