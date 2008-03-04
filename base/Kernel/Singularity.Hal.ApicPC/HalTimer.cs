///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   HalTimer.cs
//
//  Note:
//
//  This file is an implementation of Interfaces/Hal/HalTimer.csi
//

using System;
using System.Runtime.CompilerServices;

namespace Microsoft.Singularity.Hal
{
    public class HalTimer
    {
        private ApicTimer apicTimer;

        public HalTimer(ApicTimer theApicTimer)
        {
            this.apicTimer = theApicTimer;
        }

        /// <summary>
        /// Clear interrupt associated with timer.
        /// </summary>
        [NoHeapAllocation]
        public void ClearInterrupt()
        {
            apicTimer.ClearInterrupt();
        }

        /// <value>
        /// Maximum value accepted by SetNextInterrupt (in units of 100ns).
        /// </value>
        public long MaxInterruptInterval {
            [NoHeapAllocation]
            get { return apicTimer.MaxInterruptInterval; }
        }

        /// <value>
        /// Minimum value accepted by SetNextInterrupt (in units of 100ns).
        /// </value>
        public long MinInterruptInterval {
            [NoHeapAllocation]
            get { return apicTimer.MinInterruptInterval; }
        }

        /// <value>
        /// Granularity of interrupt timeout (in units of 100ns).
        /// </value>
        public long InterruptIntervalGranularity {
            [NoHeapAllocation]
            get { return apicTimer.InterruptIntervalGranularity; }
        }

        /// <summary>
        /// Set relative time of next interrupt.
        ///
        /// <param name="delta">Relative time of next interrupt in units
        /// of 100ns.  The time should be with the range between
        /// from <c>SetNextInterruptMinDelta</c> to
        /// <c>SetNextInterruptMaxDelta</c></param>.
        /// <returns> true on success.</returns>
        /// </summary>
        [NoHeapAllocation]
        public bool SetNextInterrupt(long delta)
        {
            return apicTimer.SetNextInterrupt(delta);
        }

        public byte Interrupt {
            [NoHeapAllocation]
            get { return apicTimer.Interrupt; }
        }
    }
} // namespace Microsoft.Singularity.Hal
