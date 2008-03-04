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
    using System.Threading;
    using System.Runtime.CompilerServices;

    internal unsafe class BootstrapMemory
    {

        // WARNING: don't initialize any static fields in this class
        // without manually running the class constructor at startup!

        private static BumpAllocator pool;

        [PreInitRefCounts]
#if !SINGULARITY
        [NoStackLinkCheck]
#endif
        internal static void Initialize(UIntPtr systemMemorySize) {
            pool = new BumpAllocator(PageType.NonGC);
            UIntPtr memStart = MemoryManager.AllocateMemory(systemMemorySize);
            pool.SetZeroedRange(memStart, systemMemorySize);
            if(GC.gcType != GCType.NullCollector) {
                PageManager.SetStaticDataPages(memStart, systemMemorySize);
#if !SINGULARITY
                PageTable.SetProcess(PageTable.Page(memStart),
                                     PageTable.PageCount(systemMemorySize));
#endif
            }
        }

        [ManualRefCounts]
#if !SINGULARITY
        [NoStackLinkCheck]
#endif
        internal static Object Allocate(VTable vtable) {
            UIntPtr numBytes = ObjectLayout.ObjectSize(vtable);
            UIntPtr objectAddr =
                pool.AllocateFast(numBytes, vtable.baseAlignment);
            VTable.Assert(objectAddr != UIntPtr.Zero,
                          "Out of BootstrapMemory");
            Object result = Magic.fromAddress(objectAddr);
#if REFERENCE_COUNTING_GC
            result.REF_STATE = vtable.isAcyclicRefType ?
                (ReferenceCountingCollector.
                 acyclicFlagMask | 1): 1;
            result.REF_STATE = (result.REF_STATE | 2) &
                ~ReferenceCountingCollector.countingONFlagMask;
#elif DEFERRED_REFERENCE_COUNTING_GC
            result.REF_STATE = vtable.isAcyclicRefType ?
                (DeferredReferenceCountingCollector.
                 acyclicFlagMask |
                 DeferredReferenceCountingCollector.
                 markFlagMask) :
                DeferredReferenceCountingCollector.
                markFlagMask;
            result.REF_STATE &=
                ~DeferredReferenceCountingCollector.countingONFlagMask;
#endif
            *result.VTableFieldAddr = Magic.addressOf(vtable);
            return result;
        }

        [ManualRefCounts]
#if !SINGULARITY
        [NoStackLinkCheck]
#endif
        internal static Object Allocate(VTable vtable, uint count) {
            UIntPtr numBytes = ObjectLayout.ArraySize(vtable, count);
            UIntPtr objectAddr =
                pool.AllocateFast(numBytes, vtable.baseAlignment);
            VTable.Assert(objectAddr != UIntPtr.Zero,
                          "Out of BootstrapMemory");
            Array result = Magic.toArray(Magic.fromAddress(objectAddr));
#if REFERENCE_COUNTING_GC
            result.REF_STATE = vtable.isAcyclicRefType ?
                (ReferenceCountingCollector.
                 acyclicFlagMask | 1): 1;
            result.REF_STATE = (result.REF_STATE | 2) &
                ~ReferenceCountingCollector.countingONFlagMask;
#elif DEFERRED_REFERENCE_COUNTING_GC
            result.REF_STATE = vtable.isAcyclicRefType ?
                (DeferredReferenceCountingCollector.
                 acyclicFlagMask |
                 DeferredReferenceCountingCollector.
                 markFlagMask) :
                DeferredReferenceCountingCollector.
                markFlagMask;
            result.REF_STATE &=
                ~DeferredReferenceCountingCollector.countingONFlagMask;
#endif
            *result.VTableFieldAddr = Magic.addressOf(vtable);
            result.InitializeVectorLength((int) count);
            return result;
        }

        [PreInitRefCounts]
#if !SINGULARITY
        [NoStackLinkCheck]
#endif
        internal static Object Allocate(Type t) {
            return Allocate((RuntimeType) t);
        }

        [PreInitRefCounts]
#if !SINGULARITY
        [NoStackLinkCheck]
#endif
        internal static Object Allocate(Type t, uint count) {
            return Allocate((RuntimeType) t, count);
        }

        [PreInitRefCounts]
#if !SINGULARITY
        [NoStackLinkCheck]
#endif
        internal static Object Allocate(RuntimeType t) {
            return Allocate(t.classVtable);
        }

        [PreInitRefCounts]
#if !SINGULARITY
        [NoStackLinkCheck]
#endif
        internal static Object Allocate(RuntimeType t, uint count) {
            return Allocate(t.classVtable, count);
        }

        internal static void Truncate() {
            UIntPtr allocLimit = PageTable.PagePad(pool.AllocPtr);
            UIntPtr unusedSize = pool.ReserveLimit - allocLimit;
            if(GC.gcType != GCType.NullCollector) {
                PageManager.ReleaseUnusedPages(PageTable.Page(allocLimit),
                                               PageTable.PageCount(unusedSize),
                                               true);
            }
            pool.Truncate();
        }

    }

}
