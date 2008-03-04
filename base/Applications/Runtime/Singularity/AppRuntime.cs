////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Runtime.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Bartok.Runtime;

#if !MINRUNTIME
    using Microsoft.Singularity.Io;
#endif
    using Microsoft.Singularity.V1.Services;

[assembly: AssemblyTitle("Microsoft.Singularity")]
[assembly: AssemblyProduct("Microsoft Research Singularity Runtime")]
[assembly: AssemblyCompany("Microsoft Corporation")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyKeyFile("public.snk")]
[assembly: AssemblyDelaySign(true)]

namespace Microsoft.Singularity
{

    [NoCCtor]
    [CLSCompliant(false)]
    public class AppRuntime
    {
        // Note: This function is called by Hal.cpp.
        [AccessedByRuntime("referenced from hal.cpp")]
        public static unsafe int AppStart(Type userClass)
        {
            System.GCs.Transitions.ThreadStart();

            int result = 0;

            try {
                Tracing.Log(Tracing.Audit, "Runtime.Main()");

                // Initialize the primitive runtime, which calls the
                // class constructor for Runtime().
                VTable.Initialize((RuntimeType)typeof(AppRuntime));
                /*VTable.ParseArgs(args);*/

                Tracing.Log(Tracing.Audit, "Enabling GC Heap");
                GC.EnableHeap();

#if !MINRUNTIME
                ConsoleOutput.Initialize();
                ConsoleInput.Initialize();
#endif

                SetDebuggerPresence(DebugService.IsDebuggerPresent());

                int argCount = 0;
                int argMaxLen = 0;
                for (;; argCount++) {
                    int len = ProcessService.GetStartupArg(argCount, null, 0);
                    if (len == 0) {
                        break;
                    }
                    if (argMaxLen < len) {
                        argMaxLen = len;
                    }
                }
                char[] argArray = new char [argMaxLen];
                string[] args = new string[argCount];
                for (int arg = 0; arg < argCount; arg++) {
                    fixed (char *argptr = &argArray[0]) {
                        int len = ProcessService.GetStartupArg(arg,
                                                               argptr,
                                                               argArray.Length);
                        args[arg] = String.StringCTOR(argptr, 0, len);
                    }
                }

                if (userClass != null) {
                    VTable.initType((RuntimeType)userClass);
                }

                result = CallMain(args);
                if (!MainReturnsInt()) result = 0;
                Thread.RemoveThread(Thread.CurrentThread.threadIndex);

                Thread.JoinAll();

#if !MINRUNTIME
                ConsoleOutput.Finalize();
                ConsoleInput.Finalize();
#endif
                Tracing.Log(Tracing.Audit, "Main thread exited [{0}]",
                            (UIntPtr)unchecked((uint)result));
            }
            catch (Exception e) {
                Tracing.Log(Tracing.Fatal, "Failed with exception {0}.{1}",
                            e.GetType().Namespace, e.GetType().Name);
                Tracing.Log(Tracing.Trace, "Exception message was {0}",
                            e.ToString());
                DebugStub.WriteLine("Caught {0}", __arglist(e.Message));
                result = -1;
            }

            Tracing.Log(Tracing.Audit, "Runtime shutdown started.");
            VTable.Shutdown(result);
            Tracing.Log(Tracing.Audit, "Runtime exiting [{0}]",
                        (UIntPtr)unchecked((uint)result));
            return result;
        }

        [AccessedByRuntime("output to header : defined in hal.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(256)]
        private static extern int CallMain(String[] args);

        [AccessedByRuntime("output to header: defined in hal.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(256)]
        private static extern bool MainReturnsInt();

        [AccessedByRuntime("output to header: defined in hal.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(256)]
        private static extern void SetDebuggerPresence(bool debuggerPresent);

        internal static void Shutdown(int exitCode)
        {
            //
            // Gracefully close down the process.
            //
            Tracing.Log(Tracing.Audit, "Runtime.Shutdown({0})",
                        (UIntPtr)unchecked((uint)exitCode));

            DebugStub.WriteLine("Runtime.Shutdown({0})", __arglist(exitCode));

            VTable.Shutdown(exitCode);
            Tracing.Log(Tracing.Audit, "Runtime.Shutdown({0}) terminating",
                        (UIntPtr)unchecked((uint)exitCode));
            ProcessService.Stop(exitCode);
        }

        internal static void Stop(int exitCode)
        {
            //
            // Halt the process immediately.
            //
            Tracing.Log(Tracing.Audit, "Runtime.Stop({0})",
                        (UIntPtr)unchecked((uint)exitCode));

            DebugStub.WriteLine("Runtime.Stop({0})", __arglist(exitCode));

            ProcessService.Stop(exitCode);
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

        public static void InitType(Type ty)
        {
            VTable.initType((RuntimeType) ty);
        }

        public static void DumpPageTable()
        {
            System.GCs.PageTable.Dump("PageTable");
        }

        //////////////////////////////////////////////////////////////////////
        //
        public static bool EnableGCVerify
        {
            get {
                return VTable.enableGCVerify;
            }
            set {
                VTable.enableGCVerify = value;
            }
        }

        public static bool EnableGCAccounting
        {
            get {
                return VTable.enableGCAccounting;
            }
            set {
                VTable.enableGCAccounting = value;
                if (value == true) {
                    System.GCs.MemoryAccounting.Initialize(GC.gcType);
                }
            }
        }

        public static uint GCPerfCounter
        {
            get {
                return System.GC.perfCounter;
            }
            set {
                System.GC.perfCounter = value;
            }
        }
    }
}
