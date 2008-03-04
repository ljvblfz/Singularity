/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace System.GCs
{
    using Microsoft.Bartok.Runtime;
    using System.Runtime.CompilerServices;
    using System.Threading;

    [CCtorIsRunDuringStartup]
    internal abstract unsafe class WriteBarrier
    {

        [TrustedNonNull]
        private static WriteBarrier installedWriteBarrier;

        [TrustedNonNull]
        private static CopyFieldsVisitor copyFieldsVisitor;

        [TrustedNonNull]
        private static ZeroFieldsVisitor zeroFieldsVisitor;

        [PreInitRefCounts]
        internal static void Initialize()
        {
            switch (GC.wbType) {
#if !SINGULARITY || MARK_SWEEP_COLLECTOR
              case WBType.noWB: {
                  EmptyWriteBarrier.Initialize();
                  installedWriteBarrier = EmptyWriteBarrier.instance;
                  break;
              }
#endif
#if !SINGULARITY || SEMISPACE_COLLECTOR || SLIDING_COLLECTOR || ADAPTIVE_COPYING_COLLECTOR
              case WBType.Generational: {
                  GenerationalWriteBarrier.Initialize();
                  installedWriteBarrier = GenerationalWriteBarrier.instance;
                  break;
              }
#endif
#if !SINGULARITY || CONCURRENT_MS_COLLECTOR
              case WBType.CMS: {
                  WriteBarrierCMS.Initialize();
                  installedWriteBarrier = WriteBarrierCMS.instance;
                  break;
              }
#endif
#if !SINGULARITY || ATOMIC_RC_COLLECTOR
              case WBType.ARC: {
                  AtomicRCWriteBarrier.Initialize();
                  installedWriteBarrier = AtomicRCWriteBarrier.instance;
                  break;
              }
#endif
#if !SINGULARITY || SEMISPACE_COLLECTOR || SLIDING_COLLECTOR || ADAPTIVE_COPYING_COLLECTOR
              case WBType.AllCards: {
                  AllCardsWriteBarrier.Initialize();
                  installedWriteBarrier = AllCardsWriteBarrier.instance;
                  break;
              }
#endif
              default: {
                  VTable.NotReached("Unknown write barrier type: "+GC.wbType);
                  break;
              }
            }
            // copyFieldsVisitor = new CopyFieldsVisitor();
            WriteBarrier.copyFieldsVisitor = (CopyFieldsVisitor)
                BootstrapMemory.Allocate(typeof(CopyFieldsVisitor));
            // zeroFieldsVisitor = new ZeroFieldsVisitor();
            WriteBarrier.zeroFieldsVisitor = (ZeroFieldsVisitor)
                BootstrapMemory.Allocate(typeof(ZeroFieldsVisitor));
        }

        [Inline]
        protected virtual void StoreIndirectImpl(UIntPtr *location,
                                                 Object value)
        {
            this.WriteReferenceImpl(location, value);
        }

        [Inline]
        protected virtual void StoreIndirectImpl(ref Object reference,
                                                 Object value)
        {
            this.WriteReferenceImpl(ref reference, value);
        }

        [Inline]
        protected virtual void StoreObjectFieldImpl(Object obj,
                                                    UIntPtr fieldOffset,
                                                    Object value)
        {
            UIntPtr *fieldPtr = (UIntPtr *)
                (Magic.addressOf(obj) + fieldOffset);
            this.WriteReferenceImpl(fieldPtr, value);
        }

        [Inline]
        protected virtual void StoreStructFieldImpl(UIntPtr structPtr,
                                                    UIntPtr fieldOffset,
                                                    Object value)
        {
            UIntPtr *fieldPtr = (UIntPtr *) (structPtr + fieldOffset);
            this.WriteReferenceImpl(fieldPtr, value);
        }

        [Inline]
        protected virtual void StoreVectorElementImpl(Array vector,
                                                      int index,
                                                      int arrayElementSize,
                                                      UIntPtr fieldOffset,
                                                      Object value)
        {
            UIntPtr *fieldPtr =
                IndexedFieldPtr(vector, index, arrayElementSize, fieldOffset);
            this.WriteReferenceImpl(fieldPtr, value);
        }

        [Inline]
        protected virtual void StoreArrayElementImpl(Array array,
                                                     int index,
                                                     int arrayElementSize,
                                                     UIntPtr fieldOffset,
                                                     Object value)
        {
            UIntPtr *fieldPtr =
                IndexedFieldPtr(array, index, arrayElementSize, fieldOffset);
            this.WriteReferenceImpl(fieldPtr, value);
        }

        [Inline]
        protected virtual void StoreStaticFieldImpl(ref Object staticField,
                                                    Object value)
        {
            this.WriteReferenceImpl(ref staticField, value);
        }

        protected abstract void CopyStructImpl(VTable vtable,
                                               UIntPtr srcPtr,
                                               UIntPtr dstPtr);

        protected abstract Object AtomicSwapImpl(ref Object reference,
                                                 Object value);

        protected abstract Object AtomicCompareAndSwapImpl(ref Object reference,
                                                           Object newValue,
                                                           Object comparand);

        protected abstract void CloneImpl(Object srcObject, Object dstObject);

        protected void WriteReferenceImpl(ref Object reference, Object value)
        {
            this.WriteReferenceImpl(Magic.toPointer(ref reference), value);
        }

        protected abstract void WriteReferenceImpl(UIntPtr *location,
                                                   Object value);

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        protected abstract void ArrayZeroImpl(Array array,
                                              int offset,
                                              int length);

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        protected abstract void ArrayCopyImpl(Array srcArray, int srcOffset,
                                              Array dstArray, int dstOffset,
                                              int length);

        [Inline]
        protected static UIntPtr IndexedDataPtr(Array array) {
            return (UIntPtr) (Magic.addressOf(array) + 
                              (array.vtable.baseLength-(uint)PreHeader.Size));
        }

        [Inline]
        protected static UIntPtr *IndexedFieldPtr(Array obj,
                                                  int index,
                                                  int arrayElementSize,
                                                  UIntPtr fieldOffset)
        {
            UIntPtr dataPtr = IndexedDataPtr(obj);
            UIntPtr *fieldPtr = (UIntPtr *)
                (dataPtr + index * arrayElementSize + fieldOffset);
            return fieldPtr;
        }

        [Inline]
        protected void CopyStructNoBarrier(VTable vtable,
                                           UIntPtr srcPtr,
                                           UIntPtr dstPtr)
        {
            int preHeaderSize = PreHeader.Size;
            int postHeaderSize = PostHeader.Size;
            int structSize = ((int) ObjectLayout.ObjectSize(vtable) -
                              (preHeaderSize + postHeaderSize));
            Buffer.MoveMemory((byte *) dstPtr, (byte *) srcPtr, structSize);
        }

        [Inline]
        protected void CopyStructWithBarrier(VTable vtable,
                                             UIntPtr srcPtr,
                                             UIntPtr dstPtr)
        {
            copyFieldsVisitor.VisitReferenceFields(vtable, srcPtr, dstPtr);
        }

        [Inline]
        protected Object AtomicSwapNoBarrier(ref Object reference,
                                             Object value)
        {
            UIntPtr resultAddr =
                Interlocked.Exchange(Magic.toPointer(ref reference),
                                     Magic.addressOf(value));
            return Magic.fromAddress(resultAddr);
        }

        [Inline]
        protected Object AtomicCompareAndSwapNoBarrier(ref Object reference,
                                                       Object newValue,
                                                       Object comparand)
        {
            UIntPtr resultAddr =
                Interlocked.CompareExchange(Magic.toPointer(ref reference),
                                            Magic.addressOf(newValue),
                                            Magic.addressOf(comparand));
            return Magic.fromAddress(resultAddr);
        }

        [Inline]
        protected void CloneNoBarrier(Object srcObject,
                                      Object dstObject)
        {
            UIntPtr objectSize = System.GCs.ObjectLayout.Sizeof(srcObject);
            int preHeaderSize = PreHeader.Size;
            int postHeaderSize = PostHeader.Size;
            // We don't copy any of the header fields.
            Util.MemCopy(Magic.addressOf(dstObject) + postHeaderSize,
                         Magic.addressOf(srcObject) + postHeaderSize,
                         objectSize - preHeaderSize - postHeaderSize);
        }

        [Inline]
        protected void CloneWithBarrier(Object srcObject,
                                        Object dstObject)
        {
            copyFieldsVisitor.VisitReferenceFields(srcObject, dstObject);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        [Inline]
        protected void ArrayZeroNoBarrier(Array array, int offset,
                                          int length)
        {
            UIntPtr dataPtr = IndexedDataPtr(array);
            int elementSize = array.vtable.arrayElementSize;
            Buffer.ZeroMemory((byte *)dataPtr + offset * elementSize,
                              length * elementSize);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        [Inline]
        protected void ArrayZeroWithBarrier(Array array, int offset,
                                            int length)
        {
            UIntPtr dataPtr = IndexedDataPtr(array);
            int elementSize = array.vtable.arrayElementSize;
            UIntPtr startAddr = dataPtr + offset * elementSize;
            zeroFieldsVisitor.VisitReferenceFields(array.vtable,
                                                   startAddr, length);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        [Inline]
        protected void ArrayCopyNoBarrier(Array srcArray, int srcOffset,
                                          Array dstArray, int dstOffset,
                                          int length)
        {
            UIntPtr srcDataAddr = IndexedDataPtr(srcArray);
            UIntPtr dstDataAddr = IndexedDataPtr(dstArray);
            int elementSize = srcArray.vtable.arrayElementSize;
            VTable.Assert(elementSize ==
                          dstArray.vtable.arrayElementSize);
            Buffer.MoveMemory((byte *) (dstDataAddr + dstOffset * elementSize),
                              (byte *) (srcDataAddr + srcOffset * elementSize),
                              length * elementSize);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        [Inline]
        protected void ArrayCopyWithBarrier(Array srcArray, int srcOffset,
                                            Array dstArray, int dstOffset,
                                            int length)
        {
            UIntPtr srcDataAddr = IndexedDataPtr(srcArray);
            UIntPtr dstDataAddr = IndexedDataPtr(dstArray);
            int elementSize = srcArray.vtable.arrayElementSize;
            VTable.Assert(elementSize == dstArray.vtable.arrayElementSize);
            UIntPtr srcStartAddr = srcDataAddr + srcOffset * elementSize;
            UIntPtr dstStartAddr = dstDataAddr + dstOffset * elementSize;
            copyFieldsVisitor.VisitReferenceFields(srcArray.vtable,
                                                   srcStartAddr,
                                                   dstStartAddr,
                                                   length);
        }

        [Inline]
        internal static void StoreIndirect(UIntPtr *location, Object value)
        {
            installedWriteBarrier.StoreIndirectImpl(location, value);
        }

        [RequiredByBartok]
        [Inline]
        internal static void StoreIndirect(ref Object reference,
                                           Object value)
        {
            installedWriteBarrier.StoreIndirectImpl(ref reference, value);
        }

        [RequiredByBartok]
        [Inline]
        internal static void StoreObjectField(Object obj,
                                              UIntPtr fieldOffset,
                                              Object value)
        {
            installedWriteBarrier.StoreObjectFieldImpl(obj,
                                                       fieldOffset,
                                                       value);
        }

        [RequiredByBartok]
        [Inline]
        internal static void StoreStructField(UIntPtr structPtr,
                                              UIntPtr fieldOffset,
                                              Object value)
        {
            installedWriteBarrier.StoreStructFieldImpl(structPtr,
                                                       fieldOffset,
                                                       value);
        }

        [RequiredByBartok]
        [Inline]
        internal static void StoreVectorElement(Array vector,
                                                int index,
                                                int arrayElementSize,
                                                UIntPtr fieldOffset,
                                                Object value)
        {
            installedWriteBarrier.StoreVectorElementImpl(vector,
                                                         index,
                                                         arrayElementSize,
                                                         fieldOffset,
                                                         value);
        }

        [RequiredByBartok]
        [Inline]
        internal static void StoreArrayElement(Array array,
                                               int index,
                                               int arrayElementSize,
                                               UIntPtr fieldOffset,
                                               Object value)
        {
            installedWriteBarrier.StoreArrayElementImpl(array,
                                                        index,
                                                        arrayElementSize,
                                                        fieldOffset,
                                                        value);
        }

        [RequiredByBartok]
        [Inline]
        internal static void StoreStaticField(ref Object staticField,
                                              Object value)
        {
            installedWriteBarrier.StoreStaticFieldImpl(ref staticField, value);
        }

        [Inline]
        internal static void CopyStruct(VTable vtable,
                                        UIntPtr srcPtr,
                                        UIntPtr dstPtr)
        {
            installedWriteBarrier.CopyStructImpl(vtable, srcPtr, dstPtr);
        }

        [RequiredByBartok]
        [Inline]
        internal static Object AtomicSwap(ref Object reference,
                                          Object value)
        {
            return installedWriteBarrier.AtomicSwapImpl(ref reference, value);
        }

        [RequiredByBartok]
        [Inline]
        internal static Object AtomicCompareAndSwap(ref Object reference,
                                                    Object newValue,
                                                    Object comparand)
        {
            return
                installedWriteBarrier.AtomicCompareAndSwapImpl(ref reference,
                                                               newValue,
                                                               comparand);
        }

        [Inline]
        internal static void Clone(Object srcObject, Object dstObject)
        {
            installedWriteBarrier.CloneImpl(srcObject, dstObject);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        [Inline]
        internal static void ArrayZero(Array array, int offset, int length)
        {
            installedWriteBarrier.ArrayZeroImpl(array, offset, length);
        }

        // 'offset' is not relative to the lower bound, but is a count
        // of elements from the first element in the array.
        [Inline]
        internal static void ArrayCopy(Array srcArray, int srcOffset,
                                       Array dstArray, int dstOffset,
                                       int length)
        {
            installedWriteBarrier.ArrayCopyImpl(srcArray, srcOffset,
                                                dstArray, dstOffset,
                                                length);
        }

        [Inline]
        internal static void WriteReference(UIntPtr *location, Object value)
        {
            installedWriteBarrier.WriteReferenceImpl(location, value);
        }

        private class CopyFieldsVisitor : OffsetReferenceVisitor
        {

            // Struct copy
            internal void VisitReferenceFields(VTable vtable,
                                               UIntPtr srcPtr,
                                               UIntPtr dstPtr)
            {
                int postHeaderSize = PostHeader.Size;
                ObjectDescriptor objDesc =
                    new ObjectDescriptor(vtable,
                                         srcPtr - postHeaderSize,
                                         dstPtr - postHeaderSize,
                                         (UIntPtr) postHeaderSize);
                UIntPtr ignore = VisitReferenceFieldsTemplate(ref objDesc);
                int preHeaderSize = PreHeader.Size;
                UIntPtr limitSize =
                    ObjectLayout.ObjectSize(vtable) - preHeaderSize;
                UIntPtr previouslyDone = objDesc.extra;
                UIntPtr tailSize = limitSize - previouslyDone;
                if (tailSize > UIntPtr.Zero) {
                    Util.MemCopy(objDesc.secondBase + previouslyDone,
                                 objDesc.objectBase + previouslyDone,
                                 tailSize);
                }
            }

            internal void VisitReferenceFields(Object srcObject,
                                               Object dstObject)
            {
                int postHeaderSize = PostHeader.Size;
                ObjectDescriptor objDesc =
                    new ObjectDescriptor(srcObject.vtable,
                                         Magic.addressOf(srcObject),
                                         Magic.addressOf(dstObject),
                                         (UIntPtr) postHeaderSize);
                UIntPtr objectSize = VisitReferenceFieldsTemplate(ref objDesc);
                int preHeaderSize = PreHeader.Size;
                UIntPtr limitSize = objectSize - preHeaderSize;
                UIntPtr previouslyDone = objDesc.extra;
                UIntPtr tailSize = limitSize - previouslyDone;
                if (tailSize > UIntPtr.Zero) {
                    Util.MemCopy(objDesc.secondBase + previouslyDone,
                                 objDesc.objectBase + previouslyDone,
                                 tailSize);
                }
            }

            // Partial array copy
            internal void VisitReferenceFields(VTable vtable,
                                               UIntPtr srcElementPtr,
                                               UIntPtr dstElementPtr,
                                               int length)
            {
                ObjectDescriptor objDesc =
                    new ObjectDescriptor(vtable, srcElementPtr, dstElementPtr);
                VisitReferenceFieldsTemplate(ref objDesc, length);
                UIntPtr srcLimitAddr =
                    srcElementPtr + length * vtable.arrayElementSize;
                UIntPtr previouslyDone = objDesc.objectBase + objDesc.extra;
                UIntPtr tailSize = srcLimitAddr - previouslyDone;
                if (tailSize > UIntPtr.Zero) {
                    Util.MemCopy(objDesc.secondBase + objDesc.extra,
                                 previouslyDone, tailSize);
                }
            }

            internal override void FieldOffset(UIntPtr offset,
                                               ref ObjectDescriptor objDesc)
            {
                UIntPtr previouslyDone = objDesc.extra;
                objDesc.extra = offset + UIntPtr.Size;
                UIntPtr norefSize = offset - previouslyDone;
                if (norefSize > UIntPtr.Zero) {
                    Util.MemCopy(objDesc.secondBase + previouslyDone,
                                 objDesc.objectBase + previouslyDone,
                                 norefSize);
                }
                UIntPtr *srcAddr = (UIntPtr *) (objDesc.objectBase + offset);
                UIntPtr *dstAddr = (UIntPtr *) (objDesc.secondBase + offset);
                Object fieldValue = Magic.fromAddress(*srcAddr);
                WriteBarrier.WriteReference(dstAddr, fieldValue);
            }

        }

        private class ZeroFieldsVisitor : OffsetReferenceVisitor
        {

            internal void VisitReferenceFields(VTable vtable,
                                               UIntPtr elementAddr,
                                               int length)
            {
                int postHeaderSize = PostHeader.Size;
                ObjectDescriptor objDesc =
                    new ObjectDescriptor(vtable, elementAddr);
                VisitReferenceFieldsTemplate(ref objDesc, length);
                UIntPtr limitAddr =
                    elementAddr + length * vtable.arrayElementSize;
                UIntPtr previouslyDone = objDesc.objectBase + objDesc.extra;
                UIntPtr tailSize = limitAddr - previouslyDone;
                if (tailSize > UIntPtr.Zero) {
                    Buffer.ZeroMemory((byte *) previouslyDone, tailSize);
                }
            }

            internal override void FieldOffset(UIntPtr offset,
                                               ref ObjectDescriptor objDesc)
            {
                int postHeaderSize = PostHeader.Size;
                UIntPtr previouslyDone = objDesc.extra;
                objDesc.extra = offset + UIntPtr.Size;
                UIntPtr norefSize = offset - previouslyDone;
                if (norefSize > UIntPtr.Zero) {
                    Util.MemClear(objDesc.objectBase + previouslyDone,
                                  norefSize);
                }
                UIntPtr *fieldAddr = (UIntPtr *) (objDesc.objectBase + offset);
                WriteBarrier.WriteReference(fieldAddr, null);
            }

        }

    }

}
