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

//#define TEST_STACK_LINKING
//#define DEBUG_STACK_VERBOSE
#define NO_TRACE_STACKS

namespace Microsoft.Singularity.Memory
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.GCs;
    using Microsoft.Singularity;
    using Microsoft.Singularity.X86;

    [NoCCtor]
    [CLSCompliant(false)]
    [RequiredByBartok]
    internal class Stacks
    {
        // This constant gives a reasonable size for an initial stack
        // chunk, leaving room for the metadata that will be added to
        // the top of the stack (sizeof(StackHead)).
        internal const int InitialStackSize = 0x1f00;

        [AccessedByRuntime("referenced from halstack.asm")]
        internal static int GetCount;
        internal static int ReturnCount;

        [StructLayout(LayoutKind.Sequential)]
        internal struct StackHead
        {
            internal UIntPtr    prevBegin;
            internal UIntPtr    prevLimit;
            internal UIntPtr    esp;
        };

        internal static unsafe void Initialize()
        {
            Tracing.Log(Tracing.Debug, "Stacks.Initialize() called");

            GetCount = 0;
            ReturnCount = 0;
        }

        internal static unsafe void Finalize()
        {
            unchecked {
                Tracing.Log(Tracing.Debug, "Stacks.Finalize()  GetStackSegment called {0} times.",
                            (UIntPtr)(uint)GetCount);
            }
            PrintCounts();
        }

        internal static void PrintCounts()
        {
            unchecked {
                Tracing.Log(Tracing.Debug, "Segments: {1} links, {2} outstanding",
                            (UIntPtr)(uint)GetCount,
                            (UIntPtr)(uint)(GetCount - ReturnCount));
            }
        }

        [AccessedByRuntime("referenced from halstack.asm")]
        [NoStackLinkCheck]
        internal static unsafe UIntPtr GetStackSegment(UIntPtr size,
                                                       ref ThreadContext context)
        {
            return GetStackSegment(size, ref context, false);
        }

        [NoStackLinkCheck]
        internal static unsafe UIntPtr GetInitialStackSegment(ref ThreadContext context)
        {
            // The first stack segment is always in kernel memory
            return GetStackSegment(0, ref context, true);
        }

        [NoStackLinkCheck]
        internal static unsafe UIntPtr GetStackSegment(UIntPtr size,
                                                       ref ThreadContext context,
                                                       bool forceToKernel)
        {
            UIntPtr begin = context.stackBegin;
            UIntPtr limit = context.stackLimit;
#if NO_TRACE_STACKS
#else
            Kernel.Waypoint(666);
#endif
            StackHead *head = GetStackSegmentRaw(size, ref context, forceToKernel);
            if (head != null) {
                head->prevBegin = begin;
                head->prevLimit = limit;
                head->esp = 0;
            }
            return (UIntPtr)head;
        }

        [NoStackLinkCheck]
        internal static unsafe StackHead * GetStackSegmentRaw(UIntPtr size,
                                                              ref ThreadContext context,
                                                              bool forceToKernel)
        {
            // Allocate a new chunk, making room for StackHead at the top.
            // If you change these constants to add more data, see the
            // comment about InitialStackSize at the top of this file!
#if NO_TRACE_STACKS
#else
            Kernel.Waypoint(667);
#endif
            if (size == UIntPtr.Zero) {
                size = InitialStackSize;
            }
            size = MemoryManager.PagePad(size + sizeof(StackHead));

            UIntPtr chunk;


            Process owner = Process.GetProcessByID(context.processId);
            /*
              // NOTE here's where we should be clever about
              // whether to allocate a stack chunk in the user range
              // or the kernel range. Except, if we switch contexts
              // during an ABI call while using a user-range stack
              // segment on a paging machine, we die. Gloss over
              // this hackily by always getting stack segments
              // from the kernel range.
            if (forceToKernel || (owner == Process.kernelProcess)) {
                chunk = MemoryManager.KernelAllocate(
                    MemoryManager.PagesFromBytes(size), owner, 0, PageType.Stack);
            } else {
                chunk = MemoryManager.UserAllocate(
                    MemoryManager.PagesFromBytes(size), owner, 0, PageType.Stack);
            }
            */

            chunk = MemoryManager.KernelAllocate(
                MemoryManager.PagesFromBytes(size), owner, 0, PageType.Stack);

            if (chunk != UIntPtr.Zero) {
                // NB: We do _not_ zero out stack memory!
                // We assume that Bartok prevents access to prev contents.
                StackHead *head = (StackHead *)(chunk + size - sizeof(StackHead));

                context.stackBegin = chunk + size;
                context.stackLimit = chunk;

#if DEBUG_STACK_VERBOSE
                Tracing.Log(Tracing.Debug, "GetStackSegmentRaw(size={0:d}) -> [{1,8:x}..{2,8:x}",
                            size, context.stackLimit, context.stackBegin);
#endif
                GetCount++;

                return head;
            }
            else {
                // Stack allocation failed.  In the future, we should
                // trigger a kernel exception; for now, we break to the
                // debugger.
                DebugStub.Break();
                return null;
            }
        }

        [AccessedByRuntime("reference from halstack.asm")]
        [NoStackLinkCheck]
        // NB: This function must execute in low-stack conditions!
        // See the comment at the top of this file.
        // Returns the address of the stack with argsN pushed.
        internal static unsafe UIntPtr GetStackSegmentAndCopy(UIntPtr size,
                                                              ref ThreadContext context,
                                                              uint *arg2,
                                                              uint args,
                                                              UIntPtr esp,
                                                              UIntPtr begin,
                                                              UIntPtr limit)
        {
#if NO_TRACE_STACKS
#else
            Kernel.Waypoint(668);
#endif
#if DEBUG_STACK_VERBOSE
            fixed (ThreadContext *ptr = &context) {
                DebugStub.Print("GetStackSegmentAndCopy(" +
                                "size={0},cxt={1:x8},arg={2:x8},num={3}," +
                                "esp={4:x8},beg={5:x8},lim={6:x8}\n",
                                __arglist((int)size,
                                          (UIntPtr)ptr,
                                          (UIntPtr)arg2,
                                          (int)args,
                                          esp,
                                          begin,
                                          limit));
            }
#endif
            StackHead *head = GetStackSegmentRaw(size, ref context, false);
            if (head == null) {
                // We are in serious trouble...
                DebugStub.Break();
                return (UIntPtr)head;
            }

            head->prevBegin = begin;
            head->prevLimit = limit;
            head->esp = esp;
            arg2 += args;
            uint *nsp = (uint *)head;
            for (uint n = 0; n < args; n++) {
                *--nsp = *--arg2;
            }
#if DEBUG_STACK_VERBOSE
            DebugStub.Print("  [old {0:x8}..{1:x8}..{2:x8}] {3:x8} {4:x8} {5:x8}\n",
                            __arglist(limit, (UIntPtr)(arg2 - 2), begin,
                                      arg2[-2], arg2[-1], arg2[0]));
            DebugStub.Print("  [raw {0:x8}..{1:x8}..{2:x8}]\n",
                            __arglist(context.stackLimit,
                                      (UIntPtr)head,
                                      context.stackBegin));
            DebugStub.Print("  [new {0:x8}..{1:x8}..{2:x8}] {3:x8} {4:x8} {5:x8}\n",
                            __arglist(context.stackLimit,
                                      (UIntPtr)nsp,
                                      context.stackBegin,
                                      nsp[0], nsp[1], nsp[2]));

            DebugStub.Print("  [ret {0:x8}] {1:x8} {2:x8}\n",
                            __arglist((UIntPtr)(arg2 + args - 5),
                                      arg2[-2], arg2[-1]));
#endif
            return (UIntPtr)nsp;
        }

        [AccessedByRuntime("referenced from halstack.asm")]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        internal static unsafe void ReturnStackSegmentRaw(ref ThreadContext context,
                                                          UIntPtr begin,
                                                          UIntPtr limit)
        {
            StackHead *head = (StackHead *)(begin - sizeof(StackHead));

#if NO_TRACE_STACKS
#else
            Kernel.Waypoint(669);
#endif

            UIntPtr addr = limit;
            UIntPtr size = begin - limit;

#if DEBUG_STACK_VERBOSE
            fixed (ThreadContext *ptr = &context) {
                DebugStub.Print("ReturnStackSegmentRaw(ctx={0:x8},beg={1:x8},lim={2:x8}\n",
                                __arglist((UIntPtr)ptr, begin, limit));
            }
#if TEST_STACK_LINKING
            DumpStack(Processor.GetFramePointer());
#endif
#endif

#if !PAGING
            context.stackBegin = head->prevBegin;
            context.stackLimit = head->prevLimit;
#else
            //context.stackBegin = head->prevBegin;
            //context.stackLimit = head->prevLimit;
            // Moved below, because of the following scenario:
            //   - call UnlinkStack
            //   - UnlinkStack switches to the scheduler stack
            //   - UnlinkStack calls ReturnStackSegmentRaw, which calls
            //     various other methods
            //   - one of the other methods invokes write barrier code
            //   - the write barrier code performs a stack link check
            //   - If context.stackLimit is already set to head->prevLimit,
            //     then it may appear that we're out of stack space,
            //     even if we're really not, so we jump to LinkStack
            //   - LinkStack overwrites the scheduler stack
            // TODO: really fix this.
            UIntPtr stackBegin = head->prevBegin;
            UIntPtr stackLimit = head->prevLimit;
#endif

            Process owner = Process.GetProcessByID(context.processId);
            /*
              // See note above in GetStackSegmentRaw
            if ((owner != Process.kernelProcess) &&
                (addr >= BootInfo.KERNEL_BOUNDARY)) {
                MemoryManager.UserFree(addr, MemoryManager.PagesFromBytes(size), owner);
            } else {
                MemoryManager.KernelFree(addr, MemoryManager.PagesFromBytes(size), owner);
            }
            */
            MemoryManager.KernelFree(addr, MemoryManager.PagesFromBytes(size), owner);

            ReturnCount++;

#if DEBUG_STACK_VERBOSE
            DebugStub.Print("ReturnStackSegment({0:x8}, {1:x8}\n",
                            __arglist(addr, size));
#endif
#if PAGING
            // See comments above.
            context.stackBegin = stackBegin;
            context.stackLimit = stackLimit;
#endif
        }

        [AccessedByRuntime("referenced from halstack.asm")]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        // NB: This function must execute in low-stack conditions!
        // See the comment at the top of this file.
        internal static unsafe void ReturnStackSegment(ref ThreadContext context)
        {
            ReturnStackSegmentRaw(ref context, context.stackBegin, context.stackLimit);
        }

        internal static unsafe void WalkStack(UIntPtr ebp)
        {
            System.GCs.CallStack.TransitionRecord *kernMarker;
            System.GCs.CallStack.TransitionRecord *procMarker;

            kernMarker = Processor.GetCurrentThreadContext()->stackMarkers;
            procMarker = Processor.GetCurrentThreadContext()->processMarkers;
            UIntPtr ebpKern = kernMarker != null ? kernMarker->EBP : UIntPtr.Zero;
            UIntPtr ebpProc = procMarker != null ? procMarker->EBP : UIntPtr.Zero;

#if DEBUG_STACK_VERBOSE
            fixed (byte * begin = &LinkedStackBegin) {
                fixed (byte * limit = &LinkedStackLimit) {
                    DebugStub.Print("LinkedStack: {0:x8}..{1:x8}\n",
                                    __arglist((UIntPtr)begin, (UIntPtr)limit));
                }
            }

            fixed (byte * begin = &LinkStackBegin) {
                fixed (byte * limit = &LinkStackLimit) {
                    DebugStub.Print("LinkStack:   {0:x8}..{1:x8}\n",
                                    __arglist((UIntPtr)begin, (UIntPtr)limit));
                }
            }

            fixed (byte * begin = &UnlinkStackBegin) {
                fixed (byte * limit = &UnlinkStackLimit) {
                    DebugStub.Print("UnlinkStack: {0:x8}..{1:x8}\n",
                                    __arglist((UIntPtr)begin, (UIntPtr)limit));
                }
            }
#endif

            DebugStub.Print("EBP={0:x8}, kernMarkers={1:x8}, procMarkers={2:x8}\n",
                            __arglist(ebp, (UIntPtr)kernMarker, (UIntPtr)procMarker));
            DebugStub.Print("EBP.....: EBP..... EIP..... transitn nexttran stbottom\n");

            while (ebp != UIntPtr.Zero) {
                if (ebp == ebpKern) {
                    DebugStub.Print("--kern--: {0:x8} {1:x8} {2:x8} {3:x8} {4:x8}\n",
                                    __arglist(ebpKern,
                                              (UIntPtr)kernMarker,
                                              kernMarker->callAddr,
                                              (UIntPtr)kernMarker->oldTransitionRecord,
                                              kernMarker->stackBottom));
                    kernMarker = kernMarker->oldTransitionRecord;
                    ebpKern = kernMarker != null ? kernMarker->EBP : UIntPtr.Zero;
                }
                if (ebp == ebpProc) {
                    DebugStub.Print("--proc--: {0:x8} {1:x8} {2:x8} {3:x8} {4:x8}: \n",
                                    __arglist(ebpProc,
                                              (UIntPtr)procMarker,
                                              procMarker->callAddr,
                                              (UIntPtr)procMarker->oldTransitionRecord,
                                              procMarker->stackBottom));

                    procMarker = procMarker->oldTransitionRecord;
                    ebpProc = procMarker != null ? procMarker->EBP : UIntPtr.Zero;
                }
                DebugStub.Print("{0:x8}: {1:x8} {2:x8}\n",
                                __arglist(ebp,
                                          ((UIntPtr*)ebp)[0], ((UIntPtr*)ebp)[1]));

                if (((UIntPtr*)ebp)[1] == UIntPtr.Zero) {
                    break;
                }
                ebp = ((UIntPtr*)ebp)[0];
            }
            // DebugStub.Break();
        }

        //////////////////////////////////////////////////// LinkStackN Stubs.
        //
        // Note: As per the description in SDN20, the LinkStackN stubs use
        //       a non-standard calling convention.  However, they are
        //       provided here to given Bartok a symbol to reference.
        //
        [ExternalStaticData]
        internal static byte LinkedStackBegin;
        [ExternalStaticData]
        internal static byte LinkStackBegin;
        [ExternalStaticData]
        internal static byte LinkStackLimit;
        [ExternalStaticData]
        internal static byte UnlinkStackBegin;
        [ExternalStaticData]
        internal static byte UnlinkStackLimit;
        [ExternalStaticData]
        internal static byte LinkedStackLimit;

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [RequiredByBartok]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack0(); // Copy 0 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [RequiredByBartok]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack4(); // Copy 4 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [RequiredByBartok]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack8(); // Copy 8 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack12(); // Copy 12 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack16(); // Copy 16 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack20(); // Copy 20 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack24(); // Copy 24 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack28(); // Copy 28 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack32(); // Copy 32 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack36(); // Copy 36 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack40(); // Copy 40 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack44(); // Copy 44 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack48(); // Copy 48 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack52(); // Copy 52 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack56(); // Copy 56 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack60(); // Copy 60 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [RequiredByBartok]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void LinkStack64(); // Copy 64 bytes of arguments on stack.


        ////////////////////////////////////////////////// UnlinkStackN Stubs.
        //
        // Note: As per the description in SDN20, the UnlinkStackN stubs use
        //       a non-standard calling convention and should be referenced
        //       only by the LinkStackN stubs which insert them into the stack.
        //       However, they are provided here for completeness.
        //

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack0(); // Remove 0 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack4(); // Remove 4 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack8(); // Remove 8 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack12(); // Remove 12 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack16(); // Remove 16 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack20(); // Remove 20 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack24(); // Remove 24 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack28(); // Remove 28 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack32(); // Remove 32 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack36(); // Remove 36 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack40(); // Remove 40 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack44(); // Remove 44 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack48(); // Remove 48 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack52(); // Remove 52 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack56(); // Remove 56 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack60(); // Remove 60 bytes of arguments on stack.

        [StackBound(64)]
        [NoStackLinkCheck]
        [NoStackOverflowCheck]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void UnlinkStack64(); // Remove 64 bytes of arguments on stack.

#if TEST_STACK_LINKING
        [NoStackLinkCheck]
        internal static unsafe void TestStack()
        {
            ThreadContext *thread = Processor.GetCurrentThreadContext();
            ProcessorContext *processor = Processor.GetCurrentProcessorContext();
            UIntPtr oldLimit = thread->stackLimit;
            UIntPtr newLimit = (Processor.GetStackPointer() - 512);

            DebugStub.Print("************************************************************\n");
            DebugStub.Print("threadContext={0:x8}, stackBegin={1:x8}, "+
                            "stackLimit={2:x8}, "+
                            "esp={3:x8}, stackLimit={4:x8}\n",
                            __arglist(
                                (UIntPtr)thread,
                                thread->stackBegin,
                                oldLimit,
                                Processor.GetStackPointer(),
                                newLimit));
            DebugStub.Print("processorContext={0:x8}, "+
                            "schedulerStackBegin={1:x8},"+
                            "schedulerStackLimit={2:x8}\n",
                            __arglist(
                                (UIntPtr)processor,
                                processor->schedulerStackBegin,
                                processor->schedulerStackLimit));
            DebugStub.Print("[TestStack.\n");
            Test1(1);
            DebugStub.Print(".TestStack]\n");
            thread->stackLimit = oldLimit;
        }

        internal static unsafe void DumpStack(UIntPtr ebp)
        {
            ThreadContext *thread = Processor.GetCurrentThreadContext();
            UIntPtr begin = thread->stackBegin;

            DebugStub.Print("  [{0:x8}..{1:x8}]: "+
                            "{2:x8} {3:x8} {4:x8} {5:x8} {6:x8} {7:x8} {8:x8} {8:x8}\n",
                            __arglist(
                                ebp,
                                begin,
                                ((uint*)ebp)[0],
                                ((uint*)ebp)[1],
                                ((uint*)ebp)[2],
                                ((uint*)ebp)[3],
                                ((uint*)ebp)[4],
                                ((uint*)ebp)[5],
                                ((uint*)ebp)[6],
                                ((uint*)ebp)[7]));
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test1(int a)
        {
            DebugStub.Print("[Test1. ({0})\n", __arglist(a));
            DumpStack(Processor.GetFramePointer());
            TestStackLink1024();
            a = Test2(a, a + 1) + 1;
            DebugStub.Print(".Test1] = {0}\n", __arglist(a));
            return a;
        }

        [OutsideGCDomain]
        [NoInline]
        [StackLinkCheck]
        internal static int Test2(int a, int b)
        {
            DebugStub.Print("[Test2. ({0}, {1})\n",
                            __arglist(a, b));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096(); //TestStackLink1024();
            a = Test3(a, a + 1, a + 2) + 1;
            DebugStub.Print(".Test2] = {0}", __arglist(a));
            return a;
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test3(int a, int b, int c)
        {
            DebugStub.Print("[Test3. ({0},{1},{2})",
                            __arglist(a, b, c));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096(); //TestStackLink1024();
            a = Test4(a, a + 1, a + 2, a + 3) + 1;
            DebugStub.Print(".Test3] = {0}", __arglist(a));
            return a;
        }

        [OutsideGCDomain]
        [NoInline]
        [StackLinkCheck]
        internal static int Test4(int a, int b, int c, int d)
        {
            DebugStub.Print("[Test4. ({0},{1},{2},{3})",
                            __arglist(a, b, c, d));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096(); //TestStackLink2048();
            a = Test5(a, a + 1, a + 2, a + 3, a + 4) + 1;
            DebugStub.Print(".Test4] = {0}", __arglist(a));
            return a;
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test5(int a, int b, int c, int d, int e)
        {
            DebugStub.Print("[Test5. ({0},{1},{2},{3},{4})",
                            __arglist(a, b, c, d, e));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096(); //TestStackLink2048();
            a = Test6(a, a + 1, a + 2, a + 3, a + 4, a + 5) + 1;
            DebugStub.Print(".Test5] = {0}", __arglist(a));
            return a;
        }

        [OutsideGCDomain]
        [NoInline]
        [StackLinkCheck]
        internal static int Test6(int a, int b, int c, int d, int e, int f)
        {
            DebugStub.Print("[Test6. ({0},{1},{2},{3},{4},{5})",
                            __arglist(a, b, c, d, e, f));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096(); //TestStackLink2048();
            a = Test7(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6) + 1;
            DebugStub.Print(".Test6] = {0}", __arglist(a));
            return a;
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test7(int a, int b, int c, int d, int e, int f,
                                  int g)
        {
            DebugStub.Print("[Test7. ({0},{1},{2},{3},{4},{5},{6})",
                            __arglist(a, b, c, d, e, f, g));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096();
            a = Test8(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7) + 1;
            DebugStub.Print(".Test7] = {0}", __arglist(a));
            return a;
        }

        [OutsideGCDomain]
        [NoInline]
        [StackLinkCheck]
        internal static int Test8(int a, int b, int c, int d, int e, int f,
                                  int g, int h)
        {
            DebugStub.Print("[Test8. ({0},{1},{2},{3},{4},{5},{6},{7})",
                            __arglist(a, b, c, d, e, f, g, h));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096();
            a = Test9(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                      a + 8) + 1;
            DebugStub.Print(".Test8] = {0}", __arglist(a));
            return a;
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test9(int a, int b, int c, int d, int e, int f,
                                  int g, int h, int i)
        {
            DebugStub.Print("[Test9. ({0},{1},{2},{3},{4},{5},{6},{7},{8})",
                            __arglist(a, b, c, d, e, f, g, h, i));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096();
            a = Test10(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                      a + 8, a + 9) + 1;
            DebugStub.Print(".Test9] = {0}", __arglist(a));
            return a;
        }

        [OutsideGCDomain]
        [NoInline]
        [StackLinkCheck]
        internal static int Test10(int a, int b, int c, int d, int e, int f,
                                  int g, int h, int i, int j)
        {
            DebugStub.Print("[Test10. ({0},{1},{2},{3},{4},{5},{6},{7},{8},"+
                            "{9})",
                            __arglist(a, b, c, d, e, f, g, h, i, j));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096();
            a = Test11(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                       a + 8, a + 9, a + 10) + 1;
            DebugStub.Print(".Test10] = {0}", __arglist(a));
            return a;
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test11(int a, int b, int c, int d, int e, int f,
                                   int g, int h, int i, int j, int k)
        {
            DebugStub.Print("[Test11. ({0},{1},{2},{3},{4},{5},{6},{7},{8},"+
                            "{9},{10})",
                            __arglist(a, b, c, d, e, f, g, h, i, j, k));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096();
            a = Test12(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                       a + 8, a + 9, a + 10, a + 11) + 1;
            DebugStub.Print(".Test11] = {0}", __arglist(a));
            return a;
        }

        [OutsideGCDomain]
        [NoInline]
        [StackLinkCheck]
        internal static int Test12(int a, int b, int c, int d, int e, int f,
                                   int g, int h, int i, int j, int k, int l)
        {
            DebugStub.Print("[Test12. ({0},{1},{2},{3},{4},{5},{6},{7},{8},"+
                            "{9},{10},{11})",
                            __arglist(a, b, c, d, e, f, g, h, i, j, k, l));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096();
            a = Test13(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                       a + 8, a + 9, a + 10, a + 11, a + 12) + 1;
            DebugStub.Print(".Test12] = {0}", __arglist(a));
            return a;
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test13(int a, int b, int c, int d, int e, int f,
                                   int g, int h, int i, int j, int k, int l,
                                   int m)
        {
            DebugStub.Print("[Test13. ({0},{1},{2},{3},{4},{5},{6},{7},{8},"+
                            "{9},{10},{11},{12})",
                            __arglist(a, b, c, d, e, f, g, h, i, j, k, l, m));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096();
            a = Test14(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                       a + 8, a + 9, a + 10, a + 11, a + 12, a + 13) + 1;
            DebugStub.Print(".Test13] = {0}", __arglist(a));
            return a;
        }

        [OutsideGCDomain]
        [NoInline]
        [StackLinkCheck]
        internal static int Test14(int a, int b, int c, int d, int e, int f,
                                   int g, int h, int i, int j, int k, int l,
                                   int m, int n)
        {
            DebugStub.Print("[Test14. ({0},{1},{2},{3},{4},{5},{6},{7},{8},"+
                            "{9},{10},{11},{12},{13})",
                            __arglist(a, b, c, d, e, f, g, h, i, j, k, l,
                                      m, n));
            DumpStack(Processor.GetFramePointer());
            TestStackLink4096();
            a = Test15(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                       a + 8, a + 9, a + 10, a + 11, a + 12, a + 13, a + 14) + 1;
            DebugStub.Print(".Test14] = {0}", __arglist(a));
            return a;
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test15(int a, int b, int c, int d, int e, int f,
                                   int g, int h, int i, int j, int k, int l,
                                   int m, int n, int o)
        {
            DebugStub.Print("[Test15. ({0},{1},{2},{3},{4},{5},{6},{7},{8},"+
                            "{9},{10},{11},{12},{13},{14})",
                            __arglist(a, b, c, d, e, f, g, h, i, j, k, l,
                                      m, n, o));
            DumpStack(Processor.GetFramePointer());
            TestStackLink8192();
            a = Test16(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                       a + 8, a + 9, a + 10, a + 11, a + 12, a + 13, a + 14,
                       a + 15) + 1;
            DebugStub.Print(".Test15] = {0}", __arglist(a));
            return a;
        }

        [OutsideGCDomain]
        [NoInline]
        [StackLinkCheck]
        internal static int Test16(int a, int b, int c, int d, int e, int f,
                                   int g, int h, int i, int j, int k, int l,
                                   int m, int n, int o, int p)
        {
            DebugStub.Print("[Test16. ({0},{1},{2},{3},{4},{5},{6},{7},{8},"+
                            "{9},{10},{11},{12},{13},{14},{15})",
                            __arglist(a, b, c, d, e, f, g, h, i, j, k, l,
                                      m, n, o, p));
            DumpStack(Processor.GetFramePointer());
            TestStackLink8192();
            a = Test17(a, a + 1, a + 2, a + 3, a + 4, a + 5, a + 6, a + 7,
                       a + 8, a + 9, a + 10, a + 11, a + 12, a + 13, a + 14,
                       a + 15, a + 16) + 1;
            DebugStub.Print(".Test16] = {0}", __arglist(a));
            return a;
        }

        [NoInline]
        [StackLinkCheck]
        internal static int Test17(int a, int b, int c, int d, int e, int f,
                                   int g, int h, int i, int j, int k, int l,
                                   int m, int n, int o, int p, int q)
        {
            DebugStub.Print("[Test17. ({0},{1},{2},{3},{4},{5},{6},{7},{8},"+
                            "{9},{10},{11},{12},{13},{14},{15},{16})",
                            __arglist(a, b, c, d, e, f, g, h, i, j, k, l,
                                      m, n, o, p, q));
            DumpStack(Processor.GetFramePointer());
            TestStackLink8192();
            a = a + 1;
            WalkStack(Processor.GetFramePointer());
            DebugStub.Print(".Test17] = {0}", __arglist(a));
            return a;
        }

        [AccessedByRuntime("output to header : defined in stacks.cpp")]
        [StackBound(1024)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void TestStackLink1024();

        [AccessedByRuntime("output to header : defined in stacks.cpp")]
        [StackBound(2048)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void TestStackLink2048();

        [AccessedByRuntime("output to header : defined in stacks.cpp")]
        [StackBound(4096)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void TestStackLink4096();

        [AccessedByRuntime("output to header : defined in stacks.cpp")]
        [StackBound(8192)]
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void TestStackLink8192();

#endif
    }
}
