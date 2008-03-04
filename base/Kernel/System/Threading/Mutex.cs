////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Mutex.cs
//
//  Note:
//

using System;
using System.Threading;
using System.Runtime.CompilerServices;
using Microsoft.Singularity;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Scheduling;

namespace System.Threading
{
    [CLSCompliant(false)]
    public enum MutexEvent
    {
        AcquireAgain = 1,
        Acquire = 2,
        Enqueue = 3,
    }

    //| <include path='docs/doc[@for="Mutex"]/*' />
    [NoCCtor]
    [CLSCompliant(false)]
    public sealed class Mutex : WaitHandle
    {
        private int acquired = 0;   // Number of times acquired by same thread.

        //| <include path='docs/doc[@for="Mutex.Mutex2"]/*' />
        public Mutex(bool initiallyOwned)
            : base(initiallyOwned ? 0 : 1)
        {
            if (initiallyOwned) {
                owner = Thread.CurrentThread;
                acquired = 1;
            }
        }

        //| <include path='docs/doc[@for="Mutex.Mutex3"]/*' />
        public Mutex()
            : base(1)
        {
        }

        public bool AcquireMutex()
        {
            return WaitOne();
        }

        public bool AcquireMutex(SchedulerTime stop)
        {
            return WaitOne(stop);
        }

        //| <include path='docs/doc[@for="Mutex.ReleaseMutex"]/*' />
        public void ReleaseMutex()
        {
#if DEBUG_DISPATCH
            DebugStub.Print("Mutex:ReleaseMutex 001\n");
#endif // DEBUG_DISPATCH
            if (Thread.CurrentThread != owner) {
                VTable.Assert(Thread.CurrentThread == owner);
                return;
            }

            bool iflag = Processor.DisableInterrupts();
            try {
                Scheduler.DispatchLock();
                try {
                    VTable.Assert(acquired >= 0);

                    acquired--;
                    if (acquired == 0) {
#if DEBUG_DISPATCH
                        DebugStub.Print("Mutex:ReleaseMutex 002\n");
#endif // DEBUG_DISPATCH
                        if (NotifyOne()) {
#if DEBUG_DISPATCH
                            DebugStub.Print("Mutex:ReleaseMutex 003\n");
#endif // DEBUG_DISPATCH
                            signaled = 0;
                            acquired = 1;
                        }
                        else {
                            signaled = 1;
                            owner = null;
                        }
                    }
                }
                finally {
                    Scheduler.DispatchRelease();
                }
            }
            finally {
                Processor.RestoreInterrupts(iflag);
            }
        }

        // Called by monitor to see if its lock is held by the current thread.
        internal bool IsOwnedByCurrentThread()
        {
            return (Thread.CurrentThread == owner);
        }

        // Called with dispatch lock held and interrupts off.
        // Returns true if the mutex was acquired.
        internal override bool AcquireOrEnqueue(ThreadEntry entry)
        {
            if (owner == entry.Thread) {
#if DEBUG_DISPATCH
                DebugStub.Print("Mutex:AcquireOrEnqueue 001\n");
#endif // DEBUG_DISPATCH
                acquired++;
                Monitoring.Log(Monitoring.Provider.Mutex,
                               (ushort)MutexEvent.AcquireAgain, 0,
                               (uint)this.id, (uint)entry.Thread.threadIndex,
                               0, 0, 0);
                return true;
            }
            else if (signaled != 0) {
#if DEBUG_DISPATCH
                DebugStub.Print("Mutex:AcquireOrEnqueue 002\n");
#endif // DEBUG_DISPATCH
                signaled = 0;
                owner = entry.Thread;
                acquired = 1;
                Monitoring.Log(Monitoring.Provider.Mutex,
                               (ushort)MutexEvent.Acquire, 0,
                               (uint)this.id, (uint)entry.Thread.threadIndex,
                               0, 0, 0);
                return true;
            }
            else {
#if DEBUG_DISPATCH
                DebugStub.Print("Mutex:AcquireOrEnqueue 003\n");
#endif // DEBUG_DISPATCH
                queue.EnqueueTail(entry);
                Monitoring.Log(Monitoring.Provider.Mutex,
                               (ushort)MutexEvent.Enqueue, 0,
                               (uint)this.id, (uint)entry.Thread.threadIndex,
                               0, 0, 0);
                return false;
            }
        }

        // Return mutex owner.
        [NoHeapAllocation]
        internal override Thread GetBeneficiary()
        {
            return owner;
        }
    }
}
