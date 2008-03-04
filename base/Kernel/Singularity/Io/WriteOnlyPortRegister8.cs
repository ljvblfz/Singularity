///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   WriteOnlyPortRegister8.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class WriteOnlyPortRegister8 : IWriteOnlyRegister8
    {
        private const int RegisterWidth = 8 >> 3;

        IoPort port;

        public WriteOnlyPortRegister8(IoPort port)  { this.port = port; }
        public override void Write(byte value)      { port.Write8(value); }

        public static IWriteOnlyRegister8 Create(IoPortRange imr, uint offset)
        {
            return (IWriteOnlyRegister8)
                new WriteOnlyPortRegister8(imr.PortAtOffset((ushort)offset,
                                                            RegisterWidth,
                                                            Access.Write));
        }
    }
}
