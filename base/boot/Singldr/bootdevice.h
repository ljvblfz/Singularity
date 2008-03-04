//////////////////////////////////////////////////////////////////////////////
//
//  bootdevice.h - base class for all boot devices used by Singldr
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#ifndef __BOOTDEVICE_H__
#define __BOOTDEVICE_H__

#include "singldr.h"

// This is a virtual class for a read only filesystem device.
//
// 0 - Constructor
// 1 - OpenDevice()         -- initialize the device and filesystem
// 2 - GetFileProperties()  -- open a file for reading, get its
//                             size and position
// 3 - ReadFileLow()        -- read a file into low memory
//                             is a hack, we could do without it)
// 4 - ReadFileHigh()       -- read a file into high memory
// 5 - CloseDevice()        -- close the device

// There is some linker wizardry going on here.  We're in TINY mode, which
// means that all code must be in the same segment, but data can reside
// elsewhere (if it uses a __far pointer).  This class definition says that
// the data objects will be __far, but the methods themselves will be __near.
//
// Consider the following text:
//     struct __near [1] BootDevice
//     {
//         virtual int __near [2] OpenDevice __far [3] = 0;
//     }
//
// The first __near, [1], says that the classes vtable pointer is in near
// space, i.e. is in the same code segment as CS.
// The second __near, [2], says that the OpenDevice virtual function pointer
// is in the near space, i.e. is in the same code segment as CS.
// The __far, [3], says that the object is not in the common code segment,
// as it is allocated the global heap.
//

extern void __far * operator new(uint cbSize);

struct __near BootDevice
{
public:
    // We'll expose one public field, since it is used to adjust
    // the debug port.  This lets us avoid creating a property
    // that returns a pointer to an internal field.
    uint16 __far *CmdLine;

    // each constructor will be different, as each will take parameters
    // the default constructor will do nothing:
    BootDevice() __far {};

    // As discussed above, we'll have an Init() in addition to the
    // constructor.
    // just to reiterate:  every method will be __near, since the code is
    // in ds=cs=ss.  The __far at the end indicates that the this pointer
    // is to an object in the far heap.
    virtual int __near OpenDevice() __far = 0;

    // We don't have free(), and we don't destroy objects, but we do
    // need a way to close down a boot medium (in particular PXE).
    virtual int __near CloseDevice() __far = 0;

    // Open the file described by filename, put its parameters in file
    // directory is used to avoid multiple allocs
    virtual int __near GetFileProperties(LPCHAR  filename,
                                         FilePtr file,
                                         FilePtr directory) __far = 0;

    // Get the file described by filename/file and put it in a
    // small (32KB) buffer
    virtual INT16 __near ReadFileLow(LPCHAR  filename,
                                     FilePtr file,
                                     uint8 __far * buffer) __far = 0;

    // Get the file described by filename/file and put it in high memory.
    virtual UINT32 __near ReadFileHigh(LPCHAR  filename,
                                       FilePtr file,
                                       uint32  buffer,
                                       uint32  cbBuffer
                                       ) __far = 0;

};

#endif
