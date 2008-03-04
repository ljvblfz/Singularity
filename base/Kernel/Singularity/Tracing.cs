////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:  Tracing.cs
//
//  Note:  Provides a simple tracing facility to allow for post-facto performance debugging.
//

using System;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Singularity;

namespace Microsoft.Singularity
{
    /// <remarks>
    /// Provides a simple tracing facility.  Writes and displays log records of the form:
    ///     long    cycleCounter; // GetCycleCounter() value
    ///     ushort  recordType; // TracingType value
    ///     ushort  byteCount; // Bytes of data in record including these 12 bytes
    ///     [type-specific data]
    ///
    /// Records are aligned on 8-byte boundaries to make them easier to view in memory dumps.
    ///
    /// XXX Code needs to be implemented to allow buffers to be switched, particularly after calling DumpLogRecords()
    /// </remarks>
    [NoCCtor]
    [CLSCompliant(false)]
    public class Tracing
    {
        [AccessedByRuntime("referenced from Tracing.cpp")]
        public struct LogEntry
        {
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public ulong    cycleCount;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public UIntPtr  eip;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public ushort   cpuId;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public ushort   threadId;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public ushort   processId;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public ushort   tag;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public byte     severity;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public byte     strings;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public ushort   padding0;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public unsafe byte * text;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public UIntPtr  arg0;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public UIntPtr  arg1;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public UIntPtr  arg2;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public UIntPtr  arg3;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public UIntPtr  arg4;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public UIntPtr  arg5;
            [AccessedByRuntime("referenced from Tracing.cpp")]
            public uint     padding1;
        }

        public const byte Fatal   = 0xe; // system crashed.
        public const byte Error   = 0xc; // system will crash.
        public const byte Warning = 0xa; // cause for immediate concern.
        public const byte Notice  = 0x8; // might be cause for concern.
        public const byte Trace   = 0x6; // may be of use in crash.
        public const byte Audit   = 0x4; // impact on performance.
        public const byte Debug   = 0x2; // used only for debugging.

        [Flags]
        public enum Strings
        {
            [AccessedByRuntime("referenced from Tracing.cpp")]
            String0 =   0x01,
            [AccessedByRuntime("referenced from Tracing.cpp")]
            String1 =   0x02,
            String2 =   0x04,
            String3 =   0x08,
            String4 =   0x10,
            String5 =   0x20,
            String6 =   0x40,
            String7 =   0x80,
        }

        // Note: These fields are initialized by the code in Tracing.cpp.
        //
        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe byte *    txtBegin;
        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe byte *    txtLimit;
#if SINGULARITY_KERNEL
        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe byte *    txtHead;
#endif
        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe byte **   ptxtHead;

        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe LogEntry *logBegin;
        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe LogEntry *logLimit;
#if SINGULARITY_KERNEL
        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe LogEntry *logHead;
#endif
        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe LogEntry **plogHead;
#if SINGULARITY_KERNEL
        [AccessedByRuntime("referenced from Tracing.cpp")]
        private static unsafe long *tscOffsets;
#endif
        //
        // End Note.

        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(64)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static extern void Initialize();

        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(64)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static extern void Finalize();

        [NoHeapAllocation]
        public static unsafe void GetTracingHeaders(out LogEntry *_logBegin,
                                                    out LogEntry *_logLimit,
                                                    out LogEntry **_logHead,
                                                    out byte *_txtBegin,
                                                    out byte *_txtLimit,
                                                    out byte **_txtHead)
        {
            _logBegin = logBegin;
            _logLimit = logLimit;
            _logHead = plogHead;

            _txtBegin = txtBegin;
            _txtLimit = txtLimit;
            _txtHead = ptxtHead;
        }

        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(64)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        private static unsafe extern byte * AddText(byte *buffer, string arg);

        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        private static unsafe extern LogEntry * CreateLog(byte severity,
                                                          UIntPtr eip,
                                                          int chars,
                                                          out byte *text);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, sbyte * msg);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, sbyte * msg,
                                             String arg0,
                                             UIntPtr arg1);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(128)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, sbyte * msg,
                                             String arg0,
                                             UIntPtr arg1,
                                             UIntPtr arg2);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             UIntPtr arg0);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             UIntPtr arg0, UIntPtr arg1);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             UIntPtr arg0, UIntPtr arg1, UIntPtr arg2);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             UIntPtr arg0, UIntPtr arg1, UIntPtr arg2,
                                             UIntPtr arg3);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             UIntPtr arg0, UIntPtr arg1, UIntPtr arg2,
                                             UIntPtr arg3, UIntPtr arg4);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             UIntPtr arg0, UIntPtr arg1, UIntPtr arg2,
                                             UIntPtr arg3, UIntPtr arg4, UIntPtr arg5);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             string arg0);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             string arg0, UIntPtr arg1);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             string arg0, UIntPtr arg1, UIntPtr arg2);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             string arg0, UIntPtr arg1, UIntPtr arg2,
                                             UIntPtr arg3);

        [Conditional("TRACING")]
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(256)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static unsafe extern void Log(byte severity, string msg,
                                             string arg0, string arg1);

#if SINGULARITY_KERNEL
        [AccessedByRuntime("output to header : defined in Tracing.cpp")]
        [StackBound(64)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [NoHeapAllocation]
        public static extern void SetTscOffset(long tscOffset);
#endif // SINGULARITY_KERNEL
    }
}
