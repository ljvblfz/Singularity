///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   HalPic.cs
//
//  Note:
//
//  This file is an implementation of Interfaces/Hal/HalPic.csi
//

using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.Singularity.Hal
{
    public class HalPic
    {
        private Apic apic;

        public HalPic(Apic theApic)
        {
            this.apic = theApic;
        }

        public byte MaximumIrq
        {
            [NoHeapAllocation]
            get {
                return apic.MaximumIrq;
            }
        }

        /// <summary>
        /// Convert interrupt vector to interrupt request line.
        /// </summary>
        [NoHeapAllocation]
        public byte InterruptToIrq(byte interrupt)
        {
            return apic.InterruptToIrq(interrupt);
        }

        /// <summary>
        /// Convert interrupt request line to interrupt vector.
        /// </summary>
        [NoHeapAllocation]
        public byte IrqToInterrupt(byte irq)
        {
            return apic.IrqToInterrupt(irq);
        }

        /// <summary>
        /// Acknowledge the interrupt request.  (EOI)
        /// </summary>
        [NoHeapAllocation]
        public void AckIrq(byte irq)
        {
            apic.AckIrq(irq);
        }

        /// <summary>
        /// Enable interrupt request by removing mask.
        /// </summary>
        [NoHeapAllocation]
        public void EnableIrq(byte irq)
        {
            apic.EnableIrq(irq);
        }

        /// <summary>
        /// Disable interrupt request by applying mask.
        /// </summary>
        [NoHeapAllocation]
        public void DisableIrq(byte irq)
        {
            apic.DisableIrq(irq);
        }

        /// <summary>
        /// Acknowledge and mask interrupt.
        /// </summary>
        [NoHeapAllocation]
        public void ClearInterrupt(byte interrupt)
        {
            apic.ClearInterrupt(interrupt);
        }
    }
} // namespace Microsoft.Singularity.Hal
