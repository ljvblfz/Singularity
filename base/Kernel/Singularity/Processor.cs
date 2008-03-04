////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Processor.cs
//
//  Note:
//

//#define DEBUG_EXCEPTIONS
//#define DEBUG_INTERRUPTS
//#define DEBUG_DISPATCH_TIMER
//#define DEBUG_DISPATCH_IO
//#define SAMPLE_PC

// #define SINGULARITY_ASMP

using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;

using Microsoft.Singularity.Hal;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Memory;
using Microsoft.Singularity.Scheduling;
using Microsoft.Singularity.X86;

// haryadi: for Abi Call
// using Microsoft.Singularity.V1.Services;

namespace Microsoft.Singularity
{
    [CLSCompliant(false)]
    public enum ProcessorEvent : ushort
    {
        Exception = 0,
        Resume    = 1,
        Interrupt = 2
    }

    [CLSCompliant(false)]
    [CCtorIsRunDuringStartup]
    public class Processor
    {
        private const uint interruptStackSize   = 0x2000;
        private const uint exceptionStackSize   = 0x2000;
        private const uint schedulerStackSize   = 0x2000;

        internal unsafe ProcessorContext* context;

        public static Processor[] processorTable;

        //As requested/desired by the SchedulerClock

        private HalPic   pic;
        private HalTimer timer;
        private HalClock clock;

        // haryadi
        private static IHalMemory halMemory;

        private byte timerInterrupt;
        private byte clockInterrupt;

        private bool inInterruptContext = false;
        private bool halted = false;

        public readonly int processorIndex;
        public uint NumExceptions = 0;
        public uint NumInterrupts = 0;
        public uint NumContextSwitches = 0;
        private int[] interruptCounts;
        private ulong cyclesPerSecond;

        private static int runningCpus = 0;

        public HalTimer Timer
        {
            [NoHeapAllocation]
            get { return timer; }
        }

        public Thread IdleThread;

        internal static void InitializeProcessorTable()
        {
            processorTable = new Processor [MpBootInfo.MAX_CPU];
            for (int i = 0; i < processorTable.Length; i++) {
                processorTable[i] = new Processor(i);
            }
            DebugStub.WriteLine("Processors: {0} of {1}",
                                __arglist(processorTable.Length, MpBootInfo.MAX_CPU));
        }

        internal static void AllocateStack(UIntPtr size, out UIntPtr begin, out UIntPtr limit)
        {
            Kernel.Waypoint(818);
            size = MemoryManager.PagePad(size);
            limit = MemoryManager.KernelAllocate(
                MemoryManager.PagesFromBytes(size), null, 0, System.GCs.PageType.Stack);
            begin = limit + size;
            Kernel.Waypoint(819);
        }

        private Processor(int index)
        {
            processorIndex = index;
            if (interruptCounts == null) {
                interruptCounts = new int [256];
            }
        }

        public unsafe void Initialize(int processorId)
        {
            processorTable[processorId] = this;

            BootInfo bi = BootInfo.GetBootInfo();
            CpuInfo  ci = bi.GetCpuInfo(processorId);
            context = (ProcessorContext*) ci.Fs32;
            context->UpdateAfterGC(this);

            AllocateStack(interruptStackSize,
                          out context->interruptStackBegin,
                          out context->interruptStackLimit);
            AllocateStack(exceptionStackSize,
                          out context->exceptionStackBegin,
                          out context->exceptionStackLimit);
            AllocateStack(schedulerStackSize,
                          out context->schedulerStackBegin,
                          out context->schedulerStackLimit);

            Tracing.Log(Tracing.Debug, "Initialized Processor {0}",
                        (UIntPtr)processorId);
            Tracing.Log(Tracing.Debug, "asmInterruptStack={0:x}..{1:x}",
                        context->interruptStackBegin,
                        context->interruptStackLimit);

            inInterruptContext = false;
            MpExecution.AddProcessorContext(context);

            Interlocked.Increment(ref runningCpus);
        }

        [NoHeapAllocation]
        public void Uninitialize(int processorId)
        {
            Tracing.Log(Tracing.Debug, "UnInitializing Processor {0}",
                        (UIntPtr)processorId);

            Interlocked.Decrement(ref runningCpus);

            // Processor is out of commission
            HaltUntilInterrupt();
        }

        private static void MeasureFxSavePerformance()
        {
#if DO_FXSAVE_TEST
            DebugStub.WriteLine("Starting fxsave test.");
            int timePerFxsave = TestFxsave();
            DebugStub.Print("Cycles per fxsave/fxrstor pair: {0}",
                            __arglist(timePerFxsave));
#endif // DO_FXSAVE_TEST
        }

        private static void MeasureFs0Performance()
        {
#if DO_FS0_TEST
            DebugStub.WriteLine("Starting fs:[0] test.");
            int timePerFs0 = TestFs0();
            DebugStub.WriteLine("Cycles per fs:[0] test: {0}",
                            __arglist(timePerFs0));
#endif // DO_FS0_TEST
        }

        private static void MeasureCliStiPerformance()
        {
#if DO_CLI_STI_TEST
            DebugStub.WriteLine("Starting cli/sti test.");
            int timePerCli = TestCliSti();
            DebugStub.WriteLine("Cycles per cli/sti test: {0}",
                            __arglist(timePerCli));

            ulong beg;
            ulong end;
            int loops = 1000000;

            DebugStub.WriteLine("Starting C# cli/sti test.");
            bool enabled = DisableInterrupts();

            RestoreInterrupts(true);

            beg = HalGetCycleCount();
            while (loops-- > 0) {
                RestoreInterrupts(true);    // 1
                DisableInterrupts();
                RestoreInterrupts(true);    // 2
                DisableInterrupts();
                RestoreInterrupts(true);    // 3
                DisableInterrupts();
                RestoreInterrupts(true);    // 4
                DisableInterrupts();
                RestoreInterrupts(true);    // 5
                DisableInterrupts();
                RestoreInterrupts(true);    // 6
                DisableInterrupts();
                RestoreInterrupts(true);    // 7
                DisableInterrupts();
                RestoreInterrupts(true);    // 8
                DisableInterrupts();
                RestoreInterrupts(true);    // 9
                DisableInterrupts();
                RestoreInterrupts(true);    // 10
                DisableInterrupts();
            }
            end = HalGetCycleCount();
            DisableInterrupts();
            RestoreInterrupts(enabled);

            timePerCli = ((int)((end - beg) / 1000000)) / 10;
            DebugStub.WriteLine("Cycles per C# cli/sti test: {0}",
                            __arglist(timePerCli));
#endif // DO_CLI_STI_TEST
        }

        public void AddPic(HalPic pic)
        {
            Tracing.Log(Tracing.Audit, "AddPic({0})\n",
                        Kernel.TypeName(pic));
            this.pic = pic;

            MeasureFxSavePerformance();
            MeasureFs0Performance();
            MeasureCliStiPerformance();
        }

        [NoHeapAllocation]
        public void AddTimer(byte interrupt, HalTimer timer)
        {
            Tracing.Log(Tracing.Audit, "AddTimer({0}) on {1}\n",
                        Kernel.TypeName(timer), interrupt);
            this.timer = timer;
            this.timerInterrupt = interrupt;
        }

        [NoHeapAllocation]
        public void AddClock(byte interrupt, HalClock clock)
        {
            Tracing.Log(Tracing.Audit, "AddClock({0}) on {1}\n",
                        Kernel.TypeName(clock), interrupt);
            this.clock = clock;
            this.clockInterrupt = interrupt;
        }

        [NoHeapAllocation]
        public static void AddHalMemory(IHalMemory aHalMemory)
        {
            Tracing.Log(Tracing.Audit, "AddHalMemory({0})\n",
                        Kernel.TypeName(aHalMemory));
            halMemory = aHalMemory;
        }

        [NoHeapAllocation]
        internal unsafe void Display()
        {
            int stackVariable;
            UIntPtr currentStack = new UIntPtr(&stackVariable);

            unchecked {
                Tracing.Log(Tracing.Debug, "Interrupt stack: {0:x} {1:x}..{2:x} uses",
                            currentStack,
                            context->interruptStackBegin,
                            context->interruptStackLimit);
            }
        }

        // Returns the processor that the calling thread is running on.
        // Needs to be fixed. (Added for consistency)
        public static Processor CurrentProcessor
        {
            [NoHeapAllocation]
            get { return GetCurrentProcessor(); }
        }

        [NoHeapAllocation]
        public static int GetCurrentProcessorId()
        {
            return GetCurrentProcessor().Id;
        }

        public unsafe int Id
        {
            [NoHeapAllocation]
            get { return context->cpuId; }
        }

        [NoHeapAllocation]
        public static void HaltUntilInterrupt()
        {
            CurrentProcessor.halted = true;
            HaltUntilInterruptNative();
        }

        public bool InInterruptContext
        {
            [NoHeapAllocation]
            get { return inInterruptContext; }
        }

        [NoHeapAllocation]
        public int GetInterruptCount(byte interrupt)
        {
            return interruptCounts[interrupt];
        }

        public int GetIrqCount(byte irq)
        {
            HalPic pic = CurrentProcessor.pic;
            return interruptCounts[pic.IrqToInterrupt(irq)];
        }

        public static byte GetMaxIrq()
        {
            return CurrentProcessor.pic.MaximumIrq;
        }

        public static ulong CyclesPerSecond
        {
            [NoHeapAllocation]
            get { return GetCurrentProcessor().cyclesPerSecond; }

            [NoHeapAllocation]
            set { GetCurrentProcessor().cyclesPerSecond = value; }
        }

        public static ulong CycleCount
        {
            [NoHeapAllocation]
            get { return GetCycleCount(); }
        }

        //////////////////////////////////////////////////////////////////////
        //
        //
#if SAMPLE_PC
        #if SINGULARITY_MP
        // This should be easy to fix.
        #error "SAMPLE_PC does not work in conjunction with SINGULARITY_MP."
        #endif

        const int pcLog2Samples = 16;
        const int pcMaxSamples  = 2 << pcLog2Samples;
        const int sampleMask    = pcMaxSamples - 1;

        static bool nextSampleIdle = false;

        static UIntPtr[] pcSamples = null;
        static int pcHead          = 0;    // head position
        static int pcLength        = 0;    // length of live data in buffer

        static uint pcSamplesI = 0;
        static ulong pcSamplesCounterThen;
#endif // SAMPLE_PC

        [NoHeapAllocation]
        public static bool SamplingEnabled()
        {
#if SAMPLE_PC
            return true;
#else
            return false;
#endif // SAMPLE_PC
        }

        internal static void StartSampling()
        {
#if SAMPLE_PC
            pcSamplesCounterThen = Processor.CycleCount;
            pcSamples = new UIntPtr[2 << pcLog2Samples];
            nextSampleIdle = false;
#endif // SAMPLE_PC
        }

        [NoHeapAllocation]
        internal static void NextSampleIsIdle()
        {
#if SAMPLE_PC
            nextSampleIdle = true;
#endif // SAMPLE_PC
        }

        //////////////////////////////////////////////////// External Methods.
        //
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern ulong GetCycleCount();

        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern UIntPtr GetFrameEip(UIntPtr ebp);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern UIntPtr GetFrameEbp(UIntPtr ebp);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern UIntPtr GetStackPointer();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern UIntPtr GetFramePointer();

        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        private static extern Processor GetCurrentProcessor();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern ThreadContext * GetCurrentThreadContext();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static unsafe extern ProcessorContext * GetCurrentProcessorContext();

        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern Thread GetCurrentThread();

        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern void SetCurrentThreadContext(ref ThreadContext context);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.GCFRIEND)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern void SwitchToThreadContext(ref ThreadContext oldContext, ref ThreadContext newContext);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern void SwitchToThreadContextNoGC(ref ThreadContext newContext);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern void TestSaveLoad(ref ThreadContext newContext);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern void TestSave(ref ThreadContext newContext);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern void EnterRing3();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(32)]
        [NoHeapAllocation]
        internal static extern bool AtKernelPrivilege();

        //////////////////////////////////////////////////////////////////////
        //
        //
        // These methods are currently marked external because they are used
        // by device drivers.  We need a tool to verify that device drivers
        // are in fact using them correctly!
        //
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern bool DisableInterrupts();

        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern void RestoreInterrupts(bool enabled);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern void SetIdtTable();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        internal static extern void ClearIdtTable();

        // Use this method for assertions only!
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern bool InterruptsDisabled();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern void HaltUntilInterruptNative();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern void InitFpu();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        private static extern uint ReadFpuStatus();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        private static extern void ClearFpuStatus();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern void WriteMsr(uint offset,
                                           ulong value);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern ulong ReadMsr(uint offset);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        public static extern ulong ReadPmc(uint offset);

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(64)]
        [NoHeapAllocation]
        public static extern void ReadCpuid(uint feature,
                                            out uint v0,
                                            out uint v1,
                                            out uint v2,
                                            out uint v3);

#if DO_FXSAVE_TEST
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(128)]
        [NoHeapAllocation]
        public static extern int TestFxsave();
#endif

#if DO_FS0_TEST
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(128)]
        [NoHeapAllocation]
        public static extern int TestFs0();
#endif

#if DO_CLI_STI_TEST
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(128)]
        [NoHeapAllocation]
        public static extern int TestCliSti();
#endif

        //////////////////////////////////////////////////////////////////////
        //

        /// <summary>
        /// Hardware exceptions are mapped to vectors 0x00..0x1f.
        /// They always use a processor context record.
        /// </summary>
        [AccessedByRuntime("referenced from Processor.cpp")]
        [NoHeapAllocation]
        internal static void DispatchException(int interrupt,
                                               ref ThreadContext context)
        {
            //            Tracing.Log(Tracing.Debug, "Exception {0}", (uint)interrupt);
            GetCurrentProcessor().DispatchSpecificException(interrupt, ref context);
        }

        [NoHeapAllocation]
        private void DispatchSpecificException(int interrupt,
                                               ref ThreadContext context)
        {
            // Indicate that we are in an interrupt context.
            bool wasInInterrupt = inInterruptContext;
            inInterruptContext = true;

            NumExceptions++;
            interruptCounts[interrupt]++;

            if (interrupt == EVectors.Nmi && MpExecution.FreezeRequested) {
                MpExecution.FreezeProcessor(ref context);
            }
            else if (interrupt == EVectors.SingleStep) {
                DebugStub.Trap(ref context, false);
            }
            else if (interrupt == EVectors.Breakpoint) {
                DebugStub.Trap(ref context, false);
            }
            else if (interrupt == EVectors.FirstChanceException) {
                DebugStub.Trap(ref context, true);
            }
            else if (interrupt == EVectors.FpuMathFault) {
                if ((context.mmx.fsw & Fpsw.StackFaultError) != 0) {
                    if ((context.mmx.fsw & Fpsw.C1) != 0) {
                        DebugStub.WriteLine("FPU Stack Overflow Exception (FP SW = 0x{0:x4})",
                                            __arglist(context.mmx.fsw));
                    }
                    else {
                        DebugStub.WriteLine("FPU Stack Underflow Exception (FP SW = 0x{0:x4})",
                                            __arglist(context.mmx.fsw));
                    }
                }
                else {
                    DebugStub.WriteLine("FPU Unit exception (FP SW = 0x{0:x4})",
                                        __arglist(context.mmx.fsw));
                }

                DebugStub.Trap(ref context, false);
                context.mmx.fcw |= Fpsw.ErrorClearMask;
            }
            else {
                Tracing.Log(Tracing.Debug,
                            "No recognized exception handler (0x{0:x2}",
                            (uint)interrupt);
                DebugStub.Trap(ref context, false);

                DebugStub.WriteLine("No recognized exception handler (0x{0:x2}",
                                    __arglist(interrupt));
                Thread.Display(ref context, "");
            }
            inInterruptContext = wasInInterrupt;
        }

        /// <summary>
        /// Hardware interrupts are mapped to vectors 0x20..0xff.
        /// They always use the thread's context record.
        /// </summary>
        [AccessedByRuntime("referenced from Processor.cpp")]
        [NoHeapAllocation]
        internal static void DispatchInterrupt(int interrupt,
                                               ref ThreadContext context)
        {
            GetCurrentProcessor().DispatchSpecificInterrupt(interrupt, ref context);
        }

        [NoHeapAllocation]
        private unsafe void DispatchSpecificInterrupt(int interrupt,
                                                      ref ThreadContext context)
        {
            // Indicate that we are in an interrupt context.
            Thread target = null;
            bool wasInInterrupt = inInterruptContext;
            inInterruptContext = true;

            Kernel.Waypoint(801);

            NumInterrupts++;
            NumExceptions++;
            interruptCounts[interrupt]++;

            // Don't generate loads of output for debugger-related interrupts
#if DEBUG_INTERRUPTS
            DebugStub.WriteLine("Int{0:x2}", __arglist(interrupt));
            Thread.DisplayAbbrev(ref context, " int beg");
            Thread.Display(ref context, " int beg");

            if (!context.IsFirst()) {
                DebugStub.WriteLine("*-*-*-*-*-* !IsFirst *-*-*-*-*-*");
                Thread.Display(ref context, " !IsFirst");
            }
#endif

#if SAMPLE_PC
            // Sample PC values
            ulong pcNow = Processor.CycleCount;
            uint pcDiff = unchecked((uint)(pcNow - pcSamplesCounterThen));
            if (pcSamples != null && pcDiff != 0 && nextSampleIdle == false) {
                int oldPcHead = pcHead;

                // Sample instance number
                pcSamples[pcHead] = (UIntPtr)unchecked(pcSamplesI++);
                pcHead = unchecked((pcHead + 1) & sampleMask);

                // Save relative time in ticks
                pcSamples[pcHead] = pcDiff;
                pcHead = unchecked((pcHead + 1) & sampleMask);

                pcSamples[pcHead] = (UIntPtr)interrupt;
                pcHead = unchecked((pcHead + 1) & sampleMask);

                pcSamplesCounterThen = pcNow;

                // Save stack
                UIntPtr eip = context.eip;
                UIntPtr ebp = context.ebp;
                while (true) {
                    if (eip == 0) { break; }
                    pcSamples[pcHead] = eip;
                    pcHead = (pcHead + 1) & sampleMask;
                    if (ebp == 0) { break; }
                    eip = Processor.GetFrameEip(ebp);
                    ebp = Processor.GetFrameEbp(ebp);
                }
                pcSamples[pcHead] = 0;
                pcHead = (pcHead + 1) & sampleMask;

                pcLength += (pcHead + pcMaxSamples - oldPcHead) & sampleMask;
                if (pcLength > pcMaxSamples)
                    pcLength = pcMaxSamples;

                // Clear any partially overwritten sample set ahead
                int i = pcHead;
                while (pcSamples[i] != 0) {
                    pcSamples[i] = 0;
                    pcLength--;
                    i = (i + 1) & sampleMask;
                }
            }
            nextSampleIdle = false;
#endif // SAMPLE_PC


            if (halted) {
                clock.CpuResumeFromHaltEvent();
                halted = false;
            }

            unchecked {
                if (interrupt != clockInterrupt) {
                    // We don't log the clockInterrupt because of all the spew.
                    Tracing.Log(Tracing.Debug, "Interrupt 0x{0:x}, count={1:x}",
                               (UIntPtr)(uint)interrupt,
                                (UIntPtr) interruptCounts[interrupt]
                               );
                }
            }

            Monitoring.Log(Monitoring.Provider.Processor,
                           (ushort)ProcessorEvent.Interrupt, 0,
                           (uint)interrupt, 0, 0, 0, 0);


            if ((context.efl & EFlags.IF) == 0) {
                DebugStub.WriteLine("Int{0:x2}", __arglist(interrupt));
                Thread.DisplayAbbrev(ref context, " int beg");
                DebugStub.WriteLine("Interrupt 0x{0:x2}  thrown while interrupts disabled.",
                                    __arglist(interrupt));
                DebugStub.Break();
            }


            if (interrupt == timerInterrupt) {
                timer.ClearInterrupt();
                SchedulerTime now = SchedulerTime.Now;
                Scheduler.DispatchLock();
                try {
                    if (context.thread == IdleThread) {
#if TRACE_INTERRUPTS
                        Tracing.Log(Tracing.Audit,
                                    "Timer interrupt in idle.");
#endif // TRACE_INTERRUPTS
                        target = Scheduler.OnTimerInterrupt(null, now);
                    }
                    else {
#if TRACE_INTERRUPTS
                        Tracing.Log(Tracing.Audit,
                                    "Timer interrupt in tid={0:x3}.",
                                    (uint)context.thread.GetThreadId());
#endif // TRACE_INTERRUPTS
                        target = Scheduler.OnTimerInterrupt(context.thread, now);
                    }
                    Scheduler.SelectingThread(target);
#if DEBUG_DISPATCH_TIMER
                    DebugStub.WriteLine("OnTime.Selecting");
#endif // DEBUG_DISPATCH_TIMER
                }
                finally {
                    Scheduler.DispatchRelease();
                }
            }
            else if (interrupt == clockInterrupt) {
                clock.ClearInterrupt();

#if DEBUG
                // Check for a debug break.
                if (DebugStub.PollForBreak()) {
                    DebugStub.WriteLine("Debugger ctrl-break after interrupt 0x{0:x2}",
                                        __arglist(interrupt));
                    MpExecution.FreezeAllProcessors();
                    DebugStub.Break(); // used to be: Trap(ref context, false);
                    MpExecution.ThawAllProcessors();
                }
#endif
            }
            else if (interrupt == EVectors.PingPongInt) {
                HalDevices.ClearFixedIPI();

                inInterruptContext = false;

                int task = MpExecution.GetIntrPingPong(this.Id);
                DebugStub.WriteLine
                    ("HSG: ***** cpu.{0} gets  {1}", __arglist(this.Id, task));

                RunPingPongInt(task-1); // send again
                inInterruptContext = true;
            }
            else if (interrupt == EVectors.ApImage) {
                HalDevices.ClearFixedIPI();
                inInterruptContext = false;
                MpExecution.ApImage apImage;
                bool okay = MpExecution.GetIntrApImage(this.Id, out apImage);
                if (!okay) {
                    DebugStub.WriteLine("ERROR: ApImage queue is empty");
                    DebugStub.Break();
                }

                inInterruptContext = true;

                DebugStub.WriteLine ("HSG: ** cpu.{0} gets e.{1:x}",
                                     __arglist(this.Id,
                                               apImage.entryPoint));

                Processor.MpCallEntryPoint(apImage.entryPoint);
            }
            else if (interrupt == EVectors.AbiCall) {
                HalDevices.ClearFixedIPI();
#if GENERATE_ABI_SHIM
                // haryadi: tells AP service thread to process
                // this abi call later
                ApServiceThread.abiEvent.Set();
#endif
            }
            else if (interrupt == EVectors.HaltApProcessors) {
                Processor p = Processor.CurrentProcessor;
                if (p.Id != 0) {
                    if (context.thread != IdleThread) {
                        Scheduler.DispatchLock();
                        try {
                            Scheduler.OnProcessorShutdown(context.thread);
                        }
                        finally {
                            Scheduler.DispatchRelease();
                        }
                    }
                    p.Uninitialize(p.Id);
                }
            }
            else if (!HalDevices.InternalInterrupt((byte)interrupt)) {
                IoIrq.SignalInterrupt(pic.InterruptToIrq((byte)interrupt));
                pic.ClearInterrupt((byte)interrupt);

                Scheduler.DispatchLock();
                try {
                    if (context.thread == IdleThread) {
#if  TRACE_INTERRUPTS
                        Tracing.Log(Tracing.Audit,
                                    "I/O interrupt in idle.");
#endif // TRACE_INTERRUPTS
                        target = Scheduler.OnIoInterrupt(null);
                    }
                    else {
#if  TRACE_INTERRUPTS
                        Tracing.Log(Tracing.Audit,
                                    "I/O interrupt in tid={0:x3}.",
                                    (uint)context.thread.GetThreadId());
#endif // TRACE_INTERRUPTS
                        target = Scheduler.OnIoInterrupt(context.thread);
                    }
#if DEBUG_DISPATCH_IO
                    DebugStub.WriteLine("++DispatchInterruptEvent Irq={0:x2}, Thread={1:x8}",
                                        __arglist(pic.InterruptToIrq((byte)interrupt),
                                                  Kernel.AddressOf(target)));
#endif // DEBUG_DISPATCH_IO
                    Scheduler.SelectingThread(target);
                }
                finally {
                    Scheduler.DispatchRelease();
                }
            }

#if DEBUG_INTERRUPTS
            DebugStub.WriteLine("Int{0:x2}", __arglist(interrupt));
            Thread.DisplayAbbrev(ref context, " int fin");
            if (!InterruptsDisabled()) {
                DebugStub.WriteLine("        interrupts enabled!!!!!!!");
                Thread.Display(ref context, " !IsFirst");
                DebugStub.Break();
            }
#if DEBUG_DEEPER
            DebugStub.WriteLine("Int{0:x2}", __arglist(interrupt));
            Thread.DisplayAbbrev(ref context, " int end");
#endif
#endif

            // if (context.lastExecutionTimeUpdate !=
            //     context.thread.context.lastExecutionTimeUpdate) {
            //     DebugStub.WriteLine("context != context.thread.context: {0}, {1}, ",
            //     __arglist(context.lastExecutionTimeUpdate,
            //     context.thread.context.lastExecutionTimeUpdate));
            // }

#if THREAD_TIME_ACCOUNTING
            ulong now_a = Processor.CycleCount;
            context.executionTime += now_a -
                context.lastExecutionTimeUpdate;
#endif

            if (target != null) {
#if THREAD_TIME_ACCOUNTING
                target.context.lastExecutionTimeUpdate = now_a;
#endif
                Monitoring.Log(Monitoring.Provider.Processor,
                               (ushort)ProcessorEvent.Resume, 0,
                               (uint)target.context.threadIndex, 0, 0, 0, 0);
                // Return to the thread selected by the scheduler if any.
                SetCurrentThreadContext(ref target.context);
            }
            else {
#if THREAD_TIME_ACCOUNTING
                context.lastExecutionTimeUpdate = now_a;
#endif
                Monitoring.Log(Monitoring.Provider.Processor,
                               (ushort)ProcessorEvent.Resume, 0,
                               (uint)context.threadIndex, 0, 0, 0, 0);
                SetCurrentThreadContext(ref context);
            }

            inInterruptContext = wasInInterrupt;
        }

        //Simulator Use
        //Sets the nextTimerInterrupt
        [NoHeapAllocation]
        public bool SetNextTimerInterrupt(TimeSpan delta)
        {
            long span = delta.Ticks;
            if (span > timer.MaxInterruptInterval) {
                span = timer.MaxInterruptInterval;
            }
            else if (span < timer.MinInterruptInterval) {
                span = timer.MinInterruptInterval;
            }

            bool success = timer.SetNextInterrupt(span);
            DebugStub.Assert(success);
            return success;
        }

        [NoHeapAllocation]
        public bool SetNextTimerInterrupt(SchedulerTime until)
        {
            return SetNextTimerInterrupt(until - SchedulerTime.Now);
        }

        [NoHeapAllocation]
        internal static unsafe void UpdateAfterGC(Thread currentThread)
        {
            // Update the processor pointers in processor contexts
            for (int i = 0; i < Processor.processorTable.Length; i++) {
                Processor p = Processor.processorTable[i];
                if (p != null) {
                    p.context->UpdateAfterGC(p);
                }
            }
            // Ensure that Thread.CurrentThread returns new thread object
            SetCurrentThreadContext(ref currentThread.context);
        }


        //////////////////////////////////////////////////////////////////////
        //
        //
        // These methods are public and safe to use from any where provided
        // there's at least 2 call frame on the stack.
        //
        [NoHeapAllocation]
        public static UIntPtr GetCallerEip()
        {
            UIntPtr currentFrame = GetFramePointer();
            UIntPtr callerFrame = GetFrameEbp(currentFrame);
            if (callerFrame == UIntPtr.Zero) {
                return UIntPtr.Zero;
            }
            UIntPtr callersCaller = GetFrameEip(callerFrame);
            return callersCaller;
        }

        /// <summary>
        /// Provides a mini stack trace starting from the caller of the caller
        /// of this method.
        /// </summary>
        [NoHeapAllocation]
        public static void GetStackEips(out UIntPtr pc1, out UIntPtr pc2, out UIntPtr pc3)
        {
            pc1 = UIntPtr.Zero;
            pc2 = UIntPtr.Zero;
            pc3 = UIntPtr.Zero;

            UIntPtr currentFrame = GetFramePointer();
            UIntPtr callerFrame = GetFrameEbp(currentFrame);
            if (callerFrame == UIntPtr.Zero) {
                return;
            }
            pc1 = GetFrameEip(callerFrame);
            callerFrame = GetFrameEbp(callerFrame);
            if (callerFrame == UIntPtr.Zero) {
                return;
            }
            pc2 = GetFrameEip(callerFrame);
            callerFrame = GetFrameEbp(callerFrame);
            if (callerFrame == UIntPtr.Zero) {
                return;
            }
            pc3 = GetFrameEip(callerFrame);
        }

        /// <summary>
        /// Provides the full stack trace starting from the caller of the caller
        /// of this method.
        /// </summary>
        /// <returns>Eip values in stack array from top to bottom</returns>
        [NoHeapAllocation]
        public static void GetStackEips(UIntPtr[] stack)
        {
            if (stack == null) {
                return;
            }
            UIntPtr currentFrame = GetFramePointer();
            UIntPtr callerFrame = GetFrameEbp(currentFrame);
            for (int index = 0; callerFrame != UIntPtr.Zero && index < stack.Length; index++) {
                stack[index] = GetFrameEip(callerFrame);
                callerFrame = GetFrameEbp(callerFrame);
            }
        }

        //////////////////////////////////////////////////////////////////////
        //
        //
        // These (native) methods manipulate the local processor's paging
        // hardware. They can be used even before Processor.Initialize()
        // has been called.
        //
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        private static extern void PrivateEnablePaging(uint pdpt);

        internal static void EnablePaging(AddressSpace bootstrapSpace)
        {
            PrivateEnablePaging((uint)bootstrapSpace.PdptPage.Value);
        }

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        private static extern void PrivateChangeAddressSpace(uint pdpt);

        internal static void ChangeAddressSpace(AddressSpace space)
        {
            PrivateChangeAddressSpace((uint)space.PdptPage.Value);
        }

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        internal static extern void DisablePaging();

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        private static extern void PrivateInvalidateTLBEntry(UIntPtr pageAddr);

        internal static void InvalidateTLBEntry(UIntPtr pageAddr)
        {
            DebugStub.Assert(MemoryManager.IsPageAligned(pageAddr));
            PrivateInvalidateTLBEntry(pageAddr);
        }

        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(16)]
        [NoHeapAllocation]
        private static extern uint GetCr3();

        // haryadi
        [AccessedByRuntime("output to header : defined in Processor.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [GCAnnotation(GCOption.NOGC)]
        [StackBound(64)]
        [NoHeapAllocation]
        public static extern void MpCallEntryPoint(UIntPtr entry);

        internal static AddressSpace GetCurrentAddressSpace()
        {
            return new AddressSpace(new PhysicalAddress(GetCr3()));
        }

        //
        public static IHalMemory.ProcessorAffinity[] GetProcessorAffinity()
        {
            IHalMemory.ProcessorAffinity[] processors =
                halMemory.GetProcessorAffinity();
            return processors;
        }

        public static IHalMemory.MemoryAffinity[] GetMemoryAffinity()
        {
            IHalMemory.MemoryAffinity[] memories =
                halMemory.GetMemoryAffinity();
            return memories;
        }

        /// <summary> Start application processors. </summary>
        [System.Diagnostics.Conditional("SINGULARITY_MP")]
        public static void StartApProcessors()
        {
            // At this point only the BSP is running.

            Tracing.Log(Tracing.Debug, "Processor.StartApProcessors()");
            HalDevices.StartApProcessors();

            // At this point the BSP and APs are running.
        }

        /// <summary> Stop application processors. </summary>
        [NoHeapAllocation]
        [System.Diagnostics.Conditional("SINGULARITY_MP")]
        public static void StopApProcessors()
        {
            // At this point the BSP and APs are running.

            Tracing.Log(Tracing.Debug, "Processor.StopApProcessors()");
            HalDevices.BroadcastFixedIPI((byte)EVectors.HaltApProcessors,
                                         true);

            while (GetRunningProcessorCount() != 1) {
                /* Thread.Sleep(100); Thread.Sleep needs NoHeapAllocation annotation */
                Thread.Yield();
            }

            DebugStub.RevertToUniprocessor();

            // At this point only the BSP is running.
        }

        /// <summary> Gets the number of processors in use by
        /// the system. </summary>
        [NoHeapAllocation]
        public static int GetRunningProcessorCount()
        {
            return runningCpus;
        }

        /// <summary> Gets the total number of processors known
        /// to the system.  This includes processors not
        /// currently in use by the system. </summary>
        [NoHeapAllocation]
        public static int GetProcessorCount()
        {
            return HalDevices.GetProcessorCount();
        }

        public static int GetDomainCount()
        {
            IHalMemory.ProcessorAffinity[] processors =
                halMemory.GetProcessorAffinity();
            uint domain = 0;
            for (int i = 0; i < processors.Length; i++) {
                if (processors[i].domain > domain) {
                    domain = processors[i].domain;
                }
            }
            domain++; // domain number starts from 0
            return (int)domain;
        }

        public static bool HasAffinityInfo()
        {
            IHalMemory.ProcessorAffinity[] processors =
                halMemory.GetProcessorAffinity();
            IHalMemory.MemoryAffinity[] memories =
                halMemory.GetMemoryAffinity();
            if (processors == null || memories == null) {
                return false;
            }
            return true;
        }

        // haryadi -- return cpuId if context is not null
        [NoHeapAllocation]
        public unsafe int ValidProcessorId(int i)
        {
            if (context != null) {
                return context->cpuId;
            }
            else {
                return -1;
            }
        }

        // haryadi -- determine next ping pong receiver
        //            i.e. find next processor, if this is
        //            the last processors, wrap to p0

        [NoHeapAllocation]
        private static int GetPingPongReceiver()
        {
            int from = GetCurrentProcessorId();
            int to;

            // check if processor table is valid
            if (processorTable == null || processorTable.Length == 1) {
                DebugStub.WriteLine
                    ("HSG: Processors.cs, procTable null/uniproc  ...");
                return -1;
            }

            // set up neighbor
            to = (from + 1) % processorTable.Length;

            // check if processor is valid
            if (processorTable[to].ValidProcessorId(to) >= 0) {
                to = processorTable[to].ValidProcessorId(to);
            }
            // this is the last processors, wrap to p0
            else {
                // DebugStub.WriteLine
                // ("HSG: Processors.cs, cpu.{0} is the last processor",
                // __arglist(from));
                to = 0;
                to = processorTable[to].ValidProcessorId(to);
                if (to == from) {
                    DebugStub.WriteLine
                        ("HSG: Processor.cs, only 1 valid processor (error)");
                    return -1;
                }
            }
            return to;
        }

        // haryadi -- start ping pong
        [NoHeapAllocation]
        public static int RunPingPongInt(int start)
        {
            // Done ping pong
            if (start == 0) {
                return -1;
            }

            int from = GetCurrentProcessorId();
            int to   = GetPingPongReceiver();

            // invalid to (or no destination processor)
            if (to < 0) {
                return -1;
            }

            // from and to are ready now
            DebugStub.WriteLine
                ("HSG: ***** cpu.{0} sends {1} to p{2}",
                 __arglist(from, start, to));

            // disable interrupt
            bool iflag = Processor.DisableInterrupts();

            // put integer "to" to neighbor interrupt task queue
            MpExecution.PutIntrPingPong(to, start);

            // send ping pong
            MpExecution.StartPingPongInt(from, to, (byte)EVectors.PingPongInt);

            // enable interrupt
            Processor.RestoreInterrupts(iflag);

            // DebugStub.WriteLine
            // ("HSG: Processors.cs, cpu.{0} sent to p{1} (pc:{2}) ",
            // __arglist(from, to, start));

            return 0;
        }
    }
}
