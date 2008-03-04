///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   WriteOnlyPortRegister32.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class WriteOnlyPortRegister32 : IWriteOnlyRegister32
    {
        private const int RegisterWidth = 32 >> 3;

        IoPort port;

        public WriteOnlyPortRegister32(IoPort port)  { this.port = port; }
        public override void Write(uint value)       { port.Write32(value); }

        public static IWriteOnlyRegister32 Create(IoPortRange imr, uint offset)
        {
            return (IWriteOnlyRegister32)
                new WriteOnlyPortRegister32(imr.PortAtOffset((ushort)offset,
                                                             RegisterWidth,
                                                             Access.Write));
        }
    }
}
