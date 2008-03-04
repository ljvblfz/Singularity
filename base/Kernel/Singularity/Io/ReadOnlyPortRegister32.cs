///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReadOnlyPortRegister32.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class ReadOnlyPortRegister32 : IReadOnlyRegister32
    {
        private const int RegisterWidth = 32 >> 3;

        IoPort port;

        public ReadOnlyPortRegister32(IoPort port)  { this.port = port; }
        public override uint Read()                 { return port.Read32(); }

        public static IReadOnlyRegister32 Create(IoPortRange imr, uint offset)
        {
            return (IReadOnlyRegister32)
                new ReadOnlyPortRegister32(imr.PortAtOffset((ushort)offset,
                                                           RegisterWidth,
                                                           Access.Read));
        }
    }
}
