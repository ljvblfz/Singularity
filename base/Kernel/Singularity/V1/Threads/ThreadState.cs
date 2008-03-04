////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity - Singularity ABI
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ThreadState.cs
//
//  Note:
//

namespace Microsoft.Singularity.V1.Threads
{
    using System;
    using System.Runtime.CompilerServices;

    //| <include path='docs/doc[@for="ThreadState"]/*' />
    [Flags]
    public enum ThreadState
    {
        Unstarted           = 0x00,
        Running             = 0x01,
        Blocked             = 0x02,
        Suspended           = 0x04,
        Stopped             = 0x08,
    }
}
