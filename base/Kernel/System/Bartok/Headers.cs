//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace Microsoft.Bartok.Runtime {

    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

    internal struct PreHeader {

        internal MultiUseWord muw;

#if ATOMIC_RC_COLLECTOR
        internal UIntPtr refCount;
#endif

#if CONCURRENT_MS_COLLECTOR
        internal UIntPtr headerQueue;
#endif

    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PostHeader {

#if (REFERENCE_COUNTING_GC || DEFERRED_REFERENCE_COUNTING_GC)
        internal uint refState;
#endif

        [AccessedByRuntime("Accessed from halexn.cpp")]
        internal VTable vtableObject;

    }

}
