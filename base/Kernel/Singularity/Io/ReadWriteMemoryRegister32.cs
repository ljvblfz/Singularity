///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadWriteMemoryRegister32.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadWriteMemoryRegister32 : IReadWriteRegister32
    {
        private const int RegisterWidth = 32 >> 3;

        IoMemory memory;

        public ReadWriteMemoryRegister32(IoMemory m)
        {
            memory = m;
        }

        public override uint Read()
        {
            return memory.Read32(0);
        }

        public override void Write(uint value)
        {
            memory.Write32(0, value);
        }

        public static IReadWriteRegister32 Create(IoMemoryRange imr, uint offset)
        {
            return (IReadWriteRegister32)
                new ReadWriteMemoryRegister32(imr.MemoryAtOffset(offset,
                                                                RegisterWidth,
                                                                Access.ReadWrite));
        }
    }
}
