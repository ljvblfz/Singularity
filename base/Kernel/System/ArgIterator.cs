// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
namespace System
{

    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

    using Microsoft.Bartok.Runtime;

    [NoCCtor]
    public unsafe struct ArgIterator
    {
        [AccessedByRuntime("Referenced from C++")]
        private IntPtr * nextArg;
        [AccessedByRuntime("Referenced from C++")]
        private int remainingArgs;

        [NoHeapAllocation]
        public ArgIterator(RuntimeArgumentHandle arglist)
        {
            int * p = (int *) arglist.Pointer;
            remainingArgs = *p;
            nextArg = (IntPtr*)(p+1);
        }

        [NoHeapAllocation]
        public int GetRemainingCount() {
            return remainingArgs;
        }

        internal int Length {
            [NoHeapAllocation]
            get { return remainingArgs; }
        }

        [NoHeapAllocation]
        internal RuntimeType GetArg(int arg, out IntPtr value)
        {
            if (arg < 0 || arg >= remainingArgs) {
                value = IntPtr.Zero;
                return null;
            }

            value = nextArg[arg * 2];
            return Magic.toRuntimeType(Magic.fromAddress((UIntPtr)nextArg[arg * 2 + 1]));
        }

        [NoHeapAllocation]
        internal RuntimeType PopNextArg(out IntPtr value)
        {
            if (remainingArgs == 0) {
                value = IntPtr.Zero;
                return null;
            }
            else {
                RuntimeType type;

                value = *nextArg++;
                type = Magic.toRuntimeType(Magic.fromAddress((UIntPtr)(*nextArg++)));
                remainingArgs--;

                return type;
            }
        }

        [CLSCompliant(false)]
        public TypedReference GetNextArg()
        {
            if (remainingArgs == 0) {
                throw new InvalidOperationException
                    ("GetNextArg: No more arguments");
            }

            IntPtr value = *nextArg++;
            RuntimeType type = Magic.toRuntimeType(
                Magic.fromAddress((UIntPtr)(*nextArg++)));
            remainingArgs--;

            return new TypedReference(value, type);
        }

        [CLSCompliant(false)]
        public RuntimeTypeHandle GetNextArgType()
        {
            if (remainingArgs == 0) {
                throw new InvalidOperationException
                    ("GetNextArgType: No more arguments");
            }
            return new RuntimeTypeHandle(Magic.toRuntimeType(
                                             Magic.fromAddress(
                                                 (UIntPtr)(nextArg[1]))));
        }
    }
}
