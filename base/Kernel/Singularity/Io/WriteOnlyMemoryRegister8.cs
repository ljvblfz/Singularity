///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   WriteOnlyMemoryRegister8.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class WriteOnlyMemoryRegister8 : IWriteOnlyRegister8
    {
        private const int RegisterWidth = 8 >> 3;

        IoMemory memory;

        public WriteOnlyMemoryRegister8(IoMemory m)
        {
            memory = m;
        }

        public override void Write(byte value)
        {
            memory.Write8(0, value);
        }

        public static IWriteOnlyRegister8 Create(IoMemoryRange imr, uint offset)
        {
            return (IWriteOnlyRegister8)
                new WriteOnlyMemoryRegister8(imr.MemoryAtOffset(offset,
                                                                RegisterWidth,
                                                                Access.Write));
        }
    }
}
