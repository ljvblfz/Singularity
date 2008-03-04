///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   WriteOnlyMemoryRegister32.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class WriteOnlyMemoryRegister32 : IWriteOnlyRegister32
    {
        private const int RegisterWidth = 32 >> 3;

        IoMemory memory;

        public WriteOnlyMemoryRegister32(IoMemory m)
        {
            memory = m;
        }

        public override void Write(uint value)
        {
            memory.Write32(0, value);
        }

        public static IWriteOnlyRegister32 Create(IoMemoryRange imr, uint offset)
        {
            return (IWriteOnlyRegister32)
                new WriteOnlyMemoryRegister32(imr.MemoryAtOffset(offset,
                                                                RegisterWidth,
                                                                Access.Write));
        }
    }
}
