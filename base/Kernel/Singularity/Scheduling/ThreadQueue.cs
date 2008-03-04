////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ThreadQueue.cs
//
//  Note:
//

// #define DEBUG_SCHEDULER

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Singularity;

namespace Microsoft.Singularity.Scheduling
{
    // This class is designed to support queues whose enqueue,
    // dequeue, and remove operations do not allocate memory.
    // This feature is useful when writing code that needs to
    // do such operations with interrupts off.
    [CLSCompliant(false)]
    [AccessedByRuntime("referenced from c++")]
    public class ThreadQueue
    {
        private ThreadEntry head = null;
        private ThreadEntry tail = null;
        private WaitHandle handle = null;

        public ThreadQueue()
        {
        }

        public ThreadQueue(WaitHandle handle)
        {
            this.handle = handle;
        }

        public WaitHandle Handle {
            [NoHeapAllocation]
            get { return handle; }
        }

        public ThreadEntry Head
        {
            [NoHeapAllocation]
            get { return head; }
        }

        [NoHeapAllocation]
        public bool IsEnqueued(ThreadEntry entry)
        {
            return (entry.queue == this);
        }

        [NoHeapAllocation]
        public void EnqueueTail(ThreadEntry entry)
        {
            Scheduler.AssertDispatchLockHeld();
            VTable.Assert(entry.next == null);
            VTable.Assert(entry.prev == null);
            VTable.Assert(entry.queue == null);

            entry.queue = this;
            entry.prev = tail;

            if (tail != null) {
                VTable.Assert(tail.next == null);
                tail.next = entry;
            }
            else {
                VTable.Assert(head == null);
                head = entry;
            }

            tail = entry;
        }

        [NoHeapAllocation]
        public void EnqueueHead(ThreadEntry entry)
        {
            Scheduler.AssertDispatchLockHeld();
            VTable.Assert(entry.next == null);
            VTable.Assert(entry.prev == null);
            VTable.Assert(entry.queue == null);

            entry.queue = this;
            entry.next = head;

            if (head != null) {
                VTable.Assert(head.prev == null);
                head.prev = entry;
            }
            else {
                VTable.Assert(tail == null);
                tail = entry;
            }

            head = entry;
        }

        [NoHeapAllocation]
        public void InsertBefore(ThreadEntry position, ThreadEntry entry)
        {
            Scheduler.AssertDispatchLockHeld();
            if (position == null) {
                EnqueueTail(entry);
            }
            else if (position == head) {
                EnqueueHead(entry);
            }
            else {
                VTable.Assert(head != null);
                VTable.Assert(entry.queue == null);

                entry.queue = this;
                entry.prev = position.prev;
                entry.next = position;
                position.prev = entry;
                entry.prev.next = entry;
            }
        }

        [NoHeapAllocation]
        public Thread DequeueHeadThread()
        {
            Scheduler.AssertDispatchLockHeld();
            ThreadEntry entry = DequeueHead();
            if (entry != null) {
                return entry.Thread;
            }
            return null;
        }

        [NoHeapAllocation]
        public Thread DequeueTailThread()
        {
            Scheduler.AssertDispatchLockHeld();
            ThreadEntry entry = DequeueTail();
            if (entry != null) {
                return entry.Thread;
            }
            return null;
        }

        [NoHeapAllocation]
        public ThreadEntry DequeueHead()
        {
            Scheduler.AssertDispatchLockHeld();
            ThreadEntry entry = head;

            if (entry != null) {
                Remove(entry);
                return entry;
            }
            else {
                return null;
            }
        }

        [NoHeapAllocation]
        public ThreadEntry DequeueTail()
        {
            Scheduler.AssertDispatchLockHeld();
            ThreadEntry entry = tail;

            if (entry != null) {
                Remove(entry);
                return entry;
            }
            else {
                return null;
            }
        }

        [NoHeapAllocation]
        public void Remove(ThreadEntry entry)
        {
            Scheduler.AssertDispatchLockHeld();
            VTable.Assert(entry.queue == this);

            if (entry.next != null) {
                entry.next.prev = entry.prev;
            }
            else {
                VTable.Assert(entry == tail);
                tail = entry.prev;
            }

            if (entry.prev != null) {
                entry.prev.next = entry.next;
            }
            else {
                VTable.Assert(entry == head);
                head = entry.next;
            }

            entry.next = null;
            entry.prev = null;
            entry.queue = null;
        }

        [NoHeapAllocation]
        public bool IsEmpty()
        {
#if false
            Scheduler.AssertDispatchLockHeld();
#endif
            return (head == null);
        }
    }
}
