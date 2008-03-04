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

    using Microsoft.Bartok.Options;
    using Microsoft.Bartok.Runtime;

    using System;
    using System.Threading;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

    /// <summary>
    /// This class defines a queue that is created as a linked list from
    /// each thread object through pointers stored in object headers.
    ///
    /// This queue is used for the concurrent mark sweep collector to allow
    /// it to trace through the heap without ever requiring allocation
    /// or locking.
    ///
    /// Within a thread this is a FIFO queue; all mutations are made at the
    /// head.
    /// </summary>
    internal struct ThreadHeaderQueue {

        [StructLayout(LayoutKind.Sequential)]
        [MixinConditional("ConcurrentMSGC")]
        [Mixin(typeof(PreHeader))]
        private struct PreHeaderQueue {
            internal MultiUseWord muw;
            internal UIntPtr link;
        }

        [MixinConditional("ConcurrentMSGC")]
        [Mixin(typeof(Object))]
        private class ThreadHeaderQueueObject : System.Object {
            internal new PreHeaderQueue preHeader;
        }

        [Inline]
        private static ThreadHeaderQueueObject MixinObject(Object obj) {
            return (ThreadHeaderQueueObject) obj;
        }

        [MixinConditional("ConcurrentMSGC")]
        [MixinConditional("AllThreadMixins")]
        [Mixin(typeof(Thread))]
        private class ThreadHeaderQueueThread : Object {
            [AccessedByRuntime("referenced in brtforgc.asm")]
            internal ThreadHeaderQueue gcQueue;
        }

        [Inline]
        private static ThreadHeaderQueueThread MixinThread(Thread t) {
            return (ThreadHeaderQueueThread) (Object) t;
        }
        
        /// <summary>
        /// Contains the pointer to an object that a thread is trying
        /// to insert.
        /// </summary>
        internal UIntPtr newHead;

        /// <summary>
        /// Contains the pointer to the first object in the queue, or Zero
        /// if the queue is empty.
        /// </summary>
        internal UIntPtr head;

        /// <summary>
        /// Contains a pointer to the object that was at the head of the
        /// queue the last time it was 'stolen' by a consuming thread.
        ///
        /// If this matches the current head value then there is nothing
        /// on the queue that has not been stolen.
        /// </summary>
        internal UIntPtr stolenHead;

        // Contains a count of nodes that have been stolen
        internal static long stolenCount;

        internal static UIntPtr TAIL_MARKER {
          get { return ~(UIntPtr)3U; }
        }
        
        /// <summary>
        /// Reset the queue.
        /// </summary>
        [Inline]
        internal static void Reset(Thread t) {
            MixinThread(t).gcQueue.Reset();
        }

        [Inline]
        internal void Reset()
        {
            this.head = TAIL_MARKER;
            this.newHead = TAIL_MARKER;
            this.stolenHead = TAIL_MARKER;
        }

        /// <summary>
        /// Is the queue empty?
        /// </summary>
        internal static bool IsEmpty(Thread t) {
            return MixinThread(t).gcQueue.IsEmpty();
        }

        private bool IsEmpty() {
            return (this.head == this.stolenHead);
        }

        [Inline]
        private static UIntPtr QueueField(Object obj)
        {
            return MixinObject(obj).preHeader.link;
        }

        [Inline]
        private static void SetQueueField(Object obj, UIntPtr value)
        {
            MixinObject(obj).preHeader.link = value;
        }

        private static bool ExchangeQueueField(Object obj,
                                               UIntPtr val,
                                               UIntPtr oldVal)
        {
            ThreadHeaderQueueObject obj2 = MixinObject(obj);
            return Interlocked.CompareExchange(ref obj2.preHeader.link,
                                               val, oldVal) == oldVal;
        }

        /// <summary>
        /// Get the mark value from the header word of the object.
        /// </summary>
        [Inline]
        internal static UIntPtr GcMark(Object obj)
        {
            VTable.Assert(obj != null, "Can not get mark of null");
            return (QueueField(obj) & (UIntPtr)3U);
        }

        /// <summary>
        /// Sets the mark value in the header word of the object.
        /// </summary>
        [Inline]
        internal static void SetGcMark(UIntPtr objAddr, UIntPtr markBits)
        {
            SetGcMark(Magic.fromAddress(objAddr), markBits);
        }

        [Inline]
        internal static void SetGcMark(Object obj, UIntPtr markBits)
        {
            SetQueueField(obj, markBits);
        }

        internal static bool IsInQueue(Object obj) {
            return (QueueField(obj) == UIntPtr.Zero);
        }

        /// <summary>
        /// Link a new value at the head of the queue.  It is assumed that
        /// if the object is unmarked, the value in the header word will
        /// simply be 'unmarkedColor'.  Returns true if the object was
        /// marked an linked into the queue.
        /// </summary>
        [Inline]
        internal static bool Push(Thread t,
                                  UIntPtr objAddr,
                                  UIntPtr markedColor,
                                  UIntPtr unmarkedColor)
        {
            return MixinThread(t).gcQueue.Push(objAddr,
                                               markedColor,
                                               unmarkedColor);
        }

        [Inline]
        private bool Push(UIntPtr objAddr,
                          UIntPtr markedColor,
                          UIntPtr unmarkedColor)
        {
            VTable.Assert(objAddr != UIntPtr.Zero, "Can not push null!");
            this.newHead = objAddr;
            Object obj = Magic.fromAddress(objAddr);
            if (ExchangeQueueField(obj, this.head + markedColor,
                                   unmarkedColor)) {
                this.head = objAddr;
                return true;
            } else {
                // Someone else enqueued the object, so restore this queue
                this.newHead = this.head;
                return false;
            }
        }

        /// <summary>
        /// Link a new value at the head of the queue, knowing that the
        /// object is a thread local object.
        /// </summary>
        [Inline]
        internal static void PushUnsafe(Thread t,
                                        Object obj,
                                        UIntPtr markBits)
        {
            MixinThread(t).gcQueue.PushUnsafe(obj, markBits);
        }

        [Inline]
        private void PushUnsafe(Object obj, UIntPtr markBits)
        {
            VTable.Assert(obj != null, "Can not push null!");
            SetQueueField(obj, this.head + markBits);
            this.newHead = Magic.addressOf(obj);
            this.head = this.newHead;
        }

        /// <summary>
        /// Unlink a value from the head of the queue. This
        /// method is not thread safe.  The method is supposed to be
        /// called only from the collector thread.
        /// </summary>
        [Inline]
        internal static Object Pop(Thread thread, UIntPtr markedColor) {
            return MixinThread(thread).gcQueue.Pop(markedColor);
        }

        [Inline]
        private Object Pop(UIntPtr markedColor)
        {
            VTable.Assert(!this.IsEmpty(), "Queue is empty!");
            Object obj = Magic.fromAddress(this.head);
            this.head = QueueField(obj) & ~(UIntPtr)3U;
            SetQueueField(obj, markedColor);
            return obj;
        }

        /// <summary>
        /// Returns the number of values in the list.  It is tolerant
        /// of concurrent additions, although additions after the initial
        /// read of the list head will not be counted.  It is tolerant
        /// of concurrent steals from the list, although the count may
        /// be truncated if the list is stolen from during the count.
        /// </summary>
        internal int Count {
            get {
                UIntPtr cachedStolenHead = this.stolenHead;
                if (this.head == cachedStolenHead) {
                    return 0;
                }
                Object obj = Magic.fromAddress(this.head);
                UIntPtr next = QueueField(obj) & ~(UIntPtr)3U;
                int listLength = 1;
                while (next != cachedStolenHead &&
                       this.stolenHead == cachedStolenHead) {
                    listLength++;
                    obj = Magic.fromAddress(next);
                    next = QueueField(obj) & ~(UIntPtr)3U;
                }
                return listLength;
            }
        }

        internal static void DeadThreadNotification(Thread deadThread,
                                                    UIntPtr markedColor)
        {
            StealSafe(Thread.CurrentThread, deadThread, markedColor);
        }

        /// <summary>
        /// This method attempts to take values from the passed-in queue
        /// and place it in the 'this' queue.  It assumes that the 'this'
        /// queue is not concurrently added to.  However, the method is
        /// tolerant of concurrent attempts to steal from the fromQueue.
        ///
        /// Rather than reading the old mark value from the header word
        /// of the tail object in the 'fromQueue', the new mark value in
        /// said object is going to be 'markedColor'
        ///
        /// If any values are stolen the method returns true.
        /// </summary>
        internal static bool Steal(Thread toThread,
                                   Thread fromThread,
                                   UIntPtr markedColor)
        {
            ThreadHeaderQueueThread myToThread = MixinThread(toThread);
            ThreadHeaderQueueThread myFromThread = MixinThread(fromThread);
            return myToThread.gcQueue.StealFrom(ref myFromThread.gcQueue,
                                                markedColor);
        }

        private bool StealFrom(ref ThreadHeaderQueue fromQueue,
                                UIntPtr markedColor)
        {
            UIntPtr fromHead, fromTail;
            if (fromQueue.Steal(out fromHead, out fromTail)) {
                // Prepend the stolen list segment to our list
                this.newHead = fromHead;
                Object tailObject = Magic.fromAddress(fromTail);
                SetQueueField(tailObject, this.head + markedColor);
                this.head = fromHead;
                return true;
            } else {
                return false;
            }
        }

        /// <summary>
        /// This method attempts to take values from the passed-in queue
        /// and place it in the 'this' queue.  The method is tolerant of
        /// concurrent attempts to add to the 'this' queue and is
        /// tolerant of concurrent attempts to steal from the fromQueue.
        ///
        /// Rather than reading the old mark value from the header word
        /// of the tail object in the 'fromQueue', the new mark value in
        /// said object is going to be 'markedColor'
        ///
        /// If any values are stolen the method returns true.
        /// </summary>
        internal static void StealSafe(Thread toThread,
                                       Thread fromThread,
                                       UIntPtr markedColor)
        {
            ThreadHeaderQueueThread myToThread = MixinThread(toThread);
            ThreadHeaderQueueThread myFromThread = MixinThread(fromThread);
            myToThread.gcQueue.StealFromSafe(ref myFromThread.gcQueue,
                                             markedColor);
        }

        private void StealFromSafe(ref ThreadHeaderQueue fromQueue,
                                   UIntPtr markedColor)
        {
            UIntPtr fromHead, fromTail;
            this.newHead = fromQueue.head;
            if (fromQueue.Steal(out fromHead, out fromTail)) {
                VTable.Assert(this.newHead == fromHead);
                // Prepend the stolen list segment to our list
                UIntPtr thisHead = this.head;
                while (Interlocked.CompareExchange(ref this.newHead,
                                                   fromHead, thisHead) !=
                       thisHead) {
                    thisHead = this.head;
                }
                // We have won exclusive rights to insert into 'this'.
                Object tailObject = Magic.fromAddress(fromTail);
                SetQueueField(tailObject, thisHead + markedColor);
                this.head = fromHead;
            } else {
                this.newHead = this.head;
            }
        }

        private bool Steal(out UIntPtr stolenHead, out UIntPtr stolenTail)
        {
            while (true) {
                UIntPtr thisStolen = this.stolenHead;
                UIntPtr thisHead = this.head;
                while (thisStolen != thisHead) {
                    if (Interlocked.CompareExchange(ref this.stolenHead,
                                                    thisHead, thisStolen) ==
                        thisStolen) {
                        // We managed to steal part of the list.
                        // Find the end of the stolen list segment.
                        Object obj = Magic.fromAddress(thisHead);
                        UIntPtr next =
                            QueueField(obj) & ~(UIntPtr)3U;
                        int listLength = 0;
                        while (next != thisStolen) {
                            listLength++;
                            obj = Magic.fromAddress(next);
                            next = QueueField(obj) & ~(UIntPtr)3U;
                        }
                        stolenCount += listLength;
                        stolenHead = thisHead;
                        stolenTail = Magic.addressOf(obj);
                        return true;
                    }
                    thisStolen = this.stolenHead;
                    thisHead = this.head;
                }
                if (this.newHead == thisHead) {
                    // There is nothing to steal.
                    stolenHead = UIntPtr.Zero;
                    stolenTail = UIntPtr.Zero;
                    return false;
                }
                // Someone must be in the process of inserting something
                // into the queue.
                Thread.Yield();
            }
        }

    }

}
