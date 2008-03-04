///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadWriteMemoryRegister16.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadWriteMemoryRegister16 : IReadWriteRegister16
    {
        private const int RegisterWidth = 16 >> 3;

        IoMemory memory;

        public ReadWriteMemoryRegister16(IoMemory m)
        {
            memory = m;
        }

        public override ushort Read()
        {
            return memory.Read16(0);
        }

        public override void Write(ushort value)
        {
            memory.Write16(0, value);
        }

        public static IReadWriteRegister16 Create(IoMemoryRange imr, uint offset)
        {
            return (IReadWriteRegister16)
                new ReadWriteMemoryRegister16(imr.MemoryAtOffset(offset,
                                                                RegisterWidth,
                                                                Access.ReadWrite));
        }
    }
}
