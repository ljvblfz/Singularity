////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Debug.cs
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

using Microsoft.Singularity.V1.Services;

namespace Microsoft.Singularity
{
    [NoCCtor]
    [CLSCompliant(false)]
    public class DebugStub
    {
        /////////////////////////////////////////////////////// Print Methods.
        //
        [NoHeapAllocation]
        public static void Print(byte value)
        {
            Print("{0:x2}", __arglist(value));
        }

        [NoHeapAllocation]
        public static void Print(ushort value)
        {
            Print("{0:x4}", __arglist(value));
        }

        [NoHeapAllocation]
        public static void Print(uint value)
        {
            Print("{0:x8}", __arglist(value));
        }

        [NoHeapAllocation]
        public static void Print(ulong value)
        {
            Print("{0:x}", __arglist(value));
        }

        [NoHeapAllocation]
        public static void Print(UIntPtr value)
        {
            Print("{0:x8}", __arglist(value));
        }

        [NoHeapAllocation]
        public static void Print(sbyte value)
        {
            Print("{0}", __arglist(value));
        }

        [NoHeapAllocation]
        public static void Print(short value)
        {
            Print("{0}", __arglist(value));
        }

        [NoHeapAllocation]
        public static void Print(int value)
        {
            Print("{0}", __arglist(value));
        }

        [NoHeapAllocation]
        public static void Print(long value)
        {
            Print("{0}", __arglist(value));
        }

        //////////////////////////////////////////////////////////////////////
        //

        [NoHeapAllocation]
        public static void Print(String value)
        {
            if (value != null) {
                Print(value, new ArgIterator());
            }
        }

        [NoHeapAllocation]
        public static void Print(String format, __arglist)
        {
            Print(format, new ArgIterator(__arglist));
        }

#if PAGING
[AccessedByRuntime("output to header : defined in debugstub.cpp")]
[MethodImpl(MethodImplOptions.InternalCall)]
[StackBound(64)]
[NoHeapAllocation]
public static extern void Foo();
//static int ccc = 0;
#endif
        [NoHeapAllocation]
        public static unsafe void Print(String format, ArgIterator args)
        {
#if PAGING
Foo();
/*
if (ccc == 0) {
    DebugStub.Break();
    ccc = 1;
}
*/
#endif
            char *buffer;
            int length;
            int used = 0;

            DebugService.PrintBegin(out buffer, out length);
            try {
                if (buffer != null) {
                    used = String.LimitedFormatTo(format, args, buffer, length);
                }
            }
            finally {
                DebugService.PrintComplete(buffer, used);
            }
        }

        //////////////////////////////////////////////////////////////////////
        //

        [NoHeapAllocation]
        public static void Write(String value)
        {
            if (value != null) {
                Write(value, new ArgIterator());
            }
        }

        [NoHeapAllocation]
        public static void Write(String format, __arglist)
        {
            Write(format, new ArgIterator(__arglist));
        }

        [NoHeapAllocation]
        public static unsafe void Write(String format, ArgIterator args)
        {
            char *buffer;
            int length;
            int used = 0;

            DebugService.PrintBegin(out buffer, out length);
            try {
                if (buffer != null) {
                    used = String.LimitedFormatTo(format, args, buffer, length);
                }
            }
            finally {
                DebugService.PrintComplete(buffer, used);
            }
        }

        //////////////////////////////////////////////////////////////////////
        //
        [NoHeapAllocation]
        public static void WriteLine()
        {
            WriteLine("", new ArgIterator());
        }

        [NoHeapAllocation]
        public static void WriteLine(String value)
        {
            if (value != null) {
                WriteLine(value, new ArgIterator());
            }
        }

        [NoHeapAllocation]
        public static void WriteLine(String format, __arglist)
        {
            WriteLine(format, new ArgIterator(__arglist));
        }

        [NoHeapAllocation]
        public static unsafe void WriteLine(String format, ArgIterator args)
        {
            char *buffer;
            int length;
            int used = 0;

            DebugService.PrintBegin(out buffer, out length);
            try {
                if (buffer != null) {
                    used = String.LimitedFormatTo(format, args, buffer, length);
                    if (used < length) {
                        buffer[used++] = '\n';
                    }
                }
            }
            finally {
                DebugService.PrintComplete(buffer, used);
            }
        }

        ////////////////////////////////////////////////////// Assert Methods.
        //
        [NoHeapAllocation]
        public static void NotImplemented()
        {
            failAssert("Not implemented.");
        }

        [NoHeapAllocation]
        public static void NotImplemented(String msg)
        {
            failAssert(/*"Not implemented: "+*/msg);
        }

        [Conditional("DEBUG")]
        [NoInline]
        [NoHeapAllocation]
        public static void NotReached()
        {
            failAssert("Unreachable code reached.");
        }

        [Conditional("DEBUG")]
        [NoInline]
        [NoHeapAllocation]
        public static void NotReached(String msg)
        {
            failAssert(/*"Unreachable code reached: "+*/msg);
        }

        [Conditional("DEBUG")]
        [NoInline]
        [ManualRefCounts]
        [NoHeapAllocation]
        public static void Assert(bool expr)
        {
            if (!expr) {
                failAssert(null);
            }
        }

        [Conditional("DEBUG")]
        [NoInline]
        [ManualRefCounts]
        [NoHeapAllocation]
        public static void Deny(bool expr)
        {
            if (expr) {
                failAssert(null);
            }
        }

        [Conditional("DEBUG")]
        [NoInline]
        [ManualRefCounts]
        [NoHeapAllocation]
        public static void Assert(bool expr, String s)
        {
            if (!expr) {
                failAssert(s);
            }
        }

        [Conditional("DEBUG")]
        [NoInline]
        [NoHeapAllocation]
        public static void Deny(bool expr, String s)
        {
            if (expr) {
                failAssert(s);
            }
        }

        [ManualRefCounts]
        [NoHeapAllocation]
        private static void failAssert(String s)
        {
            if (s != null) {
                Print("Assertion failed: {0}", __arglist(s));
            }
            else {
                Print("Assertion failed.");
            }
            Break();
        }

        //////////////////////////////////////////////////////////////////////
        //
        [NoHeapAllocation]
        public static ulong ReadPerfCounter(uint which)
        {
            return DebugService.ReadPerfCounter(which);
        }


        [NoHeapAllocation]
        public static bool WritePerfCounter(uint which, ulong value)
        {
            return DebugService.WritePerfCounter(which, value);
        }

        [NoHeapAllocation]
        public static bool AddToPerfCounter(uint which, ulong value)
        {
            return DebugService.AddToPerfCounter(which, value);
        }

        /////////////////////////////////////////////////////// State Methods.
        //
#if true
        [AccessedByRuntime("output to header : defined in halkd.cpp")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [StackBound(638)]
        [NoHeapAllocation]
        public static extern void Break();
#else
        [NoHeapAllocation]
        public static void Break()
        {
            DebugService.Break();
        }
#endif
    }
}
