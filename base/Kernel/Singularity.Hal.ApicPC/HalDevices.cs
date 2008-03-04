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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;

using Microsoft.Singularity;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Hal.Acpi;

namespace Microsoft.Singularity.Hal
{
    // Lockstep states for MP initialization.  The bootstrap
    // processor (BSP) sets states beginning Bsp and the
    // upcoming Application Processor (AP) sets states beginning
    // with Ap.
    internal enum MpSyncState : int
    {
        BspWaitingForAp             = 0,
        ApOnline                    = 1,
        BspWaitingForApCalibration  = 2,
        ApCalibrationDone           = 3,
        BspStartingTscCorrelation   = 4,
        ApTscCorrelationDone        = 5,
        BspWaitingApRunning         = 6,
        ApRunning                   = 7
    }

    [CLSCompliant(false)]
    public sealed class HalDevices
    {
        private const byte PicBaseVector  = 0x60;
        private const byte ApicBaseVector = 0x70;

        private static IoApic [] ioApics;

        private static HalPic    halPic;    // dumb wrapper for APIC for kernel
        private static Apic      apic;

        private static Pic       pic;       // for squelching pic interrupts

        private static HalTimer  halTimer;  // wrapper for APIC timer for kernel
        private static ApicTimer apicTimer;

        private static PMTimer   pmTimer;
        private static Timer8254 stallTimer;

        private static RTClock   rtClock;

        private static HalClock  halClock;
        private static HalMemory halMemory;

        private static int processorCount;

        private static volatile MpSyncState mpSyncState;

        // TSC skew calculation variables
        private const int TscCalibrationRounds = 20;
        private static volatile uint tsc0Lo;
        private static volatile uint tsc0Hi;
        private static ulong cpu0CyclesPerSecond;

        // Kernel thread stack variables
        private static Thread  nextKernelThread;
        private static UIntPtr nextKernelStackBegin;
        private static UIntPtr nextKernelStackLimit;

        public static void InitializeBsp(Processor rootProcessor)
        {
            DebugStub.Print("HalDevices.Initialize()\n");

            AcpiTables.Parse();
            pmTimer = AcpiTables.GetPMTimer();

            // Get PIC resources.  Pic is masked by default.
            PnpConfig picConfig
                = (PnpConfig)IoSystem.YieldResources("/pnp/PNP0000", typeof(Pic));
            pic = new Pic(picConfig);
            pic.Initialize(PicBaseVector);

            // Parse MP Table and create IO apics
            MpResources.ParseMpTables();
            ioApics = IoApic.CreateIOApics();

            apic = new Apic(ioApics);
            apic.Initialize(ApicBaseVector);
            halPic = new HalPic(apic);

            // Apic timer is used to provide one-shot timer interrupts.
            apicTimer = new ApicTimer(apic);
            apicTimer.Initialize();

            halTimer = new HalTimer(apicTimer);

            // Calibrate timers
            Calibrate.CpuCycleCounter(pmTimer);
            Calibrate.ApicTimer(pmTimer, apicTimer);

            // Store BSPs clock rate
            cpu0CyclesPerSecond = Processor.CyclesPerSecond;

            // Legacy timer is used to time stalls when starting CPUs.
            PnpConfig i8254Config
                = (PnpConfig)IoSystem.YieldResources("/pnp/PNP0100", typeof(Timer8254));
            stallTimer = new Timer8254(i8254Config);

            // Real-time clock
            PnpConfig rtClockConfig
                = (PnpConfig)IoSystem.YieldResources("/pnp/PNP0B00", typeof(RTClock));
            rtClock = new RTClock(rtClockConfig, apic);

            // Compose HalClock
            halClock = new HalClock(apic, rtClock, new PMClock(pmTimer));

            SystemClock.Initialize(halClock, TimeSpan.FromHours(8).Ticks);

            rootProcessor.AddPic(halPic);

            rootProcessor.AddTimer(halTimer.Interrupt, halTimer);
            rootProcessor.AddClock(halClock.Interrupt, halClock);

            InitializeProcessorCount();
            DebugReportProcessors();

            // ------------------------------------------------
            // haryadi: add Srat tables to the Processor
            halMemory = new HalMemory(AcpiTables.GetSrat());
            Processor.AddHalMemory(halMemory);

            Processor.SetIdtTable();

            apicTimer.Start();

            // Get the screen resources.  Since we have metadata above to
            // declare all fixed resources used by the screen,
            // YieldResources("") will keep the resource tracking correct:
            Console.Screen =
                new HalScreen(IoSystem.YieldResources("", typeof(HalScreen)));

            apic.DumpState();
            foreach (IoApic ioApic in ioApics) {
                ioApic.DumpRedirectionEntries();
            }
            pic.DumpRegisters();
        }

        public static void InitializeAp(Processor processor)
        {
            Processor.SetIdtTable();

            Thread.BindKernelThread(nextKernelThread,
                                    nextKernelStackBegin,
                                    nextKernelStackLimit);

            // Fleeting check that allocator works.
            Object o = new Object();
            if (o == null)
                DebugStub.Break();

            apic.Initialize();
            apicTimer.Initialize();
            processor.AddPic(halPic);
            processor.AddTimer(halTimer.Interrupt, halTimer);
            processor.AddClock(halClock.Interrupt, halClock);

            SetMpSyncState(MpSyncState.ApOnline);
            WaitForMpSyncState(MpSyncState.BspWaitingForApCalibration);

            // Calibrate timers
            ulong t1 = Processor.CycleCount;
            Calibrate.CpuCycleCounter(pmTimer);
            Calibrate.ApicTimer(pmTimer, apicTimer);
            ulong t2 = Processor.CycleCount;
            Tracing.Log(Tracing.Audit, "Calibration time {0}",
                        new UIntPtr((uint)(t2 - t1)));

            SetMpSyncState(MpSyncState.ApCalibrationDone);

            // Calculate tsc skew
            long [] skewEstimates = new long[TscCalibrationRounds];
            for (int i = 0; i < TscCalibrationRounds; i++) {
                ApSync(out skewEstimates[i]);
            }

            bool success = false;
            WaitForMpSyncState(MpSyncState.BspWaitingApRunning, 100000,
                               out success);

            SetMpSyncState(MpSyncState.ApRunning);

            // Sort skew estimates and picked median
            Array.Sort(skewEstimates);
            Tracing.Log(Tracing.Audit, "Skew estimate {0}",
                new UIntPtr((int)(skewEstimates[TscCalibrationRounds / 2])));
            Tracing.SetTscOffset((skewEstimates[TscCalibrationRounds / 2]));

            apicTimer.Start();
        }

        public static void Initialize(Processor processor)
        {
            if (processor.Id == 0) {
                InitializeBsp(processor);

                IoSystem.RegisterKernelDriver(
                    typeof(HpetResources),
                    new IoDeviceCreate(Hpet.CreateDevice)
                    );
            }
            else {
                InitializeAp(processor);
            }
        }

        public static void Finalize()
        {
            rtClock.Finalize();
            rtClock = null;

            apicTimer.Finalize();
            apicTimer = null;

            apic.Finalize();
            apic = null;

            pic.Finalize();
            pic = null;
        }

        [NoHeapAllocation]
        public static bool InternalInterrupt(byte interrupt)
        {
            if (interrupt >= PicBaseVector && interrupt < ApicBaseVector) {
                // Spurious PIC interrupt.
                // Appears to happen once on test boards even though PIC
                // is masked out and no interrupt was raised before masking.
                pic.DumpRegisters();
                pic.AckIrq((byte) (interrupt - PicBaseVector));
                return true;
            }
            return false;
        }

        [NoHeapAllocation]
        internal static void StallProcessor(uint microseconds)
        {
            ulong todo = microseconds *
                (Processor.CyclesPerSecond / 1000000);
            ulong last = Processor.CycleCount;
            ulong elapsed = 0;

            while (elapsed < todo) {
                // Keep track of how much time has elapsed.
                ulong now  = Processor.CycleCount;
                elapsed   += (now - last);
                last       = now;
            }
        }

        internal static void SwitchToHpetClock(Hpet hpet)
        {
            DebugStub.Print("Switching to HPET clock");
            halClock.SwitchToHpetClock(
                new HpetClock(hpet, (ulong)halClock.GetKernelTicks() + 1000u)
                );
        }

        [Conditional("SINGULARITY_MP")]
        [NoHeapAllocation]
        private static void SetMpSyncState(MpSyncState newState)
        {
            Tracing.Log(Tracing.Debug, "Changing MP sync state {0} -> {1}",
                        (uint)mpSyncState, (uint)newState);
            mpSyncState = newState;
        }

        [Conditional("SINGULARITY_MP")]
        [NoHeapAllocation]
        private static void WaitForMpSyncState(MpSyncState newState,
                                               uint microseconds,
                                               out bool success)
        {
            Tracing.Log(
                Tracing.Debug,
                "Waiting for MP sync state {0} -> {1} ({2} microseconds)",
                (uint)mpSyncState, (uint)newState, microseconds
                );

            ulong todo = microseconds * (Processor.CyclesPerSecond / 1000000);
            ulong last = Processor.CycleCount;
            ulong elapsed = 0;

            success = false;

            while (elapsed < todo) {
                ulong now  = Processor.CycleCount;
                elapsed   += (now - last);
                last       = now;
                if (HalDevices.mpSyncState == newState) {
                    success = true;
                    return;
                }
            }
            Tracing.Log(Tracing.Debug, "Mp sync state timed out");
        }

        [Conditional("SINGULARITY_MP")]
        [NoHeapAllocation]
        private static void WaitForMpSyncState(MpSyncState newState)
        {
            Tracing.Log(
                Tracing.Debug,
                "Waiting for MP sync state {0} -> {1} (indefinite)",
                (uint)mpSyncState, (uint)newState
                );
            while (HalDevices.mpSyncState != newState)
                ;
        }

        public static byte GetMaximumIrq()
        {
            return apic.MaximumIrq;
        }

        //
        // Adding and removing interrupts from the Pic.
        //
        [NoHeapAllocation]
        public static void EnableIoInterrupt(byte irq)
        {
            apic.EnableIrq(irq);
        }

        [NoHeapAllocation]
        public static void DisableIoInterrupt(byte irq)
        {
            apic.DisableIrq(irq);
        }


        private static void InitializeProcessorCount()
        {
            Madt madt = AcpiTables.GetMadt();
            if (madt != null) {
                processorCount = madt.GetLocalApics().Count;
            }
            else if (MpResources.ProcessorEntries != null) {
                processorCount = MpResources.ProcessorEntries.Count;
            }
            else {
                processorCount = 1;
            }
        }

        [NoHeapAllocation]
        private static int GetProcessorCount()
        {
            return processorCount;
        }

        private static void DebugReportProcessors()
        {
#if SINGULARITY_MP
            string kernelType = "Multi";
#else
            string kernelType = "Uni";
#endif
            DebugStub.Print("{0}processor kernel on {1} processor system.\n",
                            __arglist(kernelType, GetProcessorCount()));
        }

        // Runs Bootstrap processor component of TSC offset computation.
        [Conditional("SINGULARITY_MP")]
        [NoHeapAllocation]
        private static void BspSync(uint microseconds)
        {
            ulong todo = microseconds * (Processor.CyclesPerSecond / 1000000);
            ulong last = Processor.CycleCount;
            ulong elapsed = 0;

            SetMpSyncState(MpSyncState.BspStartingTscCorrelation);

            elapsed = 0;
            while (elapsed < todo &&
                   mpSyncState != MpSyncState.ApTscCorrelationDone) {
                // Keep track of how much time has elapsed.
                ulong now  = Processor.CycleCount;
                elapsed   += (now - last);
                last       = now;
                // Order of these assignments is important
                tsc0Lo     = (uint)(now & 0xffffffff);
                tsc0Hi     = (uint)(now >> 32);
                // Yield hardware thread on HT boxes
                Thread.NativeNoOp();
            }
        }

        [Conditional("SINGULARITY_MP")]
        [NoHeapAllocation]
        private static void ApSync(out long tscOffset)
        {
            WaitForMpSyncState(MpSyncState.BspStartingTscCorrelation);

            ulong now = 0;
            uint tscLo;
            uint tscHi;
            uint tscTmp;
            for (int i = 0; i < 5; i++) {
                do {
                    now = Processor.CycleCount;
                    tscLo  = tsc0Lo;
                    tscHi  = tsc0Hi;
                    tscTmp = tsc0Lo;
                    // Yield hardware thread on HT boxes
                    Thread.NativeNoOp();
                } while (tscTmp != tscLo);
            }
            ulong tsc0 = (((ulong)tsc0Hi) << 32) + tsc0Lo;
            if (tsc0 > now) {
                Tracing.Log(Tracing.Audit, "Skew tsc-{0}",
                            new UIntPtr(tsc0 - now));
            }
            else {
                Tracing.Log(Tracing.Audit, "Skew tsc+{0}",
                            new UIntPtr(now - tsc0));
            }
            tscOffset = (long)(now - tsc0);
            SetMpSyncState(MpSyncState.ApTscCorrelationDone);
        }

        [Conditional("SINGULARITY_MP")]
        [NoHeapAllocation]
        private static void AnnounceApFail(int nextCpu)
        {
            MpSyncState localMpState = mpSyncState;
            DebugStub.Print("MpState {0}", __arglist(localMpState));
            DebugStub.Break();
            BootInfo bi = BootInfo.GetBootInfo();
            DebugStub.Print("Cpu {0} failed. ",
                            __arglist(nextCpu));
            DebugStub.Print("GotBack-> Status {0} Count {1:x8}\n",
                            __arglist(bi.MpStatus32, bi.MpCpuCount));
        }

        [Conditional("SINGULARITY_MP")]
        //        [NoHeapAllocation]
        private static void StartApProcessorsInternal()
        {
            // Currently we fire up all processors.  They
            // serialize on a spin lock in low memory.
            int expectedCpus = GetProcessorCount();
            int nextCpu      = 1;

            bool en = Processor.DisableInterrupts();
            try {
                do {
                    DebugStub.Print("Starting CPU {0} / {1}\n",
                                    __arglist(nextCpu + 1,
                                              expectedCpus));
                    SetMpSyncState(MpSyncState.BspWaitingForAp);
                    Tracing.Log(Tracing.Audit,
                                "Initializing cpu {0}", (uint)nextCpu);
                    nextKernelThread = Thread.PrepareKernelThread();

                    MpBootInfo.PrepareForCpuStart(nextCpu);
                    MpBootInfo mbi       = MpBootInfo.GetMpBootInfo();
                    nextKernelStackBegin = mbi.KernelStackBegin;
                    nextKernelStackLimit = mbi.KernelStackLimit;

                    Tracing.Log(Tracing.Audit,
                                "Starting cpu {0}. Stack start {1:x8} base {2:x8} limit {3:x8})\n",
                                new UIntPtr(nextCpu),
                                mbi.KernelStackBegin,
                                mbi.KernelStack,
                                mbi.KernelStackLimit);

                    if (nextCpu == 1) {
                        BootInfo bi = BootInfo.GetBootInfo();
                        apic.BroadcastStartupIPI((byte) (bi.MpEnter32 >> 12));
                    }

                    bool success = false;
                    WaitForMpSyncState(MpSyncState.ApOnline, 500000,
                                       out success);
                    if (!success) {
                        AnnounceApFail(nextCpu);
                        continue;
                    }

                    SetMpSyncState(MpSyncState.BspWaitingForApCalibration);

                    WaitForMpSyncState(MpSyncState.ApCalibrationDone, 1000000,
                                       out success);
                    if (!success) {
                        AnnounceApFail(nextCpu);
                        continue;
                    }

                    for (int i = 0; i < TscCalibrationRounds; i++) {
                        BspSync(100000);
                    }

                    SetMpSyncState(MpSyncState.BspWaitingApRunning);

                    WaitForMpSyncState(MpSyncState.ApRunning, 100000,
                                       out success);
                    if (!success) {
                        AnnounceApFail(nextCpu);
                        continue;
                    }
                    Processor.RestoreInterrupts(en);
                    en = false;
                    DebugStub.Print("Cpu {0} running ({1} remaining).\n",
                                    __arglist(nextCpu,
                                              expectedCpus - nextCpu - 1));
                } while (++nextCpu < expectedCpus);
                DebugStub.Print("Done.");
            }
            finally {
                DebugStub.Print("Reached finally block in HalDevices.StartApProcessorsInternal()");
                Processor.RestoreInterrupts(en);
            }
        }

        //        [NoHeapAllocation]
        public static void StartApProcessors()
        {
            if (GetProcessorCount() > 1) {
                StartApProcessorsInternal();
            }
        }

        [NoHeapAllocation]
        public static void FreezeProcessors()
        {
            apic.BroadcastFreezeIPI();
        }

        [NoHeapAllocation]
        public static void SendFixedIPI(byte vector, int from, int to)
        {
            apic.SendFixedIPI(vector, from, to);
        }

        [NoHeapAllocation]
        public static void BroadcastFixedIPI(byte vector, bool includeSelf)
        {
            apic.BroadcastFixedIPI(vector, includeSelf);
        }

        [NoHeapAllocation]
        public static void ClearFixedIPI()
        {
            apic.ClearFixedIPI();
        }
    }
}
