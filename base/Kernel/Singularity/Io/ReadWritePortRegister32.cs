///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadWritePortRegister32.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadWritePortRegister32 : IReadWriteRegister32
    {
        private const int RegisterWidth = 32 >> 3;

        IoPort port;

        public ReadWritePortRegister32(IoPort port) { this.port = port; }
        public override uint Read()                 { return port.Read32(); }
        public override void Write(uint value)      { port.Write32(value); }

        public static IReadWriteRegister32 Create(IoPortRange imr, uint offset)
        {
            return (IReadWriteRegister32)
                new ReadWritePortRegister32(imr.PortAtOffset((ushort)offset,
                                                             RegisterWidth,
                                                             Access.ReadWrite));
        }
    }
}
