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
using System.Threading;
using Microsoft.Singularity;
using Microsoft.Singularity.Memory;
using Microsoft.Singularity.Security;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.V1.Security;

namespace Microsoft.Singularity.V1.Services
{
    [CLSCompliant(false)]
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

    [CLSCompliant(false)]
    public struct ProcessService
    {
        // In order to pass the arguments to the kernel, we first need to
        // flatten the arguments parameter from an array of strings to
        // an array of characters (containing the contents of the strings).

        // If argVector is null we compute the the length needed to store
        // the result. It is up to the calling process to then allocate
        // the memory and call again.
        private static unsafe int FlattenStringArray(string[] arguments,
                                                      int* argLengths,
                                                      char* argVector
                                                      )
        {
            int totalCharacters = 0;

            int len = arguments.Length;
            for (int arg = 0; arg < len; arg++) {
                string argstring = arguments[arg];
                if (argstring == null) continue;
                totalCharacters += argstring.Length;
            }

            if (argVector == null ) {
                return totalCharacters;
            }

            int offset = 0;
            for (int arg = 0; arg < len; arg++) {
                string argstring = arguments[arg];
                if (argstring == null) {
                    argLengths[arg] = 0;
                    continue;
                }
                int alen = argstring.Length;
                //argstring.CopyTo(0, argVector, offset, alen);
                for (int i=0; i < alen; i++){
                    argVector[offset+i] = argstring[i];
                }
                offset += alen;
                argLengths[arg] = alen;
            }
           return totalCharacters;
        }

        [ExternalEntryPoint]
        public static void Stop(int exitCode)
        {
            Tracing.Log(Tracing.Debug, "ProcessService.Stop(exit={0})",
                        (UIntPtr)unchecked((uint)exitCode));

            Thread.CurrentProcess.Stop(exitCode);
        }

        [ExternalEntryPoint]
        public static TimeSpan GetUpTime()
        {
            TimeSpan ts = SystemClock.KernelUpTime;
            return ts;
        }

        [ExternalEntryPoint]
        public static DateTime GetUtcTime()
        {
            DateTime dt = SystemClock.GetUtcTime();
            return dt;
        }

        [ExternalEntryPoint]
        public static long GetCycleCount()
        {
            return (long)Processor.CycleCount;
        }

        [ExternalEntryPoint]
        public static long GetCyclesPerSecond()
        {
            Kernel.Waypoint(300);
            return (long)Processor.CyclesPerSecond;
        }

        [ExternalEntryPoint]
        public static long GetContextSwitchCount()
        {
            return Processor.CurrentProcessor.NumContextSwitches;
        }

        [ExternalEntryPoint]
        public static void SetGcPerformanceCounters(TimeSpan spent, long bytes)
        {
            Thread.CurrentProcess.SetGcPerformanceCounters(spent, bytes);
        }

        [ExternalEntryPoint]
        public static long GetKernelGcCount()
        {
            int collectorCount;
            long collectorMillis;
            long collectorBytes;

            GC.PerformanceCounters(out collectorCount,
                                   out collectorMillis,
                                   out collectorBytes);

            return collectorCount;
        }

        [ExternalEntryPoint]
        public static long GetKernelBootCount()
        {
            return Resources.GetWarmBootCount();
        }

        [ExternalEntryPoint]
        public static long GetKernelInterruptCount()
        {
            return Processor.CurrentProcessor.NumInterrupts;
        }

        [ExternalEntryPoint]
        public static ushort GetCurrentProcessId()
        {
            return (ushort)Thread.CurrentProcess.ProcessId;
        }

        /*
          [ExternalEntryPoint]
        public static PrincipalHandle GetCurrentPrincipal()
        {
            return new PrincipalHandle(Thread.CurrentProcess.PrincipalId.val);
        }
        */

        [ExternalEntryPoint]
        // Return parameter is really: DirectoryService.Imp opt(ExHeap) *
        public static unsafe SharedHeapService.Allocation * GetNamespaceEndpoint()
        {
            return (SharedHeapService.Allocation *)
                Thread.CurrentProcess.GetNamespaceEndpoint();
        }

        [ExternalEntryPoint]
        public static int GetStartupEndpointCount() {
            return Thread.CurrentProcess.GetStartupEndpointCount();
        }

        [ExternalEntryPoint]
        // Return parameter is really: ExtensionContract.Exp opt(ExHeap) *
        public static unsafe SharedHeapService.Allocation * GetStartupEndpoint(int arg)
        {
            return (SharedHeapService.Allocation *)
                Thread.CurrentProcess.GetStartupEndpoint(arg);
        }

        [ExternalEntryPoint]
        // Parameter is really: ExtensionContract.Exp opt(ExHeap) *
        public static unsafe void SetStartupEndpoint(int arg,
                                                     SharedHeapService.Allocation * ep)
        {
            SharedHeap.Allocation * mep = (SharedHeap.Allocation *)ep;
            Thread.CurrentProcess.SetEndpoint(arg, ref mep);
        }

        [ExternalEntryPoint]
        public static int GetStartupArgCount()
        {
            return Thread.CurrentProcess.GetStartupArgCount();
        }

        [ExternalEntryPoint]
        public static unsafe int GetStartupArg(int arg, char * output, int maxput)
        {
            string s = Thread.CurrentProcess.GetStartupArg(arg);
            Tracing.Log(Tracing.Debug,
                        "Process.GetStartupArg(arg={0}, out={1:x8}, max={2}) = {3}",
                        (UIntPtr)unchecked((uint)arg),
                        (UIntPtr)output,
                        (UIntPtr)unchecked((uint)maxput),
                        (UIntPtr)unchecked((uint)(s != null ? s.Length : 0)));

            if (s == null) {
                return 0;
            }
            if (output == null) {
                return s.Length + 1;
            }
            return s.InternalGetChars(output, maxput);
        }

        [ExternalEntryPoint]
        public static TimeSpan GetThreadTime() {
            return Thread.CurrentThread.ExecutionTime;
        }

        [ExternalEntryPoint]
        public static long GetThreadsCreatedCount() {
            return PerfCounters.ThreadsCreated;
        }

        [ExternalEntryPoint]
        public static unsafe void GetTracingHeadersImpl(
            LogEntry **logBegin,
            LogEntry **logLimit,
            LogEntry ***logHead,
            byte **txtBegin,
            byte **txtLimit,
            byte ***txtHead)
        {
            GetTracingHeaders(out *logBegin, out *logLimit, out *logHead,
                out *txtBegin, out *txtLimit, out *txtHead);
        }

        [AccessedByRuntime("referenced from Tracing.cpp")]
        public static unsafe void GetTracingHeaders(out LogEntry *logBegin,
                                                    out LogEntry *logLimit,
                                                    out LogEntry **logHead,
                                                    out byte *txtBegin,
                                                    out byte *txtLimit,
                                                    out byte **txtHead)
        {
            Tracing.LogEntry *_logBegin;
            Tracing.LogEntry *_logLimit;
            Tracing.LogEntry **_logHead;

            Tracing.GetTracingHeaders(out _logBegin, out _logLimit, out _logHead,
                                      out txtBegin, out txtLimit, out txtHead);

            logBegin = (LogEntry *)_logBegin;
            logLimit = (LogEntry *)_logLimit;
            logHead = (LogEntry **)_logHead;
        }

        [ExternalEntryPoint]
        public static unsafe void GetMonitoringHeadersImpl(byte * * _buffer)
        {
            GetMonitoringHeaders(out *_buffer);
        }

        [AccessedByRuntime("referenced from Monitoring.cpp")]
        public static unsafe void GetMonitoringHeaders(out byte * _buffer)
        {
            Monitoring.GetMonitoringHeaders(out _buffer);
        }


        [ExternalEntryPoint]
        public static void Waypoint0()
        {
            Kernel.Waypoint0();
        }

        [ExternalEntryPoint]
        public static void Waypoint(int num)
        {
            Kernel.Waypoint(num);
        }

        [ExternalEntryPoint]
        public static void WaypointDone()
        {
            Kernel.WaypointDone();
        }

        [ExternalEntryPoint]
        public static void WaypointDump()
        {
            Kernel.WaypointDump();
        }

        [ExternalEntryPoint]
        public static int GetStartupBoolArgCount() {
            return Thread.CurrentProcess.GetStartupBoolArgCount();
        }

        [ExternalEntryPoint]
        public static int GetStartupLongArgCount() {
            return Thread.CurrentProcess.GetStartupLongArgCount();
        }

        [ExternalEntryPoint]
        public static int GetStartupStringArgCount() {
            return Thread.CurrentProcess.GetStartupStringArgCount();
        }

        [ExternalEntryPoint]
        public static int GetStartupStringArrayArgCount() {
            return Thread.CurrentProcess.GetStartupStringArrayArgCount();
        }

        [ExternalEntryPoint]
        public static unsafe ParameterCode GetStartupLongArgImpl(
            int index,
            long * value)
        {
            return GetStartupLongArg(index, out *value);
        }

        public static ParameterCode GetStartupLongArg(int index, out long value) {
            return (ParameterCode) Thread.CurrentProcess.GetStartupLongArg(index, out value);
        }

        [ExternalEntryPoint]
        public static unsafe ParameterCode GetStartupBoolArgImpl(
            int index,
            bool * value)
        {
            return GetStartupBoolArg(index, out *value);
        }

        public static ParameterCode GetStartupBoolArg(int index, out bool value) {
            return (ParameterCode)Thread.CurrentProcess.GetStartupBoolArg(index, out value);
        }

        [ExternalEntryPoint]
        public static unsafe ParameterCode GetStartupStringArrayArgImpl(
                                            int index,
                                            char *strbuff,
                                            int  *len,
                                            int *arrayLength,
                                            int *totalCharCount)
        {
            ParameterCode code;
            string[] strings;
            code = (ParameterCode) Thread.CurrentProcess.GetStartupStringArrayArg(index, out strings);
            if (code != ParameterCode.Success) {
                *arrayLength = 0;
                *totalCharCount = 0;
                return code;
            }
            if (strings == null) {
                *arrayLength = 0;
                *totalCharCount = 0;
                return ParameterCode.NotSet;
            }
            *totalCharCount = FlattenStringArray(strings, len, strbuff);
            *arrayLength = strings.Length;
            return ParameterCode.Success;
        }

        [ExternalEntryPoint]
        public static unsafe ParameterCode GetStartupStringArgImpl(
            int arg,
            char * output,
            int * maxput)
        {
            return GetStartupStringArg(arg, output, ref *maxput);
        }

        public static unsafe ParameterCode GetStartupStringArg(int arg, char * output, ref int maxput)
        {
            string s;
            ParameterCode code = (ParameterCode)Thread.CurrentProcess.GetStartupStringArg(arg, out s);
            Tracing.Log(Tracing.Debug,
                "Process.GetStartupStringArg(arg={0}, out={1:x8}, max={2}) = {3}",
                (UIntPtr)unchecked((uint)arg),
                (UIntPtr)output,
                (UIntPtr)unchecked((uint)maxput),
                (UIntPtr)unchecked((uint)(s != null ? s.Length : 0)));

            if (code != ParameterCode.Success) {
                return code;
            }
            if (output == null) {
                if (s == null) maxput = 0;
                else maxput = s.Length ;
                return ParameterCode.Success;
            }
            if (s != null) s.InternalGetChars(output, maxput);
            return ParameterCode.Success;
        }


        // haryadi -- interface to run ping pong from app
        [ExternalEntryPoint]
        public static int RunPingPongInt(int start)
        {
            return Processor.RunPingPongInt(start);
        }

        // haryadi
        // Note: [NoHeapAllocation] is bad .. Since HelloProcessABI
        // is called from Processor.DispatchSpecificInterrupt(), then
        // every ABI should be annotated with [NoHeapAllocation]
        // Probably, in the future when we already use the scheduler,
        // instead of direct invocation during interrupt context, we
        // don't need [NoHeapAllocation]
        [ExternalEntryPoint]
        [ NoHeapAllocation ]
        public static int HelloProcessABI(int num, int num2)
        {
            DebugStub.WriteLine
                ("HSG: ** cpu.{0} HelloProcessABI({1:x8},{2:x8})",
                 __arglist(Processor.GetCurrentProcessorId(),
                           num, num2));
            // return the power of 2 of the value
            return num+num2;
        }



        [ExternalEntryPoint]
        [ NoHeapAllocation ]
        public static unsafe ulong TestAbiCallOne(ulong a) {
            DebugStub.WriteLine("HSG: ** TestAbiCallOne({0:x16})}",
                                __arglist(a));
            return 18000000000000000000 + a; // 64 bit
        }


        [ExternalEntryPoint]
        [ NoHeapAllocation ]
        public static unsafe int TestAbiCallTwo(uint a, char *b)
        {
            DebugStub.WriteLine("HSG: ** TestAbiCallTwo({0:x8}, {1:x8})}",
                                __arglist((int)(a), (int)(b)));
            return ((int)(b) + (int)a);
        }

        [ExternalEntryPoint]
        [ NoHeapAllocation ]
        public static unsafe char* TestAbiCallThree(int a, int *b, byte c)
        {
            DebugStub.WriteLine
                ("HSG: ** TestAbiCallOne({0:x8}, {1:x8}, {2:x8})}",
                 __arglist((int)(a), (int)(b), (int)(c)));

            return (char*)((int)b + a + (int)c);
        }



#if FALSE
        [ExternalEntryPoint]
        public static int StubHelloProcessABI2(int num)
        {

            int ap  = Processor.GetCurrentProcessorId();
            int bsp = 0;

            DebugStub.WriteLine
                ("\n\nHSG: ** cpu.{0} ProcessService.StubHelloProcessABI({1})",
                 __arglist(ap, num));
            DebugStub.WriteLine("HSG: ** -----------------------------------");

            bool iflag = Processor.DisableInterrupts();

            MpExecution.AbiCall abiCall = new MpExecution.AbiCall();

            // 1) set up all the parameters
            abiCall.argVal = num;

            // prepare IPI
            DebugStub.Print
                ("HSG: ** cpu.{0} PutAbiCall(cpu.{1}, arg.{2}) --> ",
                 __arglist(ap, bsp, abiCall.argVal));

            // 2) register the abiCall,
            //    after this, we get the position
            int pos = MpExecution.PutAbiCall(bsp, abiCall);

            DebugStub.WriteLine("pos.{0}", __arglist(pos));

            DebugStub.WriteLine
                ("HSG: ** cpu.{0} SendAbiCall(cpu.{1}, cpu.{2})",
                 __arglist(ap, ap, bsp));

            // 3) send
            MpExecution.SendAbiCall(ap, bsp);

            DebugStub.WriteLine
                ("HSG: ** cpu.{0} WaitAbiCall(cpu.{1}, pos.{2}) ... zzz ... zzz ... ",
                 __arglist(ap, bsp, pos));

            // 4) spin until done
            MpExecution.WaitAbiCall(bsp, pos, out abiCall);

            // 5) we have the return value
            int retval = abiCall.retVal;

            DebugStub.WriteLine
                ("HSG: ** cpu.{0} is waken up and receives retval.{1}",
                 __arglist(ap, retval));

            DebugStub.WriteLine
                ("HSG: ** cpu.{0} ReleaseAbiCall(cpu.{1}, pos.{2})",
                 __arglist(ap, bsp, pos));

            // 6) release abiCall
            MpExecution.ReleaseAbiCall(bsp, pos);

            Processor.RestoreInterrupts(iflag);

            return retval;
            }
#endif
    }
}
