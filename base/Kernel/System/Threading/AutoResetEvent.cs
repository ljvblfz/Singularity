////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   AutoResetEvent.cs
//
//  Note:
//

using System;
using System.Runtime.CompilerServices;
using Microsoft.Singularity;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Scheduling;

namespace System.Threading
{
    [CLSCompliant(false)]
    public enum AutoResetEventEvent : ushort
    {
        Acquire = 1,
        Enqueue = 2
    }

    //| <include path='docs/doc[@for="AutoResetEvent"]/*' />
    [NoCCtor]
    [CLSCompliant(false)]
    public sealed class AutoResetEvent : WaitHandle
    {
        //| <include path='docs/doc[@for="AutoResetEvent.AutoResetEvent"]/*' />
        public AutoResetEvent(bool initialState) :
            base(initialState ? 1 : 0)
        {
        }

        //| <include path='docs/doc[@for="AutoResetEvent.Reset"]/*' />
        [NoHeapAllocation]
        public bool Reset()
        {
            bool iflag = Processor.DisableInterrupts();
            try {
                Scheduler.DispatchLock();
                try {
#if DEBUG_DISPATCH
                    DebugStub.Print("Thread {0:x8}  AutoResetEvent.Reset() on {1:x8}\n",
                                    __arglist(
                                        Kernel.AddressOf(Thread.CurrentThread),
                                        Kernel.AddressOf(this)));
#endif // DEBUG_DISPATCH
                    signaled = 0;
                }
                finally {
                    Scheduler.DispatchRelease();
                }
            }
            finally {
                Processor.RestoreInterrupts(iflag);
            }
            return true;
        }

        //| <include path='docs/doc[@for="AutoResetEvent.Set"]/*' />
        [NoHeapAllocation]
        public bool Set()
        {
            bool iflag = Processor.DisableInterrupts();
            try {
                Scheduler.DispatchLock();
                try {
                    if (NotifyOne()) {
#if DEBUG_DISPATCH
                        DebugStub.Print("Thread {0:x8} AutoResetEvent.Set() on {1:x8}" +
                                        "unblocked  {2:x8}\n",
                                        __arglist(
                                            Kernel.AddressOf(Thread.CurrentThread),
                                            Kernel.AddressOf(this),
                                            Kernel.AddressOf(owner)));
#endif // DEBUG_DISPATCH
                        signaled = 0;
                    }
                    else {
#if DEBUG_DISPATCH
                        DebugStub.Print("Thread {0:x8} AutoResetEvent.Set() on {1:x8} ready\n",
                                        __arglist(
                                            Kernel.AddressOf(Thread.CurrentThread),
                                            Kernel.AddressOf(this)));
#endif // DEBUG_DISPATCH
                        signaled = 1;
                    }
                }
                finally {
                    Scheduler.DispatchRelease();
                }
            }
            finally {
                Processor.RestoreInterrupts(iflag);
            }
            return true;
        }

        //| <include path='docs/doc[@for="AutoResetEvent.Set"]/*' />
        public bool SetAll()
        {
            bool iflag = Processor.DisableInterrupts();
            try {
                Scheduler.DispatchLock();
                try {
                    if (NotifyAll()) {
                        signaled = 0;
                    }
                    else {
                        signaled = 1;
                    }
                }
                finally {
                    Scheduler.DispatchRelease();
                }
            }
            finally {
                Processor.RestoreInterrupts(iflag);
            }
            return true;
        }

        // Called with dispatch lock held and interrupts off.
        // Returns true if the AutoResetEvent was signaled.
        internal override bool AcquireOrEnqueue(ThreadEntry entry)
        {
            if (signaled != 0) {
#if DEBUG_DISPATCH
                DebugStub.Print("Thread {0:x8} AutoResetEvent.Acquire on {1:x8}\n",
                                __arglist(
                                    Kernel.AddressOf(Thread.CurrentThread),
                                    Kernel.AddressOf(this)));
#endif // DEBUG_DISPATCH
                signaled = 0;
                Monitoring.Log(Monitoring.Provider.AutoResetEvent,
                               (ushort)AutoResetEventEvent.Acquire, 0,
                               (uint)this.id, (uint)entry.Thread.threadIndex,
                               0, 0, 0);
                return true;
            }
            else {
#if DEBUG_DISPATCH
                DebugStub.Print("Thread {0:x8} AutoResetEvent.Enqueue on {1:x8}\n",
                                __arglist(
                                    Kernel.AddressOf(Thread.CurrentThread),
                                    Kernel.AddressOf(this)));
#endif // DEBUG_DISPATCH

                queue.EnqueueTail(entry);
                Monitoring.Log(Monitoring.Provider.AutoResetEvent,
                               (ushort)AutoResetEventEvent.Enqueue, 0,
                               (uint)this.id, (uint)entry.Thread.threadIndex,
                               0, 0, 0);
                return false;
            }
        }

        // Return thread who could use our priority.
        [NoHeapAllocation]
        internal override Thread GetBeneficiary()
        {
            return null;
        }
    }
}
