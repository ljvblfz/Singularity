///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   HalMpClock.cs
//
//  Note:
//
//  This file is an implementation of Interfaces/Hal/HalClock.csi
//
//    For now we just read the time from the HPET or PMTimer.  Both of these
//  use a spinlock to protect the relevant state.  The PMTimer is incredibly
//  expensive.


#if !SINGULARITY_MP
#error "This file is only for MP builds."
#endif

namespace Microsoft.Singularity.Hal
{
    using Microsoft.Singularity.Hal.Acpi;

    using System;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Threading;

    public class HalClock
    {
        Apic      apic;
        PMClock   pmClock;
        RTClock   rtClock;
        HpetClock hpetClock;
        SpinLock  spinLock;

        internal HalClock(Apic apic, RTClock rtClock, PMClock pmClock)
        {
            this.apic      = apic;
            this.rtClock   = rtClock;
            this.pmClock   = pmClock;
            this.hpetClock = null;
            this.spinLock  = new SpinLock();
        }

        [Conditional("SINGULARITY_MP")]
        [NoHeapAllocation]
        private void AcquireLock()
        {
            this.spinLock.Acquire();
        }

        [Conditional("SINGULARITY_MP")]
        [NoHeapAllocation]
        private void ReleaseLock()
        {
            this.spinLock.Release();
        }

        [NoHeapAllocation]
        public long GetKernelTicks()
        {
            bool en = Processor.DisableInterrupts();
            this.AcquireLock();
            try {
                if (this.hpetClock == null) {
                    return (long) pmClock.GetKernelTicks();
                }
                else {
                    return (long) hpetClock.GetKernelTicks();
                }
            }
            finally {
                this.ReleaseLock();
                Processor.RestoreInterrupts(en);
            }
        }

        internal byte Interrupt
        {
            [NoHeapAllocation]
            get { return rtClock.Interrupt; }
        }

        [NoHeapAllocation]
        public void ClearInterrupt()
        {
            bool en = Processor.DisableInterrupts();
            this.AcquireLock();
            try {
                if (hpetClock == null) {
                    pmClock.Update();
                }
                else {
                    hpetClock.Update();
                }
                rtClock.ClearInterrupt();
            }
            finally {
                this.ReleaseLock();
                Processor.RestoreInterrupts(en);
            }
        }

        internal void SwitchToHpetClock(HpetClock hc)
        {
            // Change rt clock interrupt frequency to appropriate
            // rate for HPET main clock.
            bool en = Processor.DisableInterrupts();
            this.AcquireLock();
            try {
                rtClock.SetFrequency(HpetClock.UpdateFrequency(hc.Hpet));
                hpetClock = hc;
            }
            finally {
                this.ReleaseLock();
                Processor.RestoreInterrupts(en);
            }
            DebugStub.Print("Hal switching to HpetClock.\n");
        }

        [NoHeapAllocation]
        public void CpuResumeFromHaltEvent()
        {
        }

        [NoHeapAllocation]
        public long GetRtcTime()
        {
            return rtClock.GetBootTime() + GetKernelTicks();
        }

        public void SetRtcTime(long newRtcTime)
        {
            rtClock.SetRtcTime(newRtcTime, GetKernelTicks());
        }
    }
}
