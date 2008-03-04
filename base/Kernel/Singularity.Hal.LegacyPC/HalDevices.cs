///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File: HalDevices.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;

using Microsoft.Singularity;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Hal.Acpi;

namespace Microsoft.Singularity.Hal
{
    public class HalDevices
    {
        private static Pic pic;
        private static Timer8254 timer;
        private static PMTimer pmTimer;
        private static RTClock clock;

        // haryadi
        private static HalMemory halMemory;

        [CLSCompliant(false)]
        public static void Initialize(Processor rootProcessor)
        {
            DebugStub.Print("HalDevices.Initialize() - Legacy\n");

            AcpiTables.Parse();
            pmTimer = AcpiTables.GetPMTimer();

            // PIC
            PnpConfig picConfig
                = (PnpConfig)IoSystem.YieldResources("/pnp/PNP0000", typeof(Pic));
            pic = new Pic(picConfig);
            pic.Initialize();

            // Timer
            PnpConfig timerConfig
                = (PnpConfig)IoSystem.YieldResources("/pnp/PNP0100", typeof(Timer8254));
            timer = new Timer8254(timerConfig, pic);
            byte timerInterrupt = timer.Initialize();

            // Real-time clock
            PnpConfig clockConfig
                = (PnpConfig)IoSystem.YieldResources("/pnp/PNP0B00", typeof(RTClock));
            clock = new RTClock(clockConfig, pic, timer);
            byte clockInterrupt = clock.Initialize();

            bool noisyTimer = false;
            if (pmTimer != null) {
                noisyTimer = CalibrateTimers.Run(pmTimer, timer);
            }
            else {
                CalibrateTimers.Run(clock, timer);
            }

            clock.SetNoisyTimer(noisyTimer);
            clock.Start();

            HalClock halClock = new HalClock(clock);
            SystemClock.Initialize(halClock, TimeSpan.FromHours(8).Ticks);

            rootProcessor.AddPic(new HalPic(pic));
            rootProcessor.AddTimer(timerInterrupt, new HalTimer(timer));
            rootProcessor.AddClock(clockInterrupt, halClock);

            // ----------------------------------------------------------
            // haryadi: add Srat tables to the Processor
            halMemory = new HalMemory(AcpiTables.GetSrat());
            Processor.AddHalMemory(halMemory);

            Processor.SetIdtTable();

            timer.Start();

            // Get the screen resources.  Since we have metadata above to
            // declare all fixed resources used by the screen,
            // YieldResources("") will keep the resource tracking correct:
            Console.Screen =
                new HalScreen(IoSystem.YieldResources("", typeof(HalScreen)));
        }

        public static void Finalize()
        {
            clock.Finalize();
            clock = null;

            timer.Finalize();
            timer = null;

            pic.Finalize();
            pic = null;
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static byte GetMaximumIrq()
        {
            return pic.MaximumIrq;
        }

        //
        // Adding and removing interrupts from the Pic.
        //
        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static void EnableIoInterrupt(byte irq)
        {
            pic.EnableIrq(irq);
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static void DisableIoInterrupt(byte irq)
        {
            pic.DisableIrq(irq);
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static bool InternalInterrupt(byte interrupt)
        {
            // Strictly there are no interrupts internal to
            // this Hal instance.  In practice, some hardware seems
            // intent on firing an interrupt even if it is masked.
            //
            // Return true if interrupt appears to be valid but
            // is masked, false otherwise.
            byte irq = pic.InterruptToIrq(interrupt);

#if DEBUG_SPURIOUS
            DebugStub.Break();
#endif

            if (pic.IrqMasked(irq) == true)
            {
                DebugStub.WriteLine("--- Acked spurious Irq={0:x2}", __arglist(irq));
                pic.AckIrq(irq);
                return true;
            }
            return false;
        }

        [NoHeapAllocation]
        private static int GetProcessorCount()
        {
            return 1;
        }

        [CLSCompliant(false)]
        public static void StartApProcessors()
        {
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        public static void FreezeProcessors()
        {
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        private static void SendFixedIPI(byte vector, int from, int to)
        {
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        private static void BroadcastFixedIPI(byte vector, bool includeSelf)
        {
        }

        [CLSCompliant(false)]
        [NoHeapAllocation]
        private static void ClearFixedIPI()
        {
        }
    }
}
