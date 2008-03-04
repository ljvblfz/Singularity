//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace System.Threading {

    using Microsoft.Singularity;

    using System.Runtime.CompilerServices;
    using System;

    [NoCCtor]
    [CLSCompliant(false)]
    public struct SpinLock
    {
        [NoHeapAllocation]
        public void Acquire()
        {
            Acquire(Thread.CurrentThread);
        }

        // Note: any changes to this code must be reflected in
        // the Release code in SwitchToThreadContext in halidt.asm.
        [NoHeapAllocation]
        public void Release()
        {
            Release(Thread.CurrentThread);
        }

        [NoHeapAllocation]
        public void Acquire(Thread thread)
        {
#if TRACE
            Tracing.Log(Tracing.Debug, "SpinLock.Acquire({0:x8},{1})",
                        VTable.addressOf(thread),
                        (UIntPtr)unchecked((uint)(thread != null ? thread.threadIndex : -1)));
#endif
            AcquireInternal(thread.GetThreadId() + 1);
        }

        // Note: any changes to this code must be reflected in
        // the Release code in SwitchToThreadContext in halidt.asm.
        [NoHeapAllocation]
        public void Release(Thread thread)
        {
#if TRACE
            Tracing.Log(Tracing.Debug, "SpinLock.Release({0:x8},{1})",
                        VTable.addressOf(thread),
                        (UIntPtr)unchecked((uint)(thread != null ? thread.threadIndex : -1)));
#endif
            ReleaseInternal(thread.GetThreadId() + 1);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        [NoHeapAllocation]
        public void AssertHeldBy(Thread thread)
        {
            VTable.Assert(IsHeldBy(thread));
        }

        [NoHeapAllocation]
        public bool IsHeldBy(Thread thread)
        {
#if TRACE
            Tracing.Log(Tracing.Debug, "SpinLock.IsHeldBy({0:x8},{1})",
                        VTable.addressOf(thread),
                        (UIntPtr)unchecked((uint)(thread != null ? thread.threadIndex : -1)));
#endif
            return IsHeldByInternal(thread.GetThreadId() + 1);
        }

        [NoHeapAllocation]
        public void Acquire(int threadId)
        {
            AcquireInternal(threadId + 1);
        }

        [NoHeapAllocation]
        public void Release(int threadId)
        {
            ReleaseInternal(threadId + 1);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        [NoHeapAllocation]
        public void AssertHeldBy(int threadId)
        {
            VTable.Assert(IsHeldByInternal(threadId + 1));
        }

        [NoHeapAllocation]
        public bool IsHeldBy(int threadId)
        {
            return IsHeldByInternal(threadId + 1);
        }

        [NoHeapAllocation]
        private void AcquireInternal(int currentThreadIndexPlusOne)
        {
            // SpinLocks are *NOT* re-entrant.
            VTable.Assert(lockingThreadIndexPlusOne !=
                          currentThreadIndexPlusOne);
            VTable.Assert(Processor.InterruptsDisabled());

            int result = Interlocked.Exchange(ref this.lockWord, 1);
            if (result != 0) {
                // spin for exponentially backed off delay
                int count = 31;
                while (true) {
#if SINGULARITY_KERNEL
                    Kernel.Waypoint(888);
#endif // SINGULARITY_KERNEL
                    Thread.SpinWait(count);
                    result = Interlocked.Exchange(ref this.lockWord, 1);
                    if (result == 0) {
                        break;
                    }
#if !SINGULARITY_KERNEL
                    // We yield to allow the thread holding the lock to run
                    Thread.Yield();
                    // check the lock word once after yielding
                    result = Interlocked.Exchange(ref this.lockWord, 1);
                    if (result == 0) {
                        break;
                    }
#endif // !SINGULARITY_KERNEL
                    count = (count == 0x7ffffffu) ? count : count + count + 1;
                } // while
            }

            VTable.Assert(lockWord == 1 && lockingThreadIndexPlusOne == 0);
            this.lockingThreadIndexPlusOne = currentThreadIndexPlusOne;
        }

        // Note: any changes to this code must be reflected in
        // the Release code in SwitchToThreadContext in halidt.asm.
        [NoHeapAllocation]
        private void ReleaseInternal(int currentThreadIndexPlusOne)
        {
            VTable.Assert(lockWord == 1 &&
                          lockingThreadIndexPlusOne == currentThreadIndexPlusOne);
            VTable.Assert(Processor.InterruptsDisabled());

            this.lockingThreadIndexPlusOne = 0;
            this.lockWord = 0;
        }

        [NoHeapAllocation]
        private bool IsHeldInternal()
        {
            return this.lockWord != 0;
        }

        [NoHeapAllocation]
        private bool IsHeldByInternal(int threadIndexPlusOne)
        {
            return (this.lockWord != 0 &&
                    this.lockingThreadIndexPlusOne == threadIndexPlusOne);
        }

        [AccessedByRuntime("referenced from halidt.asm")]
        private int lockWord;
        [AccessedByRuntime("referenced from halidt.asm")]
        private int lockingThreadIndexPlusOne; // Thread.GetThreadId() + 1
    }
}
