///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadOnlyMemoryRegister8.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadOnlyMemoryRegister8 : IReadOnlyRegister8
    {
        private const int RegisterWidth = 8 >> 3;

        IoMemory memory;

        public ReadOnlyMemoryRegister8(IoMemory m)
        {
            memory = m;
        }

        public override byte Read()
        {
            return memory.Read8(0);
        }

        public static IReadOnlyRegister8 Create(IoMemoryRange imr, uint offset)
        {
            return (IReadOnlyRegister8)
                new ReadOnlyMemoryRegister8(imr.MemoryAtOffset(offset,
                                                               RegisterWidth,
                                                               Access.Read));
        }
    }
}
