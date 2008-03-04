///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   IReadWriteRegister32.cs
//

using System;

namespace Microsoft.Singularity.Io
{
    [CLSCompliant(false)]
    public abstract class IReadWriteRegister32
    {
        public abstract uint Read();
        public abstract void Write(uint value);
    }
}
