////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity - Singularity ABI
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ProcessService.cs
//
//  Note:
//

using System;
using System.Runtime.CompilerServices;
using System.Reflection;
using Microsoft.Singularity.V1.Security;

[assembly: AssemblyTitle("Microsoft.Singularity.V1 ABI")]
[assembly: AssemblyProduct("Microsoft Research Singularity Operating System")]
[assembly: AssemblyCompany("Microsoft Corporation")]
[assembly: AssemblyVersion("1.0.0.1")]

namespace Microsoft.Singularity.V1.Services
{
    public struct LogEntry
    {
    }

    public enum  ParameterCode {
        Success,
        OutOfRange,
        NotSet,
        Retrieved,
        Undefined,
    }

    public struct ProcessService
    {
        //private readonly int id;

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1152)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Stop(int exitCode);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(960)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern TimeSpan GetUpTime();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(960)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern DateTime GetUtcTime();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetCycleCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetContextSwitchCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void SetGcPerformanceCounters(TimeSpan time, long bytes);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetKernelGcCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetKernelBootCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetKernelInterruptCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetCyclesPerSecond();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern ushort GetCurrentProcessId();

        /*
          [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern PrincipalHandle GetCurrentPrincipal();
        */

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetStartupArgCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(896)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe int GetStartupArg(int arg, char * output, int maxout);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetStartupEndpointCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(896)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        // Return parameter is really: ExtensionContract.Exp opt(ExHeap) *
        public static extern unsafe SharedHeapService.Allocation * GetStartupEndpoint(int arg);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(896)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        // Parameter is really: ExtensionContract.Exp opt(ExHeap) *
        public static extern unsafe void SetStartupEndpoint(int arg, SharedHeapService.Allocation * ep);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1472)]
        [MethodImpl(MethodImplOptions.InternalCall)]
            // Return parameter is really: DirectoryService.Imp opt(ExHeap) *
        public static extern unsafe SharedHeapService.Allocation * GetNamespaceEndpoint();


        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern TimeSpan GetThreadTime();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern long GetThreadsCreatedCount();


        [NoHeapAllocation]
        [AccessedByRuntime("referenced from Tracing.cpp")]
        public static unsafe void GetTracingHeaders(out LogEntry *logBegin,
                                                    out LogEntry *logLimit,
                                                    out LogEntry **logHead,
                                                    out byte *txtBegin,
                                                    out byte *txtLimit,
                                                    out byte **txtHead)
        {
            fixed (LogEntry **logBeginPtr = &logBegin,
                              logLimitPtr = &logLimit) {
                fixed (LogEntry ***logHeadPtr = &logHead) {
                    fixed (byte **txtBeginPtr = &txtBegin,
                                  txtLimitPtr = &txtLimit) {
                        fixed (byte ***txtHeadPtr = &txtHead) {
                            GetTracingHeadersImpl(logBeginPtr, logLimitPtr,
                                logHeadPtr, txtBeginPtr, txtLimitPtr,
                                txtHeadPtr);
                        }
                    }
                }
            }
        }

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1186)]
        [AccessedByRuntime("referenced from c++")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe void GetTracingHeadersImpl(
            LogEntry **logBegin,
            LogEntry **logLimit,
            LogEntry ***logHead,
            byte **txtBegin,
            byte **txtLimit,
            byte ***txtHead);

        [NoHeapAllocation]
        [AccessedByRuntime("referenced from Monitoring.cpp")]
        public static unsafe void GetMonitoringHeaders(out byte * buffer)
        {
            unsafe {
                fixed (byte * * bufferPtr = &buffer) {
                    GetMonitoringHeadersImpl(bufferPtr);
                }
            }
        }

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1166)]
        [AccessedByRuntime("referenced from c++")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe void GetMonitoringHeadersImpl(
            byte * * buffer);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Waypoint0();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void Waypoint(int num);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void WaypointDone();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void WaypointDump();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetStartupStringArgCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetStartupStringArrayArgCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetStartupLongArgCount();

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetStartupBoolArgCount();

        [NoHeapAllocation]
        public static unsafe ParameterCode GetStartupStringArg(
            int arg,
            char * output,
            ref int inOutLength)
        {
            fixed (int * inOutLengthPtr = &inOutLength) {
                return GetStartupStringArgImpl(arg, output, inOutLengthPtr);
            }
        }

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1178)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe ParameterCode GetStartupStringArgImpl(
            int arg,
            char * output,
            int * inOutLength);

        [NoHeapAllocation]
        public static ParameterCode GetStartupLongArg(
            int index,
            out long value)
        {
            unsafe {
                fixed (long * valuePtr = &value) {
                    return GetStartupLongArgImpl(index, valuePtr);
                }
            }
        }

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1174)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe ParameterCode GetStartupLongArgImpl(
            int index,
            long * value);

        [NoHeapAllocation]
        public static ParameterCode GetStartupBoolArg(
            int index,
            out bool value)
        {
            unsafe {
                fixed (bool * valuePtr = &value) {
                    return GetStartupBoolArgImpl(index, valuePtr);
                }
            }
        }

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1174)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe ParameterCode GetStartupBoolArgImpl(
            int index,
            bool * value);

        [NoHeapAllocation]
        public static unsafe ParameterCode GetStartupStringArrayArg(
                                                int index,
                                                char *args,
                                                int *argLengths,
                                                out int arrayLength,
                                                out int totalCharCount
                                         )
        {
            fixed (int * arrayLengthPtr = &arrayLength,
                         totalCharCountPtr = &totalCharCount) {
                return GetStartupStringArrayArgImpl(index, args, argLengths,
                    arrayLengthPtr, totalCharCountPtr);
            }
        }

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(1186)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe ParameterCode GetStartupStringArrayArgImpl(
                                                int index,
                                                char *args,
                                                int *argLengths,
                                                int *arrayLength,
                                                int *totalCharCount
                                         );


        // haryadi -- ping pong interface to app
        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int RunPingPongInt(int start);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int HelloProcessABI(int num, int num2);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe ulong TestAbiCallOne(ulong a);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe int TestAbiCallTwo(uint a, char *b);

        [OutsideGCDomain]
        [NoHeapAllocation]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern unsafe char* TestAbiCallThree(int a, int *b, byte c);


        public static unsafe void TestAbiCallAll()
        {
            ulong a1= 33;
            ulong r1 = ProcessService.TestAbiCallOne(a1);

            char x1 = 'a';
            char *b2 =  &x1;
            uint a2 = 10;
            int r2 = ProcessService.TestAbiCallTwo(a2, b2);

            int a3 = 44;
            int *b3 = & a3;
            byte c3 = 1;
            char *r3 = ProcessService.TestAbiCallThree(a3, b3, c3);
        }

    }
}
