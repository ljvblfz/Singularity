////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Stacks.cs - Primitive stack segment manager
//
//  Note:
//

namespace Microsoft.Singularity.Memory {

    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using Microsoft.Singularity;
    using Microsoft.Singularity.X86;
    using Microsoft.Singularity.V1.Services;

    [NoCCtor]
    [CLSCompliant(false)]
    [RequiredByBartok]
    internal class Stacks {

        internal static unsafe void Initialize()
        {
            DebugStub.Print("Stacks.Initialize() called\n");
        }

        internal static unsafe void Finalize()
        {
            DebugStub.Print("Stacks.Finalize() called\n");
        }

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack0(); // Copy 0 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack4(); // Copy 4 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack8(); // Copy 8 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack12(); // Copy 12 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack16(); // Copy 16 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack20(); // Copy 20 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack24(); // Copy 24 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack28(); // Copy 28 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack32(); // Copy 32 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack36(); // Copy 36 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack40(); // Copy 40 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack44(); // Copy 44 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack48(); // Copy 48 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack52(); // Copy 52 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack56(); // Copy 56 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack60(); // Copy 60 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        [RequiredByBartok]
        internal static extern void LinkStack64(); // Copy 64 bytes of arguments on stack.

        [ExternalStaticData]
        internal static byte UnlinkStackBegin;

        [ExternalStaticData]
        internal static byte UnlinkStackLimit;
    }
}
