// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==

namespace System.Threading {
    using System;
    using System.Threading;
    using System.Runtime.CompilerServices;
    using System.Collections;
    using Microsoft.Singularity;
    using Microsoft.Singularity.Io;
    using Microsoft.Singularity.Scheduling;

    [CLSCompliant(false)]
    public enum WaitHandleEvent : ushort
    {
        WaitDone = 10,
    }

    //| <include path='docs/doc[@for="WaitHandle"]/*' />
    [NoCCtor]
    [CLSCompliant(false)]
    public abstract class WaitHandle
    {
        // These two fields should only be accessed with interrupts off.
        protected volatile int signaled;
        protected volatile Thread owner;     // Last thread to be notified by this signal.
        protected ThreadQueue queue;
        protected int id;                     // unique ID for this waithandle
        private static int idGenerator;

        // This field is an array of length 1 containing 'this'.
        // It is used to avoid allocation when calling WaitAny from WaitOne.
        internal WaitHandle[] singleHandle;

        //| <include path='docs/doc[@for="WaitHandle.WaitTimeout"]/*' />
        public const int WaitTimeout = -1;

        //| <include path='docs/doc[@for="WaitHandle.WaitHandle"]/*' />
        protected WaitHandle(int initialState)
        {
            id = ++idGenerator;
            owner = null;
            queue = new ThreadQueue(this);
            signaled = initialState;
            singleHandle = new WaitHandle [1] { this };
        }

        // Called with dispatch lock held and interrupts off.
        [NoHeapAllocation]
        protected bool NotifyOne()
        {
            Kernel.Waypoint(201);

            ThreadEntry entry = queue.DequeueHead();
            if (entry != null) {
                //Kernel.Waypoint(202);
                owner = entry.Thread;
                Monitoring.Log(Monitoring.Provider.WaitHandle,
                               (ushort)WaitHandleEvent.WaitDone, 0,
                               (uint)this.id, (uint)owner.threadIndex,
                               0, 0, 0);
                entry.Thread.WaitDone(entry);
                //Kernel.Waypoint(203);
                return true;
            }
            return false;
        }

        // Called with dispatch lock held and interrupts off.
        [NoHeapAllocation]
        protected bool NotifyAll()
        {
            Kernel.Waypoint(204);
            bool notified = false;

            for (ThreadEntry entry; (entry = queue.DequeueHead()) != null;) {
                //Kernel.Waypoint(205);
                owner = entry.Thread;
                Monitoring.Log(Monitoring.Provider.WaitHandle,
                               (ushort)WaitHandleEvent.WaitDone, 0,
                               (uint)this.id, (uint)owner.threadIndex,
                               0, 0, 0);
                entry.Thread.WaitDone(entry);
                //Kernel.Waypoint(206);
                notified = true;
            }
            //Kernel.Waypoint(207);
            return notified;
        }

        // Called with interrupts off.
        internal abstract bool AcquireOrEnqueue(ThreadEntry entry);

        /// Used to return a thread which can make progress on this object.
        [NoHeapAllocation]
        internal abstract Thread GetBeneficiary();

        //| <include path='docs/doc[@for="WaitHandle.WaitOne"]/*' />
        public bool WaitOne(TimeSpan timeout)
        {
            return Thread.CurrentThread.WaitAny(singleHandle, 1, timeout) != WaitTimeout;
        }

        //| <include path='docs/doc[@for="WaitHandle.WaitOne1"]/*' />
        internal bool WaitOne(SchedulerTime stop)
        {
            return Thread.CurrentThread.WaitAny(singleHandle, 1, stop) != WaitTimeout;
        }

        //| <include path='docs/doc[@for="WaitHandle.WaitOne2"]/*' />
        public bool WaitOne()
        {
            return WaitOne(SchedulerTime.MaxValue);
        }

        //| <include path='docs/doc[@for="WaitHandle.WaitAny"]/*' />
        public static int WaitAny(WaitHandle[] waitHandles,
                                  TimeSpan timeout)
        {
            return Thread.CurrentThread.WaitAny(waitHandles, waitHandles.Length, timeout);
        }

        //| <include path='docs/doc[@for="WaitHandle.WaitAny"]/*' />
        public static int WaitAny(WaitHandle[] waitHandles,
                                  SchedulerTime stop)
        {
            return Thread.CurrentThread.WaitAny(waitHandles, waitHandles.Length, stop);
        }

        //| <include path='docs/doc[@for="WaitHandle.WaitAny"]/*' />
        public static int WaitAny(WaitHandle[] waitHandles,
                                  int waitHandlesCount,
                                  SchedulerTime stop)
        {
            return Thread.CurrentThread.WaitAny(waitHandles, waitHandlesCount, stop);
        }

        //| <include path='docs/doc[@for="WaitHandle.WaitAny2"]/*' />
        public static int WaitAny(WaitHandle[] waitHandles)
        {
            return WaitAny(waitHandles, SchedulerTime.MaxValue);
        }
    }
}
