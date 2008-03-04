///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadOnlyPortRegister16.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadOnlyPortRegister16 : IReadOnlyRegister16
    {
        private const int RegisterWidth = 16 >> 3;

        IoPort port;

        public ReadOnlyPortRegister16(IoPort port)   { this.port = port; }
        public override ushort Read()                { return port.Read16(); }

        public static IReadOnlyRegister16 Create(IoPortRange imr, uint offset)
        {
            return (IReadOnlyRegister16)
                new ReadOnlyPortRegister16(imr.PortAtOffset((ushort)offset,
                                                           RegisterWidth,
                                                           Access.Read));
        }
    }
}
