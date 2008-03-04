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

    internal unsafe class WriteBarrierCMS : UniversalWriteBarrier
    {

        internal static WriteBarrierCMS instance;

        [PreInitRefCounts]
        internal static new void Initialize() {
            WriteBarrierCMS.instance = (WriteBarrierCMS)
                BootstrapMemory.Allocate(typeof(WriteBarrierCMS));
        }

        [Inline]
        protected override Object AtomicSwapImpl(ref Object reference,
                                                 Object value)
        {
            ReferenceCheck(Magic.toPointer(ref reference), value);
            UIntPtr resultAddr =
                Interlocked.Exchange(Magic.toPointer(ref reference),
                                     Magic.addressOf(value));
            return Magic.fromAddress(resultAddr);
        }

        [Inline]
        protected override
        Object AtomicCompareAndSwapImpl(ref Object reference,
                                        Object newValue,
                                        Object comparand)
        {
            ReferenceCheck(Magic.toPointer(ref reference), newValue);
            UIntPtr resultAddr =
                Interlocked.CompareExchange(Magic.toPointer(ref reference),
                                            Magic.addressOf(newValue),
                                            Magic.addressOf(comparand));
            return Magic.fromAddress(resultAddr);
        }

        [Inline]
        protected override void CloneImpl(Object srcObject, Object dstObject)
        {
            // There is no need to keep track of initial writes, so do nothing!
            CloneNoBarrier(srcObject, dstObject);
        }

        [Inline]
        protected override void WriteReferenceImpl(UIntPtr *location,
                                                   Object value)
        {
            ReferenceCheck(location, value);
            *location = Magic.addressOf(value);
        }

        /// <summary>
        /// In the sliding views phase, where some threads may have
        /// scanned their roots and others have not, we need to ensure
        /// that both old and new values will be marked and scanned.
        /// In the tracing phase we only need to ensure that the old
        /// values are traced and marked, as the old values may be the
        /// only references to a part of the snapshot reachable object
        /// graph from the untraced part of the object graph.
        /// </summary>
        /// <param name="addr">The memory location being modified</param>
        /// <param name="value">The reference value to be written into
        /// the "addr" location</param>
        [Inline]
        private static void ReferenceCheck(UIntPtr *addr, Object value)
        {
#if !SINGULARITY || CONCURRENT_MS_COLLECTOR
            if (ConcurrentMSCollector.CurrentMarkingPhase ==
                ConcurrentMSCollector.MarkingPhase.ComputingRoots) {
                UIntPtr oldValue = *addr;
                MarkIfNecessary(oldValue);
                MarkIfNecessary(Magic.addressOf(value));
            } else if (ConcurrentMSCollector.CurrentMarkingPhase ==
                       ConcurrentMSCollector.MarkingPhase.Tracing) {
                UIntPtr oldValue = *addr;
                MarkIfNecessary(oldValue);
            }
#endif // CONCURRENT_MS_COLLECTOR
        }

        /// <summary>
        /// Ensures that a reference value is going to be marked and
        /// scanned.
        /// </summary>
        /// <param name="value">The reference value that may need to
        /// be marked</param>
        [RequiredByBartok]
        private static void MarkIfNecessary(UIntPtr value) {
#if !SINGULARITY || CONCURRENT_MS_COLLECTOR
            if (PageTable.IsGcPage(PageTable.Page(value)) &&
                (ThreadHeaderQueue.GcMark(Magic.fromAddress(value)) !=
                 ConcurrentMSCollector.markedColor)) {
                VTable.Assert(PageTable.IsMyPage(PageTable.Page(value)));
                Thread thread = Thread.CurrentThread;
                ThreadHeaderQueue.Push(thread,
                                       value,
                                       ConcurrentMSCollector.markedColor,
                                       ConcurrentMSCollector.unmarkedColor);
            }
#endif // CONCURRENT_MS_COLLECTOR
        }

        internal static void EnterSnoopingPhase() {
            WriteBarrierCMS.isSnooping = true;
        }

        internal static void LeaveSnoopingPhase() {
            WriteBarrierCMS.isSnooping = false;
        }

        internal static bool InSnoopingPhase {
            get {
                return WriteBarrierCMS.isSnooping;
            }
        }

        /// <summary>
        /// Ensures that an object is going to be marked and scanned.
        /// </summary>
        /// <param name="obj">The object that may need to be marked</param>
        internal static void MarkObject(Object obj) {
#if !SINGULARITY || CONCURRENT_MS_COLLECTOR
            MarkIfNecessary(Magic.addressOf(obj));
#endif // CONCURRENT_MS_COLLECTOR
        }

        private static bool isSnooping;

    }

}
