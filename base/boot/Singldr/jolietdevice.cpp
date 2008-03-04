//////////////////////////////////////////////////////////////////////////////
//
//  jolietdevice.cpp - Access Joliet volumes from SINGLDR
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#include "jolietdevice.h"
#include "fnames.h"

#pragma warning(disable: 4505) // Compiler generated constructor unreferenced

//////////////////////////////////////////////////////////////////////////////
//
// Private Data
static uint32 MaxBlocksInBuffer;

//////////////////////////////////////////////////////////////////////////////
//
// Private Function

// helper function to match an ASCII string from the ini file
// against a Unicode entry in a directory
int CompareJolietEntry(LPCHAR filename, uint8 len, uint8 __far * entry)
{
    // first check length of file identifier at offset 32
    if (2 * len != entry[32]) {
        return 0;
    }

    // now try to match all chars
    for (uint8 indx = 0; indx < len-1; indx++) {
        if (UCase(filename[indx]) != UCase(entry[33 + 2 * indx + 1])) {
            return 0;
        }
    }
    return 1;
}

//////////////////////////////////////////////////////////////////////////////
//
// Private Method

// given a file name and a directory, populate a FilePtr structure
// or return an empty one if the file doesn't exist in the directory
int JolietDevice::DirLookup(LPCHAR filename,
                            uint8 len,
                            FilePtr directory,
                            FilePtr file) __far
{
    uint16 numblocks =
        (uint16) ((directory->Size + BlockSize - 1) / BlockSize);

    uint8 __far * buffer = FileBuffer; // for progressing through buffer
    uint16 bytecounter = 0; // count # bytes of dir entry we've already checked

    // read the directory from the disk (32KB limit)
    if (directory->Size>0x7FFF) {
        printf("Directory too large\n");
        return -1;
    }
    BiosDiskRead(buffer,
                 directory->FirstBlock,
                 numblocks,
                 (uint16)BootDrive);

    // scan through entries:
    while (bytecounter < directory->Size) {
        // check this entry
        if (CompareJolietEntry(filename, len, buffer) == 1) {
            // on success, save the info for this file and exit
            file->FirstBlock = *((uint32 __far *)(buffer+2));
            file->Size = *((uint32 __far *)(buffer+10));
            return 0;
        }
        else {
            uint8 tmp = buffer[0];
            buffer += tmp;
            bytecounter += (uint16)buffer[0];
            // handle the zero-padding that might exist to
            // block-align the next entry:
            while (buffer[0] == 0 && bytecounter < directory->Size) {
                buffer++;
                bytecounter++;
            }
        }
        if (bytecounter == directory->Size) {
            return -1;
        }
    }
    return -1;
}


//////////////////////////////////////////////////////////////////////////////
//
// Public Methods

static int IsJolietSignature(uint8 __far* buffer)
{
        // check the signatures:  chars 1-5 = "CD001" and
        // chars 88-90 = 0x25 0x2fh 0x45
    return (buffer[0] == 2 &&
            buffer[1] == 'C' &&
            buffer[2] == 'D' &&
            buffer[3] == '0' &&
            buffer[4] == '0' &&
            buffer[5] == '1' &&
            buffer[88] == 0x25 &&
            buffer[89] == 0x2f &&
            buffer[90] == 0x45);
}

int JolietDevice::OpenDevice() __far
{
    uint8 currentblock;

    // allocate memory for reading a block
    FileBuffer = (uint8 __far *) alloc(0x7FFF, 0); // 32KB

    // temp to make using far pointers easier
    uint8 __far * buffer = FileBuffer;

    // find the SVD
    currentblock = 16;
    do {
        BiosDiskRead(FileBuffer, currentblock, 1, BootDrive);
        currentblock++;
        if (buffer[0] == 0xFF) {
            // somehow we couldn't find the SVD even
            // though we booted off of it...
            printf("Unable to find Joliet SVD");
            return -1;
        }
    } while (!IsJolietSignature(buffer));

    // get the block size
    uint16 tmp16 = *((uint16 __far *)(buffer+128));
    BlockSize = tmp16;
    MaxBlocksInBuffer = 0x7FFF / BlockSize;

    // read the root directory info
    RootStartBlock = *((uint32 __far *)(buffer+158));
    RootSize = *((uint32 __far *)(buffer+166));

    // display results
    printf("\nCD Configuration\n");
    printf("------------------------------\n");
    printf("Boot Drive = %2xh\n", BootDrive);
    printf("Block Size = %8xh\n", BlockSize);
    printf("Root Start = %8xh\n", RootStartBlock);
    printf("Root Size  = %8xh\n", RootSize);

    return 0;
}

int JolietDevice::CloseDevice() __far
{
    return 0;
}

int JolietDevice::GetFileProperties(LPCHAR filename,
                                    FilePtr file,
                                    FilePtr directory) __far
{
    LPCHAR fname = filename;
    uint8 len;
    int result;
    char c;

    // we'll continually recycle the File and Directory structs
    directory->FirstBlock = RootStartBlock;
    directory->Size = RootSize;
    file->Size = 0;
    file->FirstBlock = 0;

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
            printf("CD: Invalid filename: ");
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
        result = DirLookup(fname, len, directory, file);

        // undo the change we made to the filename:
        fname[len] = c;

        if (result == -1) {
            printf("CD: File not found: ");
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

INT16 JolietDevice::ReadFileLow(LPCHAR /* filename */,
                                FilePtr file,
                                uint8 __far * buffer) __far
{
    uint32 size = file->Size;
    uint16 blocks = (uint16) ((size + BlockSize - 1) / BlockSize);

    BiosDiskRead(buffer, file->FirstBlock, blocks, BootDrive);
    return 0;
}

UINT32 JolietDevice::ReadFileHigh(LPCHAR  /* filename */,
                                  FilePtr file,
                                  uint32 destinationAddress,
                                  uint32 /* cbDestinationAddress */) __far
{
    uint32 bytesread = 0;
    uint32 nextdestination = destinationAddress;
    uint32 nextblocknum = file->FirstBlock;
    uint32 bufferaddress = PointerToUint32(FileBuffer);

    while (bytesread < file->Size) {
        uint32 blocksThisRead = ((file->Size - bytesread) + BlockSize - 1) / BlockSize;
        if (blocksThisRead > MaxBlocksInBuffer) {
            blocksThisRead = MaxBlocksInBuffer;
        }
        uint32 bytesThisRead = blocksThisRead * BlockSize;

        // do the read
        BiosDiskRead(FileBuffer, nextblocknum, (uint16) blocksThisRead, BootDrive);

        // move the data into extended memory
        PModeTransfer(bufferaddress, nextdestination, bytesThisRead);

        // get next block number
        nextblocknum += blocksThisRead;

        // update the count of bytes read
        bytesread += bytesThisRead;

        // and update the destination address
        nextdestination += bytesThisRead;
    }

    // since we read full blocks at the bios level, we should trim
    // our count back down a bit here
    if (bytesread > file->Size) {
        bytesread = file->Size;
    }
    return bytesread;
}
