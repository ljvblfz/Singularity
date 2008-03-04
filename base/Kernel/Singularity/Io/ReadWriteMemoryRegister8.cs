///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadWriteMemoryRegister8.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadWriteMemoryRegister8 : IReadWriteRegister8
    {
        private const int RegisterWidth = 8 >> 3;

        IoMemory memory;

        public ReadWriteMemoryRegister8(IoMemory m)
        {
            memory = m;
        }

        public override byte Read()
        {
            return memory.Read8(0);
        }

        public override void Write(byte value)
        {
            memory.Write8(0, value);
        }

        public static IReadWriteRegister8 Create(IoMemoryRange imr, uint offset)
        {
            return (IReadWriteRegister8)
                new ReadWriteMemoryRegister8(imr.MemoryAtOffset(offset,
                                                                RegisterWidth,
                                                                Access.ReadWrite));
        }
    }
}
