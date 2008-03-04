////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Thread.cs
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
    public enum ManualResetEventEvent : ushort
    {
        Acquire = 1,
        Enqueue = 2
    }

    //| <include path='docs/doc[@for="ManualResetEvent"]/*' />
    [NoCCtor]
    [CLSCompliant(false)]
    public sealed class ManualResetEvent : WaitHandle
    {
        //| <include path='docs/doc[@for="ManualResetEvent.ManualResetEvent"]/*' />
        public ManualResetEvent(bool initialState)
            : base(initialState ? 1 : 0)
        {
        }

        //| <include path='docs/doc[@for="ManualResetEvent.Reset"]/*' />
        public bool Reset()
        {
            bool iflag = Processor.DisableInterrupts();
            try {
                Scheduler.DispatchLock();
                try {
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

        //| <include path='docs/doc[@for="ManualResetEvent.Set"]/*' />
        public bool Set()
        {
            bool iflag = Processor.DisableInterrupts();
            try {
                Scheduler.DispatchLock();
                try {
                    signaled = 1;
                    NotifyAll();
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
        // Returns true if the ManuelResetEvent was signaled.
        internal override bool AcquireOrEnqueue(ThreadEntry entry)
        {
#if DEBUG_DISPATCH
            DebugStub.Print("ManualResetEvent:AcquireOrEnqueue 001\n");
#endif // DEBUG_DISPATCH

            if (signaled > 0) {
                Monitoring.Log(Monitoring.Provider.ManualResetEvent,
                               (ushort)ManualResetEventEvent.Acquire, 0,
                               (uint)this.id, (uint)entry.Thread.threadIndex,
                               0, 0, 0);
                return true;
            }
            else {
                queue.EnqueueTail(entry);
                Monitoring.Log(Monitoring.Provider.ManualResetEvent,
                               (ushort)ManualResetEventEvent.Enqueue, 0,
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
