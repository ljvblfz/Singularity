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
    using System.Threading;

    internal sealed class Magic {

        internal static extern UIntPtr OffsetOfVTable {
            [NoHeapAllocation]
            [Intrinsic]
            get;
        }

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern UIntPtr addressOf(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static unsafe extern UIntPtr *toPointer(ref Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static unsafe extern UIntPtr *toPointer(ref VTable o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern Object fromAddress(UIntPtr v);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern Thread toThread(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern Monitor toMonitor(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern EMU toEMU(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern VTable toVTable(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern Array toArray(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern String toString(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern RuntimeType toRuntimeType(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern Type toType(Object o);

        [Intrinsic]
        [NoHeapAllocation]
        internal static extern uint[] toUIntArray(Object o);

        [Intrinsic]
        internal static extern WeakReference toWeakReference(Object o);

        [Intrinsic]
        internal static extern void calli(System.UIntPtr p);

        [Intrinsic]
        internal static extern void calli(System.UIntPtr p, System.UIntPtr v);

        [Intrinsic]
        internal static extern void callFinalizer(Object o);

    }

}
