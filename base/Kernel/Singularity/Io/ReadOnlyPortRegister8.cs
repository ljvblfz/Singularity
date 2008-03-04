///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadOnlyPortRegister8.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadOnlyPortRegister8 : IReadOnlyRegister8
    {
        private const int RegisterWidth = 8 >> 3;

        IoPort port;

        public ReadOnlyPortRegister8(IoPort port)   { this.port = port; }
        public override byte Read()                 { return port.Read8(); }

        public static IReadOnlyRegister8 Create(IoPortRange imr, uint offset)
        {
            return (IReadOnlyRegister8)
                new ReadOnlyPortRegister8(imr.PortAtOffset((ushort)offset,
                                                           RegisterWidth,
                                                           Access.Read));
        }
    }
}
