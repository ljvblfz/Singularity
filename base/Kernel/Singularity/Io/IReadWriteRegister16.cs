///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   IReadWriteRegister16.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public abstract class IReadWriteRegister16
    {
        public abstract ushort Read();
        public abstract void Write(ushort value);
    }
}
