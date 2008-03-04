///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Microsoft.Bartok.Runtime;

using Microsoft.SingSharp;
using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Drivers;
using Microsoft.Singularity.Hal;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Loader;
using Microsoft.Singularity.Memory;
using Microsoft.Singularity.Scheduling;

using Microsoft.Singularity.Directory;
using Microsoft.Singularity.Security;
using Microsoft.Singularity.Xml;
using Microsoft.Singularity.V1.Services;

[assembly: AssemblyTitle("Microsoft.Singularity")]
[assembly: AssemblyProduct("Microsoft Research Singularity Operating System")]
[assembly: AssemblyCompany("Microsoft Corporation")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyKeyFile("public.snk")]
[assembly: AssemblyDelaySign(true)]

namespace Microsoft.Singularity
{
    [NoCCtor]
    [CLSCompliant(false)]
    public class Kernel
    {
        private static string[] args;
        private static ManualResetEvent mpEndEvent;
        private static int bootReturnCode;

        #region Waypoints

        public static long[]    Waypoints;
        public static int[]     WaypointSeq;
        public static int[]     WaypointThd;
        public static int       WaypointNumber;// = 0;
        public static long      WaypointSamples;// = 0;
        public static bool      WaypointSuspicious;
        public static uint      WaypointInterrupt;
        public const int        NWP = 2048; // XXX if this is readonly then it seems to read as 0!

        [Conditional("WAYPOINTS")]
        [NoHeapAllocation]
        public static void Waypoint0()
        {
            WaypointSamples++;
            Waypoints[0] = (long)Processor.CycleCount;
            WaypointNumber = 1;
            WaypointSuspicious = false;
            WaypointInterrupt = Processor.CurrentProcessor.NumInterrupts;
        }

        [Conditional("WAYPOINTS")]
        [NoHeapAllocation]
        public static void Waypoint(int num)
        {
            if (Waypoints == null || Waypoints[0] == 0 || WaypointNumber > NWP-1) {
                return;
            }

            long delta = (long)Processor.CycleCount - Waypoints[0];
            if (delta > 7000000) {
                WaypointSuspicious = true;
            }

            WaypointSeq[WaypointNumber] = num;
            WaypointThd[WaypointNumber] = Thread.GetCurrentThreadIndex();
            Waypoints[WaypointNumber++] = delta;
        }

        [Conditional("WAYPOINTS")]
        [NoHeapAllocation]
        public static void WaypointDone()
        {
            Waypoints[0] = 0;
        }

#if false
        [Conditional("WAYPOINTS")]
        [NoHeapAllocation]
        public static void WaypointDump()
        {
            WaypointDump(NWP);
        }
#endif

        [Conditional("WAYPOINTS")]
        public static void WaypointDump()
        {
            bool iflag = Processor.DisableInterrupts();

            DebugStub.WriteLine("Interrupts: {0}",
                                __arglist(Processor.CurrentProcessor.NumInterrupts
                                          - WaypointInterrupt));
            DebugStub.WriteLine("WPT Waypoint   Sequence   THD Diff");

            for (int i = 1; i < WaypointNumber; i++) {
                DebugStub.WriteLine("{0,3:d} {1,10:d} {2,10:d} {3,3:d} {4,10:d}",
                                    __arglist(
                                        i,
                                        Waypoints[i],
                                        WaypointSeq[i],
                                        WaypointThd[i].GetHashCode(),
                                        Waypoints[i] - Kernel.Waypoints[i-1]));
            }
            Processor.RestoreInterrupts(iflag);
        }

        [Conditional("WAYPOINTS")]
        [NoHeapAllocation]
        public static void WaypointReset()
        {
            for (int i = 0; i < NWP; i++) {
                Waypoints[i] = 0;
            }
            WaypointSamples = 0;
        }
        #endregion // Waypoints


        // Note: This function is called by Hal.cpp.
        [AccessedByRuntime("referenced from hal.cpp")]
        internal static int Main()
        {
            bootReturnCode = BootInfo.EXIT_AND_RESTART;

            DebugStub.WriteLine("Kernel.Main()");

            // Initialize the memory subsystem. This turns on paging!
            MemoryManager.Initialize();

            // Note for Monitoring early boot process:
            // if you ever want to monitor stuff before this point, you should
            // allocate a static memory area in BootInit.cs, init the
            // monitoring system earlier, hold the system at this point here,
            // copy over all the collected data up to now to the new
            // dynamically created buffer and continue
            Monitoring.Initialize();  // uses page memory

            HandleTable.Initialize();
            Stacks.Initialize();

            try {
                // Initialize the rest of the primitive runtime.
                VTable.Initialize((RuntimeType)typeof(Kernel));

#if PAGING
                // Must occur before MemoryManager.PostGCInitialize()
                ProtectionDomain.Initialize();
#endif

                // Must occur before SharedHeap.Initialize()
                MemoryManager.PostGCInitialize();

                SharedHeap.Initialize();

#if PAGING
                // Must occur after SharedHeap.Initialize();
                ProtectionDomain.DefaultDomain.InitHook();
#endif

                args = GetCommandLine();
                VTable.ParseArgs(args);

                // Initialize the processor table.
                InitType(typeof(Processor));
                InitType(typeof(Scheduler));
                InitType(typeof(MinScheduler));

                Processor.InitializeProcessorTable();

                Tracing.Log(Tracing.Audit, "processor");
                Processor processor = Processor.processorTable[0];
                processor.Initialize(0);

                PEImage.Initialize();

                // initialize endpoints
                InitType(typeof(Microsoft.Singularity.Channels.EndpointCore));

                // get the system manifest
                XmlReader xmlReader = new XmlReader(Binder.GetSystemManifest());
                XmlNode xmlData = xmlReader.Parse();
                XmlNode manifestRoot = xmlData.GetChild("system");
                XmlNode initConfig = manifestRoot.GetChild("initConfig");

                PerfCounters.Initialize();
                // need to have processed the manifest before we can call Process initialize
                PrincipalImpl.Initialize(initConfig);
                Process.Initialize(manifestRoot.GetChild("processConfig"));


                // obtain the configuration for the namespace service
                // and initialize the namespace service
                DirectoryService.Initialize(initConfig);

                Tracing.Log(Tracing.Audit, "IoSystem");
                IoSystem.Initialize(
                    manifestRoot.GetChild("drivers"));

                Tracing.Log(Tracing.Audit, "Registering HAL Drivers.");
                Devices.RegisterPnpResources(); // add the root device

                HalDevices.Initialize(processor); //

#if SINGULARITY_MP
                // haryadi -- at this point we have the SRAT table,
                // Create per-processor address space now
                MemoryManager.InitializeProcessorAddressSpace();
#endif

                // From here on, we want lazy type initialization to worry about
                // competing threads.
                VTable.InitializeForMultipleThread();

                Console.WriteLine("Running C# Kernel of {0}", GetLinkDate());

                Console.WriteLine();

                Console.WriteLine("Initializing Scheduler");
                MinScheduler.Initialize(Process.idleProcess);


                DirectoryService.StartNotificationThread();

                Console.WriteLine("Initializing Shared Heap Walker");
                Process.InitializeSharedHeapWalker();

                Console.WriteLine("Initializing Service Thread");
                ServiceThread.Initialize();

#if GENERATE_ABI_SHIM
                // haryadi: add Ap service thread
                Console.WriteLine("Initializing AP Service Thread");
                ApServiceThread.Initialize();
#endif

                Tracing.Log(Tracing.Audit, "Enabling GC Heap");
                GC.EnableHeap();

                Tracing.Log(Tracing.Audit, "Waypoints init");
                Waypoints = new long[2048];
                WaypointSeq = new int[2048];
                WaypointThd = new int[2048];

                Tracing.Log(Tracing.Audit, "Interrupts ON.");
                Processor.RestoreInterrupts(true);

                Tracing.Log(Tracing.Audit, "Starting Security Service channels");
                PrincipalImpl.Export();

                Tracing.Log(Tracing.Audit, "Creating Root Directory.");
                IoSystem.InitializeDirectoryService();
                // [TODO]turn off kernel interfaces to namespace here

                Tracing.Log(Tracing.Audit, "Binder");
                Binder.Initialize(
                    manifestRoot.GetChild("namingConventions"));

                Tracing.Log(Tracing.Audit, "Creating console.");
                ConsoleOutput.Initialize();

                // Initialize MP after Binder and ConsoleOutput
                // are initialized so there are no
                // initialization races if the additional
                // threads try to use them.
                Tracing.Log(Tracing.Audit, "Starting additional processors");
                MpExecution.Initialize();
                mpEndEvent = new ManualResetEvent(false);

                Processor.StartApProcessors();

                Tracing.Log(Tracing.Audit, "Initializing Volume Manager.");
                IoSystem.InitializeVolumeManager();

                // Register drivers who depend on scheduling and resource
                // management.
                DebugStub.WriteLine("--- Registering Drivers ---------------------------");
                Console.WriteLine("Registering Non-HAL Drivers.");
                // register the metadata-based drivers
                IoSystem.RegisterDrivers();
                // register the internal kernel drivers
                Devices.RegisterInternalDrivers();
                NvPciLpcBridge.Register();
#if DEBUG
                // and output the results
                IoSystem.Dump(false);
#endif

                DebugStub.WriteLine("--- Activating Devices ----------------------------");
                // now do device initialization
                IoSystem.ActivateDrivers();
#if DEBUG
                // and output the results
                IoSystem.Dump(true);
#endif

                Tracing.Log(Tracing.Audit, "Initializing Service Manager.");
                IoSystem.InitializeServiceManager(manifestRoot.GetChild("serviceConfig"));

                // Start up the kernel's diagnostics module
                Console.WriteLine("Starting diagnostics module...");
                Diagnostics.DiagnosticsModule.Initialize();

                // Start up the kernel's stress test module
                Console.WriteLine("Initializing stress module...");
                Stress.StressService.Initialize();

                Processor.StartSampling();

                // Consider boot successful at this stage.
                bootReturnCode = BootInfo.EXIT_AND_SHUTDOWN;

                DebugStub.WriteLine("----------------------------------------------------------");
                DebugStub.WriteLine("RdTsc, Halt, RdTsc");
                ulong beg;
                ulong end;
                beg = Processor.CycleCount;
                Processor.HaltUntilInterrupt();
                end = Processor.CycleCount;

                DebugStub.WriteLine("  Begin: {0,16:d}", __arglist(beg));
                DebugStub.WriteLine("  End:   {0,16:d}", __arglist(end));
                DebugStub.WriteLine("  Diff:  {0,16:d}", __arglist(end - beg));
                DebugStub.WriteLine("----------------------------------------------------------");

                Tracing.Log(Tracing.Audit, "Creating Shell Process");
                int exit = -10000;
                Process process = null;
                IoMemory memory;
                Manifest manifest;

#if KERNEL_USE_LOGIN
                if (args[0] == "bvt") {
                    memory = Binder.LoadImage(Thread.CurrentProcess, "tty.x86", out manifest);
                }
                else{
                    // TODO: The login app needs to be fixed to setup stdin and stdout pipes for
                    // the shell and pump the data back and forth.
                    memory = Binder.LoadImage(Thread.CurrentProcess, "login.x86", out manifest);
                }
#else
                memory = Binder.LoadImage(Thread.CurrentProcess, "tty.x86", out manifest);
#endif

                if (memory != null && memory.Length > 0) {
                    String[] shellArgs = new String[args.Length + 2];
                    shellArgs[0] = "tty.x86";
                    shellArgs[1] = "shell.x86";
                    for (int i = 0; i < args.Length; i++) {
                        shellArgs[i + 2] = args[i];
                    }
                    process = new Process(Thread.CurrentProcess, memory, null, shellArgs, manifest);
                    if (process != null) {
                        process.Start();
                        process.Join();
                        exit = process.ExitCode;
                    }
                }

                switch (exit) {
                    case -10000:
                        Tracing.Log(Tracing.Audit, "Failed to start shell process.");
                        bootReturnCode = BootInfo.EXIT_AND_RESTART;
                        break;
                    case BootInfo.EXIT_AND_WARMBOOT:
                        bootReturnCode = BootInfo.EXIT_AND_WARMBOOT;
                        break;
                    case BootInfo.EXIT_AND_RESTART:
                        bootReturnCode = BootInfo.EXIT_AND_RESTART;
                        break;
                    case BootInfo.EXIT_AND_SHUTDOWN:
                        bootReturnCode = BootInfo.EXIT_AND_SHUTDOWN;
                        break;
                }

                Tracing.Log(Tracing.Audit, "Shutting down AP processors");
                Processor.StopApProcessors();

                Tracing.Log(Tracing.Audit, "Shutting down I/O system");
                Console.WriteLine("Shutting down I/O system");
                IoSystem.Finalize();

                Tracing.Log(Tracing.Audit, "Interrupts OFF.");
                Processor.DisableInterrupts();

                Tracing.Log(Tracing.Audit, "Shutting down scheduler");
                Console.WriteLine("Shutting down scheduler");
                MinScheduler.Finalize();

                // We should turn off interrupts here!
                HalDevices.Finalize();
                PEImage.Finalize();

                DebugStub.WriteLine("Kernel Exiting [{0}]",
                                    __arglist(bootReturnCode));
            }
            catch (Exception e) {
                Tracing.Log(Tracing.Fatal, "Caught exception {0}", e.Message);
                DebugStub.WriteLine("Caught {0}", __arglist(e.Message));
                bootReturnCode = -1;
                DebugStub.Break();
            }

            DebugStub.WriteLine("Kernel exiting with 0x{0:x4}",
                                __arglist(bootReturnCode));

            Stacks.Finalize();
            HandleTable.Finalize();
#if !PAGING
            SharedHeap.Finalize();
#endif //PAGING

            MemoryManager.Finalize();

            if (bootReturnCode != BootInfo.EXIT_AND_WARMBOOT) {
                Kill(bootReturnCode);
            }
            return bootReturnCode;
        }

        // Note: This function is entry point to the managed
        // kernel for CPU's other than the bootstrap processor.
        // It is called by Hal.cpp.
        [AccessedByRuntime("referenced from hal.cpp")]
        internal static int MpMain(int cpu)
        {
            Tracing.Log(Tracing.Audit, "processor");
            Processor processor = Processor.processorTable[cpu];
            processor.Initialize(cpu);
            HalDevices.Initialize(processor);

            Processor.CurrentProcessor.SetNextTimerInterrupt(TimeSpan.Zero);
            Processor.RestoreInterrupts(true);

            Tracing.Log(Tracing.Audit,
                        "Halting processor until first interrupt.");
            Processor.HaltUntilInterrupt();
            Tracing.Log(Tracing.Audit, "Resumed from halt.");

            // This probably won't be the
            // ultimate way of dissociating kernel entry threads
            // from the kernel.
            mpEndEvent.WaitOne();

            return 0;
        }

        // This function is called by the GC to locate all non-static object
        // references allocated somewhere other than the stack.
        internal static
        void VisitSpecialData(System.GCs.NonNullReferenceVisitor visitor)
        {
            Process.VisitSpecialData(visitor);
            visitor.VisitReferenceFields(Processor.processorTable);
        }

        // This function is called by GCs that move object to update all
        // object references contained in non-objects (i.e. unsafe structs).
        internal static void UpdateAfterGC(Thread currentThread)
        {
            Processor.UpdateAfterGC(currentThread);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(8)]
        [NoHeapAllocation]
        private static unsafe extern char * HalGetLinkDate();

        internal static unsafe string GetLinkDate()
        {
            return new String(HalGetLinkDate());
        }

        internal static unsafe string[] GetCommandLine()
        {
            String[] args =
                (new String((char *)(BootInfo.HalGetBootInfo()->CmdLine32))).Split(null);
            int dst = 0;
            for (int src = 0; src < args.Length; src++) {
                if (args[src] != null && args[src].Length > 0) {
                    args[dst++] = args[src];
                }
            }
            if (dst < args.Length) {
                String[] list = new String[dst];
                for (int i = 0; i < dst; i++) {
                    list[i] = args[i];
                }
                return list;
            }
            return args;
        }

        [NoHeapAllocation]
        public static void RequestWarmBoot()
        {
            bootReturnCode = BootInfo.EXIT_AND_WARMBOOT;
        }

        [NoHeapAllocation]
        public static void RequestRestart()
        {
            bootReturnCode = BootInfo.EXIT_AND_RESTART;
        }

        [NoHeapAllocation]
        public static void RequestShutdown()
        {
            bootReturnCode = BootInfo.EXIT_AND_SHUTDOWN;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(1024)]
        [NoHeapAllocation]
        private static extern void Kill(int exitCode);

        internal static void Shutdown(int exitCode)
        {
            unchecked {
                Tracing.Log(Tracing.Audit, "Kernel.Shutdown({0})", (UIntPtr)(uint)exitCode);
            }
            DebugStub.WriteLine("Kernel.Shutdown(0x{0:x4})",
                                __arglist(exitCode));
            DebugStub.Break();
            VTable.Shutdown(exitCode);
        }

        internal static void Panic(string why)
        {
            DebugStub.WriteLine("KERNEL PANIC: {0}", __arglist(why));
            Shutdown(BootInfo.EXIT_AND_HALT);
        }

        //////////////////////////////////////////////////////////////////////
        //
        [NoHeapAllocation]
        public static UIntPtr AddressOf(Object o)
        {
            return Magic.addressOf(o);
        }

        [NoHeapAllocation]
        public static UIntPtr SizeOf(Object o)
        {
            return System.GCs.ObjectLayout.Sizeof(o);
        }

        [NoHeapAllocation]
        public static UIntPtr AddressOf(ITracked tracked)
        {
            return Magic.addressOf(tracked);
        }

        [NoHeapAllocation]
        unsafe public static UIntPtr AddressOf(void * ptr)
        {
            return (UIntPtr)ptr;
        }

        public static void InitType(Type ty)
        {
            VTable.initType((RuntimeType) ty);
        }

        [NoHeapAllocation]
        public static string TypeName(Object o)
        {
            if (o == null) {
                return "null";
            }
            else {
                Type t = o.GetType();
                RuntimeType r = (RuntimeType)t;
                return r.Name;
            }
        }

        [NoHeapAllocation]
        public static string TypeNameSpace(Object o)
        {
            if (o == null) {
                return "null";
            }
            else {
                Type t = o.GetType();
                RuntimeType r = (RuntimeType)t;
                return r.Namespace;
            }
        }

        public static string FullTypeName(Object o)
        {
            if (o == null) {
                return "null";
            }
            else {
                Type t = o.GetType();
                RuntimeType r = (RuntimeType)t;
                return r.Namespace + "." + r.Name;
            }
        }

        public static void DumpPageTable()
        {
            System.GCs.PageTable.Dump("PageTable");
        }
    }
}
