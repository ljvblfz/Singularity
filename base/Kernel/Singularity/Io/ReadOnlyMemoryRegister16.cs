///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadOnlyMemoryRegister16.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadOnlyMemoryRegister16 : IReadOnlyRegister16
    {
        private const int RegisterWidth = 16 >> 3;

        IoMemory memory;

        public ReadOnlyMemoryRegister16(IoMemory m)
        {
            memory = m;
        }

        public override ushort Read()
        {
            return memory.Read16(0);
        }

        public static IReadOnlyRegister16 Create(IoMemoryRange imr, uint offset)
        {
            return (IReadOnlyRegister16)
                new ReadOnlyMemoryRegister16(imr.MemoryAtOffset(offset,
                                                               RegisterWidth,
                                                               Access.Read));
        }
    }
}
