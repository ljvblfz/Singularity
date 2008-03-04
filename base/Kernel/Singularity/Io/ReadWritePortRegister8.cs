///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadWritePortRegister8.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadWritePortRegister8 : IReadWriteRegister8
    {
        private const int RegisterWidth = 8 >> 3;

        IoPort port;

        public ReadWritePortRegister8(IoPort port)  { this.port = port; }
        public override byte Read()                 { return port.Read8(); }
        public override void Write(byte value)      { port.Write8(value); }

        public static IReadWriteRegister8 Create(IoPortRange imr, uint offset)
        {
            return (IReadWriteRegister8)
                new ReadWritePortRegister8(imr.PortAtOffset((ushort)offset,
                                                            RegisterWidth,
                                                            Access.ReadWrite));
        }
    }
}
