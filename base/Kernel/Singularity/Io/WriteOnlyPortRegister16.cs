///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   WriteOnlyPortRegister16.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public class WriteOnlyPortRegister16 : IWriteOnlyRegister16
    {
        private const int RegisterWidth = 16 >> 3;

        IoPort port;

        public WriteOnlyPortRegister16(IoPort port)  { this.port = port; }
        public override void Write(ushort value)     { port.Write16(value); }

        public static IWriteOnlyRegister16 Create(IoPortRange imr, uint offset)
        {
            return (IWriteOnlyRegister16)
                new WriteOnlyPortRegister16(imr.PortAtOffset((ushort)offset,
                                                            RegisterWidth,
                                                            Access.Write));
        }
    }
}
