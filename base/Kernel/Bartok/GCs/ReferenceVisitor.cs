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

    internal abstract class ReferenceVisitor {

        internal struct ObjectDescriptor {
            [ManualRefCounts]
            [Inline]
            internal ObjectDescriptor(VTable vtable, UIntPtr objectBase) :
                this(vtable, objectBase, UIntPtr.Zero, UIntPtr.Zero)
            {
            }

            [ManualRefCounts]
            [Inline]
            internal ObjectDescriptor(VTable vtable, UIntPtr objectBase,
                                      UIntPtr secondBase) :
                this(vtable, objectBase, secondBase, UIntPtr.Zero)
            {
            }

            [ManualRefCounts]
            [Inline]
            internal ObjectDescriptor(VTable vtable,
                                      UIntPtr objectBase,
                                      UIntPtr secondBase,
                                      UIntPtr extra)
            {
                this.vtable = vtable;
                this.objectBase = objectBase;
                this.secondBase = secondBase;
                this.extra = extra;
            }

            internal new VTable vtable;
            internal UIntPtr objectBase;
            internal UIntPtr secondBase;
            internal UIntPtr extra;
        }

        [ManualRefCounts]
        internal virtual UIntPtr VisitReferenceFields(Object obj)
        {
            return this.VisitReferenceFields(Magic.addressOf(obj),
                                             obj.vtable);
        }

        internal abstract UIntPtr VisitReferenceFields(UIntPtr objectBase,
                                                       VTable vtable);

        [Inline]
        protected abstract unsafe
        void Filter(UIntPtr *location, ref ObjectDescriptor objDesc);

        [ManualRefCounts]
        protected unsafe
        UIntPtr VisitReferenceFieldsTemplate(ref ObjectDescriptor objDesc)
        {
            UIntPtr pointerTracking = objDesc.vtable.pointerTrackingMask;
            uint objectTag = (pointerTracking & 0xf);
            UIntPtr size;
            switch (objectTag) {
              case ObjectLayout.SPARSE_TAG: {
                  UIntPtr *sparseObject = (UIntPtr *) objDesc.objectBase;
                  size = ObjectLayout.ObjectSize(objDesc.vtable);
                  pointerTracking >>= 4;
                  while (pointerTracking != 0) {
                      uint index = pointerTracking & 0xf;
                      pointerTracking >>= 4;
                      // The cast to int prevents C# from taking the
                      // index * sizeof(UIntPtr) to long:
                      UIntPtr *loc = sparseObject + (int) index;
                      this.Filter(loc, ref objDesc);
                  }
                  break;
              }
              case ObjectLayout.DENSE_TAG: {
                  // skip vtable
                  int postHeaderSize = PostHeader.Size;
                  UIntPtr *denseObject = (UIntPtr *)
                      (objDesc.objectBase + postHeaderSize);
                  size = ObjectLayout.ObjectSize(objDesc.vtable);
                  pointerTracking >>= 4;
                  while (pointerTracking != 0) {
                      if ((pointerTracking & ((UIntPtr)0x1)) != 0) {
                          this.Filter(denseObject, ref objDesc);
                      }
                      pointerTracking >>= 1;
                      denseObject++;
                  }
                  break;
              }
              case ObjectLayout.PTR_VECTOR_TAG: {
                  int postHeaderSize = PostHeader.Size;
                  uint length = *(uint*)(objDesc.objectBase + postHeaderSize);
                  size = ObjectLayout.ArraySize(objDesc.vtable, length);
                  int preHeaderSize = PreHeader.Size;
                  UIntPtr *elementAddress = (UIntPtr *)
                      (objDesc.objectBase + objDesc.vtable.baseLength -
                       preHeaderSize);
                  for (uint i = 0; i < length; i++, elementAddress++) {
                      this.Filter(elementAddress, ref objDesc);
                  }
                  break;
              }
              case ObjectLayout.OTHER_VECTOR_TAG: {
                  int postHeaderSize = PostHeader.Size;
                  uint length = *(uint*)(objDesc.objectBase + postHeaderSize);
                  size = ObjectLayout.ArraySize(objDesc.vtable, length);
                  if (objDesc.vtable.arrayOf == StructuralType.Struct) {
                      // pretend the struct is boxed and account for the
                      // presence of the vtable field
                      VTable elementVTable = objDesc.vtable.arrayElementClass;
                      UIntPtr elementMask = elementVTable.pointerTrackingMask;
                      // A structure with no references will have a SPARSE
                      // descriptor with no offset values.
                      if (elementMask != (UIntPtr) ObjectLayout.SPARSE_TAG) {
                          int preHeaderSize = PreHeader.Size;
                          UIntPtr elementAddress = (objDesc.objectBase +
                                                    objDesc.vtable.baseLength -
                                                    preHeaderSize -
                                                    postHeaderSize);
                          int elementSize = objDesc.vtable.arrayElementSize;
                          objDesc.vtable = elementVTable;
                          for (uint i = 0; i < length; i++) {
                              objDesc.objectBase = elementAddress;
                              this.VisitReferenceFieldsTemplate(ref objDesc);
                              elementAddress += elementSize;
                          }
                      }
                  }
                  break;
              }
              case ObjectLayout.PTR_ARRAY_TAG: {
                  int postHeaderSize = PostHeader.Size;
                  uint length = *(uint*)(objDesc.objectBase + postHeaderSize +
                                         sizeof(uint));
                  size = ObjectLayout.ArraySize(objDesc.vtable, length);
                  int preHeaderSize = PreHeader.Size;
                  UIntPtr *elementAddress = (UIntPtr *)
                      (objDesc.objectBase + objDesc.vtable.baseLength -
                       preHeaderSize);
                  for (uint i = 0; i < length; i++, elementAddress++) {
                      this.Filter(elementAddress, ref objDesc);
                  }
                  break;
              }
              case ObjectLayout.OTHER_ARRAY_TAG: {
                  int postHeaderSize = PostHeader.Size;
                  uint length = *(uint*)(objDesc.objectBase + postHeaderSize +
                                         sizeof(uint));
                  size = ObjectLayout.ArraySize(objDesc.vtable, length);
                  if (objDesc.vtable.arrayOf == StructuralType.Struct) {
                      // pretend the struct is boxed and account for the
                      // presence of the PostHeader
                      VTable elementVTable = objDesc.vtable.arrayElementClass;
                      UIntPtr elementMask = elementVTable.pointerTrackingMask;
                      // A structure with no references will have a SPARSE
                      // descriptor with no offset values.
                      if (elementMask != (UIntPtr) ObjectLayout.SPARSE_TAG) {
                          int preHeaderSize = PreHeader.Size;
                          int elementSize = objDesc.vtable.arrayElementSize;
                          UIntPtr elementAddress =
                              objDesc.objectBase + objDesc.vtable.baseLength -
                              preHeaderSize - postHeaderSize;
                          objDesc.vtable = elementVTable;
                          for (uint i = 0; i < length; i++) {
                              objDesc.objectBase = elementAddress;
                              this.VisitReferenceFieldsTemplate(ref objDesc);
                              elementAddress += elementSize;
                          }
                      }
                  }
                  break;
              }
              case ObjectLayout.STRING_TAG: {
                  int postHeaderSize = PostHeader.Size;
                  uint arrayLength =
                      *(uint*)(objDesc.objectBase + postHeaderSize);
                  size = ObjectLayout.StringSize(objDesc.vtable, arrayLength);
                  break;
              }
              default: {
                  // escape case
                  VTable.Assert((objectTag & 0x1) == 0,
                                "ReferenceVisitor: (objectTag & 0x1) == 0");
                  UIntPtr *largeObject = (UIntPtr *) objDesc.objectBase;
                  size = ObjectLayout.ObjectSize(objDesc.vtable);
                  int *pointerDescription = (int *) pointerTracking;
                  int count = *pointerDescription;
                  for (int i = 1; i <= count; i++) {
                      UIntPtr *loc = largeObject + *(pointerDescription+i);
                      this.Filter(loc, ref objDesc);
                  }
                  break;
              }
            }
            return size;
        }

        [ManualRefCounts]
        protected unsafe
        void VisitReferenceFieldsTemplate(ref ObjectDescriptor objDesc,
                                          int count)
        {
            UIntPtr pointerTracking = objDesc.vtable.pointerTrackingMask;
            uint objectTag = (pointerTracking & 0xf);
            switch (objectTag) {
              case ObjectLayout.PTR_VECTOR_TAG:
              case ObjectLayout.PTR_ARRAY_TAG: {
                  UIntPtr *elementAddress = (UIntPtr *) objDesc.objectBase;
                  for (int i = 0; i < count; i++, elementAddress++) {
                      this.Filter(elementAddress, ref objDesc);
                  }
                  break;
              }
              case ObjectLayout.OTHER_VECTOR_TAG:
              case ObjectLayout.OTHER_ARRAY_TAG: {
                  if (objDesc.vtable.arrayOf == StructuralType.Struct) {
                      // pretend the struct is boxed and account for the
                      // presence of the vtable field
                      VTable elementVTable = objDesc.vtable.arrayElementClass;
                      UIntPtr elementMask = elementVTable.pointerTrackingMask;
                      // A structure with no references will have a SPARSE
                      // descriptor with no offset values.
                      if (elementMask != (UIntPtr) ObjectLayout.SPARSE_TAG) {
                          int postHeaderSize = PostHeader.Size;
                          objDesc.objectBase -= postHeaderSize;
                          objDesc.secondBase -= postHeaderSize;
                          objDesc.extra += postHeaderSize;
                          int elementSize = objDesc.vtable.arrayElementSize;
                          objDesc.vtable = elementVTable;
                          for (int i = 0; i < count; i++) {
                              this.VisitReferenceFieldsTemplate(ref objDesc);
                              objDesc.objectBase += elementSize;
                              objDesc.secondBase += elementSize;
                              objDesc.extra -= elementSize;
                          }
                          objDesc.objectBase += postHeaderSize;
                          objDesc.secondBase += postHeaderSize;
                          objDesc.extra -= postHeaderSize;
                      }
                  }
                  break;
              }
              default: {
                  throw new Exception("Indexing non-indexed type");
              }
            }
        }

    }

    internal abstract class NonNullReferenceVisitor : ReferenceVisitor
    {

        internal unsafe abstract void Visit(UIntPtr *location);

#region HELP_DEVIRT
        // This method simply forces the compiler to generate a copy
        // of VisitReferenceFieldsTemplate in this class.
        [ManualRefCounts]
        internal override UIntPtr VisitReferenceFields(Object obj)
        {
            return this.VisitReferenceFields(Magic.addressOf(obj),
                                             obj.vtable);
        }

        // This method simply forces the compiler to generate a copy
        // of VisitReferenceFieldsTemplate in this class.
        [ManualRefCounts]
        internal override
        UIntPtr VisitReferenceFields(UIntPtr objectBase, VTable vtable)
        {
            ObjectDescriptor objDesc =
                new ObjectDescriptor(vtable, objectBase);
            return VisitReferenceFieldsTemplate(ref objDesc);
        }
#endregion

        [Inline]
        protected override unsafe
        void Filter(UIntPtr *location, ref ObjectDescriptor objDesc)
        {
            if (*location != UIntPtr.Zero) {
                this.Visit(location);
            }
        }

    }

    internal abstract class OffsetReferenceVisitor : ReferenceVisitor
    {

        internal abstract void FieldOffset(UIntPtr offset,
                                           ref ObjectDescriptor objDesc);

#region HELP_DEVIRT
        // This method simply forces the compiler to generate a copy
        // of VisitReferenceFieldsTemplate in this class.
        [ManualRefCounts]
        internal override sealed
        UIntPtr VisitReferenceFields(UIntPtr objectBase, VTable vtable)
        {
            ObjectDescriptor objDesc =
                new ObjectDescriptor(vtable, objectBase);
            return VisitReferenceFieldsTemplate(ref objDesc);
        }
#endregion

        [Inline]
        protected override unsafe sealed
        void Filter(UIntPtr *location, ref ObjectDescriptor objDesc)
        {
            UIntPtr offset = ((UIntPtr) location) - objDesc.objectBase;
            this.FieldOffset(offset, ref objDesc);
        }

    }

}
