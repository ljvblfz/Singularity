////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   IoPort.cpp
//
//  Note:
//

#include "hal.h"

#if SINGULARITY

///////////////////////////////////////////////////////////// I/O Port Access.
//

uint8 Class_Microsoft_Singularity_Io_IoPort::g_HalReadInt8(uint32 port)
{
    __asm {
        mov eax,0;
        mov edx,port;
        in al,dx;
    }
}

uint16 Class_Microsoft_Singularity_Io_IoPort::g_HalReadInt16(uint32 port)
{
    __asm {
        mov eax,0;
        mov edx,port;
        in ax,dx;
    }
}

uint32 Class_Microsoft_Singularity_Io_IoPort::g_HalReadInt32(uint32 port)
{
    __asm {
        mov edx,port;
        in eax,dx;
    }
}

void Class_Microsoft_Singularity_Io_IoPort::g_HalWriteInt8(uint32 port,
                                                           uint8  value)
{
    __asm {
        mov edx,port;
        mov al,value;
        out dx,al;
    }
}

void Class_Microsoft_Singularity_Io_IoPort::g_HalWriteInt16(uint32 port,
                                                            uint16 value)
{
    __asm {
        mov edx,port;
        mov ax,value;
        out dx,ax;
    }
}

void Class_Microsoft_Singularity_Io_IoPort::g_HalWriteInt32(uint32 port,
                                                            uint32 value)
{
    __asm {
        mov edx,port;
        mov eax,value;
        out dx,eax;
    }
}

#if DO_UNSAFE_CODE_IN_IO
void Class_Microsoft_Singularity_Io_IoPort::g_HalReadFifo16(uint32 port,
                                                            uint16 *buffer,
                                                            uint32 count)
{
    __asm {
        mov edx,port;
        mov edi,buffer;
        mov ecx,count;
        rep insw;
    }
}

void Class_Microsoft_Singularity_Io_IoPort::g_HalWriteFifo16(uint32 port,
                                                             uint16 *buffer,
                                                             uint32 count)
{
    __asm {
        mov edx,port;
        mov esi,buffer;
        mov ecx,count;
        rep outsw;
    }
}
#endif // DO_UNSAFE_CODE_IN_IO

#endif // SINGULARITY_KERNEL

//
///////////////////////////////////////////////////////////////// End of File.
