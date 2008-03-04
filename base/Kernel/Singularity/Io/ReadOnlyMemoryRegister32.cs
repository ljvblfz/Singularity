///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadOnlyMemoryRegister32.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadOnlyMemoryRegister32 : IReadOnlyRegister32
    {
        private const int RegisterWidth = 32 >> 3;

        IoMemory memory;

        public ReadOnlyMemoryRegister32(IoMemory m)
        {
            memory = m;
        }

        public override uint Read()
        {
            return memory.Read32(0);
        }

        public static IReadOnlyRegister32 Create(IoMemoryRange imr, uint offset)
        {
            return (IReadOnlyRegister32)
                new ReadOnlyMemoryRegister32(imr.MemoryAtOffset(offset,
                                                               RegisterWidth,
                                                               Access.Read));
        }
    }
}
