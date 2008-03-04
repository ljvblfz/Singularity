///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   WriteOnlyMemoryRegister16.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class WriteOnlyMemoryRegister16 : IWriteOnlyRegister16
    {
        private const int RegisterWidth = 16 >> 3;

        IoMemory memory;

        public WriteOnlyMemoryRegister16(IoMemory m)
        {
            memory = m;
        }

        public override void Write(ushort value)
        {
            memory.Write16(0, value);
        }

        public static IWriteOnlyRegister16 Create(IoMemoryRange imr, uint offset)
        {
            return (IWriteOnlyRegister16)
                new WriteOnlyMemoryRegister16(imr.MemoryAtOffset(offset,
                                                                RegisterWidth,
                                                                Access.Write));
        }
    }
}
