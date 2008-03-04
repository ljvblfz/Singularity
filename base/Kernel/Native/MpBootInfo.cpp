///////////////////////////////////////////////////////////////////////////////
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//

#include "hal.h"

#if SINGULARITY_KERNEL

Struct_Microsoft_Singularity_MpBootInfo*
Struct_Microsoft_Singularity_MpBootInfo::g_HalGetMpBootInfo()
{
    Struct_Microsoft_Singularity_BootInfo* bi = Struct_Microsoft_Singularity_BootInfo::g_HalGetBootInfo();
    uint32 addr = (uint32) bi->MpBootInfo32;
    return (Struct_Microsoft_Singularity_MpBootInfo*)((uint8*) addr);
}

void
Struct_Microsoft_Singularity_MpBootInfo::g_HalReleaseMpStartupLock()
{
    Struct_Microsoft_Singularity_BootInfo* bi = Struct_Microsoft_Singularity_BootInfo::g_HalGetBootInfo();
    uint32 lockAddr = (uint32) bi->MpStartupLock32;
    if (lockAddr != 0)
    {
        *((uint16*) lockAddr) = 0;
    }
}

#endif // SINGULARITY_KERNEL
