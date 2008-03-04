////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\IGetBeneficiary.cs
//
//  Note:
//

using System;
using System.Threading;

namespace Microsoft.Singularity.Scheduling
{
    [CLSCompliant(false)]
    public abstract class IGetBeneficiary
    {
        ///<summary>Returns the beneficiary of scheduling inheritance on this object.</summary>
        public abstract Thread GetBeneficiary();
    }
}
