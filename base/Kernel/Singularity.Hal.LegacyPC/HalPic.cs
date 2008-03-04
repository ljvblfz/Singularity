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
        private Pic pic;

        internal HalPic(Pic thePic)
        {
            this.pic = thePic;
        }

        /// <summary>
        /// Maximum valid IRQ property.  On legacy PC systems this value is
        /// 15.  On APIC PC systems this number will usually be larger.
        /// </summary>
        public byte MaximumIrq
        {
            [NoHeapAllocation]
            get { return pic.MaximumIrq; }
        }

        /// <summary>
        /// Convert interrupt vector to interrupt request line.
        /// </summary>
        [NoHeapAllocation]
        public byte InterruptToIrq(byte interrupt)
        {
            return pic.InterruptToIrq(interrupt);
        }

        /// <summary>
        /// Convert interrupt request line to interrupt vector.
        /// </summary>
        [NoHeapAllocation]
        public byte IrqToInterrupt(byte irq)
        {
            return pic.IrqToInterrupt(irq);
        }

        /// <summary>
        /// Acknowledge the interrupt request.  (EOI)
        /// </summary>
        [NoHeapAllocation]
        public void AckIrq(byte irq)
        {
            pic.AckIrq(irq);
        }

        /// <summary>
        /// Enable interrupt request by removing mask.
        /// </summary>
        [NoHeapAllocation]
        public void EnableIrq(byte irq)
        {
            pic.EnableIrq(irq);
        }

        /// <summary>
        /// Disable interrupt request by applying mask.
        /// </summary>
        [NoHeapAllocation]
        public void DisableIrq(byte irq)
        {
            pic.DisableIrq(irq);
        }

        /// <summary>
        /// Acknowledge and mask interrupt.
        /// </summary>
        [NoHeapAllocation]
        public void ClearInterrupt(byte interrupt)
        {
            pic.ClearInterrupt(interrupt);
        }
    }
} // namespace Microsoft.Singularity.Hal
