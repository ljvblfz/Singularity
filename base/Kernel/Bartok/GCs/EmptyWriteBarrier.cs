/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace System.GCs {

    using Microsoft.Bartok.Runtime;
    using System.Runtime.CompilerServices;

    internal unsafe class EmptyWriteBarrier : WriteBarrier
    {

        internal static EmptyWriteBarrier instance;

        internal static new void Initialize() {
            EmptyWriteBarrier.instance =  (EmptyWriteBarrier)
                BootstrapMemory.Allocate(typeof(EmptyWriteBarrier));
        }

        [Inline]
        protected override void CopyStructImpl(VTable vtable,
                                               UIntPtr srcPtr,
                                               UIntPtr dstPtr)
        {
            CopyStructNoBarrier(vtable, srcPtr, dstPtr);
        }

        [Inline]
        protected override Object AtomicSwapImpl(ref Object reference,
                                                 Object value)
        {
            return AtomicSwapNoBarrier(ref reference, value);
        }

        [Inline]
        protected override
        Object AtomicCompareAndSwapImpl(ref Object reference,
                                        Object newValue,
                                        Object comparand)
        {
            return AtomicCompareAndSwapNoBarrier(ref reference, newValue,
                                                 comparand);
        }

        [Inline]
        protected override void CloneImpl(Object srcObject, Object dstObject)
        {
            CloneNoBarrier(srcObject, dstObject);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        [Inline]
        protected override void ArrayZeroImpl(Array array,
                                              int offset,
                                              int length)
        {
            ArrayZeroNoBarrier(array, offset, length);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        [Inline]
        protected override void ArrayCopyImpl(Array srcArray, int srcOffset,
                                              Array dstArray, int dstOffset,
                                              int length)
        {
            ArrayCopyNoBarrier(srcArray, srcOffset,
                               dstArray, dstOffset,
                               length);
        }

        [Inline]
        protected override void WriteReferenceImpl(UIntPtr *location,
                                                   Object value)
        {
            *location = Magic.addressOf(value);
        }

    }

}
