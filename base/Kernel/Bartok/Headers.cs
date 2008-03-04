/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace Microsoft.Bartok.Runtime {

    using System;
    using System.Runtime.CompilerServices;

    [RequiredByBartok]
    internal struct PreHeader {

        internal MultiUseWord muw;

        [Intrinsic]
        internal static int Size;

    }

    [RequiredByBartok]
    internal struct PostHeader {

#if (REFERENCE_COUNTING_GC || DEFERRED_REFERENCE_COUNTING_GC)
        internal uint refState;
#endif

#if SINGULARITY
        [AccessedByRuntime("accessed from halexn.cpp")]
#else
        [RequiredByBartok]
#endif
        internal VTable vtableObject;

        [Intrinsic]
        internal static int Size;

    }

}
