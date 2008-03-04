///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadWritePortRegister16.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadWritePortRegister16 : IReadWriteRegister16
    {
        private const int RegisterWidth = 16 >> 3;

        IoPort port;

        public ReadWritePortRegister16(IoPort port)  { this.port = port; }
        public override ushort Read()                { return port.Read16(); }
        public override void Write(ushort value)     { port.Write16(value); }

        public static IReadWriteRegister16 Create(IoPortRange imr, uint offset)
        {
            return (IReadWriteRegister16)
                new ReadWritePortRegister16(imr.PortAtOffset((ushort)offset,
                                                            RegisterWidth,
                                                            Access.ReadWrite));
        }
    }
}
