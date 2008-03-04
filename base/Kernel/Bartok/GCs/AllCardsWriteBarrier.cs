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
    using System.Threading;

    internal unsafe class AllCardsWriteBarrier : WriteBarrier
    {

        internal static AllCardsWriteBarrier instance;

        internal static new void Initialize() {
            AllCardsWriteBarrier.instance = (AllCardsWriteBarrier)
                BootstrapMemory.Allocate(typeof(AllCardsWriteBarrier));
         }

         [Inline]
         protected override void StoreStaticFieldImpl(ref Object staticField,
                                                      Object value)
         {
             // No need to mark the card for a static field.
             *Magic.toPointer(ref staticField) = Magic.addressOf(value);
         }

         protected override void CopyStructImpl(VTable vtable,
                                                UIntPtr srcPtr,
                                                UIntPtr dstPtr)
         {
             CopyStructWithBarrier(vtable, srcPtr, dstPtr);
         }

         [Inline]
         protected override Object AtomicSwapImpl(ref Object reference,
                                                  Object value)
         {
             UIntPtr resultAddr =
                 Interlocked.Exchange(Magic.toPointer(ref reference),
                                      Magic.addressOf(value));
             RecordReference(ref reference, value);
             return Magic.fromAddress(resultAddr);
        }

        [Inline]
        protected override
        Object AtomicCompareAndSwapImpl(ref Object reference,
                                        Object newValue,
                                        Object comparand)
        {
            UIntPtr resultAddr =
                Interlocked.CompareExchange(Magic.toPointer(ref reference),
                                            Magic.addressOf(newValue),
                                            Magic.addressOf(comparand));
            RecordReference(ref reference, newValue);
            return Magic.fromAddress(resultAddr);
        }

        [Inline]
        protected override void CloneImpl(Object srcObject, Object dstObject)
        {
            CloneNoBarrier(srcObject, dstObject);
            RecordClone(dstObject);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        protected override void ArrayZeroImpl(Array array,
                                              int offset,
                                              int length)
        {
            ArrayZeroNoBarrier(array, offset, length);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        protected override void ArrayCopyImpl(Array srcArray, int srcOffset,
                                              Array dstArray, int dstOffset,
                                              int length)
        {
            if ((length > 1000) || ((length << 2) >= dstArray.Length)) {
                ArrayCopyNoBarrier(srcArray, srcOffset,
                                   dstArray, dstOffset,
                                   length);
                RecordClone(dstArray);
            } else {
                ArrayCopyWithBarrier(srcArray, srcOffset,
                                     dstArray, dstOffset,
                                     length);
            }
        }

        [Inline]
        protected override void WriteReferenceImpl(UIntPtr *location,
                                                   Object value)
        {
            *location = Magic.addressOf(value);
            RecordReference(location, value);
        }

        [Inline]
        private static void RecordClone(Object clone) {
            GenerationalCollector.installedRemSet.RecordClonedObject(clone);
        }

        [Inline]
        private static void RecordReference(ref Object location,
                                            Object value)
        {
            RecordReference(Magic.toPointer(ref location), value);
        }

        [Inline]
        private static void RecordReference(UIntPtr *location,
                                            Object value)
        {
            GenerationalCollector.
                installedRemSet.RecordReference(location, value);
        }

    }

}
