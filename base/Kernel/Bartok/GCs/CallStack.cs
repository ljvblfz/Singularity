/*******************************************************************/
/*                           WARNING                               */
/* This file should be identical in the Bartok and Singularity     */
/* depots. Master copy resides in Bartok Depot. Changes should be  */
/* made to Bartok Depot and propagated to Singularity Depot.       */
/*******************************************************************/

//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

namespace System.GCs {

    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Threading;

    using Microsoft.Bartok.Options;
    using Microsoft.Bartok.Runtime;

#if SINGULARITY_KERNEL
    using Microsoft.Singularity.X86;
#elif SINGULARITY_PROCESS
    using Microsoft.Singularity.X86;
    using Microsoft.Singularity.V1.Services;
#endif

    [NoCCtor]
    [RequiredByBartok]
    internal unsafe class CallStack
    {

        [Mixin(typeof(Thread))] 
        private class CallStackThread : Object {
            [AccessedByRuntime("reference in brtstack.asm")]
            internal UIntPtr asmStackBase; // Limit of thread's stack
            [AccessedByRuntime("reference in brtstack.asm")]
            internal UIntPtr asmStackLimit;
            [AccessedByRuntime("reference in brtforgc.asm")]
            internal unsafe TransitionRecord *asmStackMarker;
        }

        private static CallStackThread MixinThread(Thread t) {
            return (CallStackThread) (Object) t;
        }

        internal static UIntPtr StackBase(Thread t) {
            return MixinThread(t).asmStackBase;
        }

        internal static void SetStackBase(Thread t, UIntPtr value) {
            MixinThread(t).asmStackBase = value;
        }

        internal static UIntPtr StackLimit(Thread t) {
            return MixinThread(t).asmStackLimit;
        }

        internal static void SetStackLimit(Thread t, UIntPtr value) {
            MixinThread(t).asmStackLimit = value;
        }

        internal static TransitionRecord* StackMarker(Thread t)
        {
            return MixinThread(t).asmStackMarker;
        }

#if X86
        [AccessedByRuntime("referenced from halforgc.asm")]
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct TransitionRecord {
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal TransitionRecord *oldTransitionRecord;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr callAddr;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr stackBottom;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr EBX;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr EDI;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr ESI;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr EBP;
        }
#elif AMD64
        [AccessedByRuntime("referenced from halforgc.asm")]
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct TransitionRecord {
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal TransitionRecord *oldTransitionRecord;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr callAddr;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr stackBottom;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr EBX;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr EDI;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr ESI;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr EBP;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr R12;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr R13;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr R14;
            [AccessedByRuntime("referenced from halforgc.asm")]
            internal UIntPtr R15;
        }
#endif
        internal static Thread threadBeingProcessed;

        private struct CalleeSaveLocations {

#if SINGULARITY_KERNEL
            internal void SetCalleeSaves(ThreadContext *context) {
                EBX.SetCalleeReg(&context->ebx);
                EDI.SetCalleeReg(&context->edi);
                ESI.SetCalleeReg(&context->esi);
                EBP.SetCalleeReg(&context->ebp);
            }
#endif

#if X86
            internal void SetCalleeSaves(TransitionRecord *transitionRecord)
            {
                EBX.SetCalleeReg(&transitionRecord->EBX);
                EDI.SetCalleeReg(&transitionRecord->EDI);
                ESI.SetCalleeReg(&transitionRecord->ESI);
                EBP.SetCalleeReg(&transitionRecord->EBP);
            }

            internal void ClearCalleeSaves() {
                EBX.ClearCalleeReg();
                ESI.ClearCalleeReg();
                EDI.ClearCalleeReg();
                EBP.ClearCalleeReg();
            }

            internal
            void ScanLiveRegs(uint mask,
                              NonNullReferenceVisitor referenceVisitor)
            {
                EDI.ScanLiveReg((mask >> 2) & 0x3, referenceVisitor);
                ESI.ScanLiveReg((mask >> 4) & 0x3, referenceVisitor);
                EBX.ScanLiveReg((mask >> 0) & 0x3, referenceVisitor);
                EBP.ScanLiveReg((mask >> 6) & 0x3, referenceVisitor);
            }

            internal void PopFrame(UIntPtr *framePointer,
                                   uint calleeSaveMask,
                                   bool framePointerOmitted)
            {
                UIntPtr *calleeSaveStart;
                if (framePointerOmitted) {
                    calleeSaveStart = framePointer - 1;
                } else {
                    VTable.Assert((calleeSaveMask & 0x10) == 0,
                                  "EBP should not be callee saved");
                    calleeSaveStart = framePointer;
                    EBP.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x1) != 0) {
                    calleeSaveStart -=
                        sizeof(TransitionRecord) / sizeof(UIntPtr);
                }

                // Note: the order in which these appear is important!
                if ((calleeSaveMask & 0x2) != 0) {
                    EBX.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x10) != 0) {
                    EBP.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x8) != 0) {
                    ESI.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x4) != 0) {
                    EDI.PopFrameReg(ref calleeSaveStart);
                }
            }

            internal void ClearFrame(uint calleeSaveMask,
                                     bool framePointerOmitted)
            {
                if (!framePointerOmitted) {
                    VTable.Assert((calleeSaveMask & 0x10) == 0,
                                  "EBP should not be callee saved");
                    EBP.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x2) != 0) {
                    EBX.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x10) != 0) {
                    EBP.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x8) != 0) {
                    ESI.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x4) != 0) {
                    EDI.ClearFrameReg();
                }
            }
#elif AMD64
            internal void SetCalleeSaves(TransitionRecord *transitionRecord)
            {
                EBX.SetCalleeReg(&transitionRecord->EBX);
                EDI.SetCalleeReg(&transitionRecord->EDI);
                ESI.SetCalleeReg(&transitionRecord->ESI);
                EBP.SetCalleeReg(&transitionRecord->EBP);
                R12.SetCalleeReg(&transitionRecord->R12);
                R13.SetCalleeReg(&transitionRecord->R13);
                R14.SetCalleeReg(&transitionRecord->R14);
                R15.SetCalleeReg(&transitionRecord->R15);
            }

            internal void ClearCalleeSaves() {
                EBX.ClearCalleeReg();
                ESI.ClearCalleeReg();
                EDI.ClearCalleeReg();
                EBP.ClearCalleeReg();
                R12.ClearCalleeReg();
                R13.ClearCalleeReg();
                R14.ClearCalleeReg();
                R15.ClearCalleeReg();
            }

            internal
            void ScanLiveRegs(uint mask,
                              NonNullReferenceVisitor referenceVisitor)
            {
                EDI.ScanLiveReg((mask >> 2) & 0x3, referenceVisitor);
                ESI.ScanLiveReg((mask >> 4) & 0x3, referenceVisitor);
                EBX.ScanLiveReg((mask >> 0) & 0x3, referenceVisitor);
                R12.ScanLiveReg((mask >> 6) & 0x3, referenceVisitor);
                R13.ScanLiveReg((mask >> 8) & 0x3, referenceVisitor);
                R14.ScanLiveReg((mask >> 10) & 0x3, referenceVisitor);
                R15.ScanLiveReg((mask >> 12) & 0x3, referenceVisitor);
                EBP.ScanLiveReg((mask >> 14) & 0x3, referenceVisitor);
            }

            internal void PopFrame(UIntPtr *framePointer,
                                   uint calleeSaveMask,
                                   bool framePointerOmitted)
            {
                UIntPtr *calleeSaveStart;
                if (framePointerOmitted) {
                    calleeSaveStart = framePointer - 1;
                } else {
                    VTable.Assert((calleeSaveMask & 0x100) == 0,
                                  "EBP should not be callee saved");
                    calleeSaveStart = framePointer;
                    EBP.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x1) != 0) {
                    calleeSaveStart -=
                        sizeof(TransitionRecord) / sizeof(UIntPtr);
                }

                // Note: the order in which these appear is important!
                if ((calleeSaveMask & 0x2) != 0) {
                    EBX.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x100) != 0) {
                    EBP.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x8) != 0) {
                    ESI.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x4) != 0) {
                    EDI.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x10) != 0) {
                    R12.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x20) != 0) {
                    R13.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x40) != 0) {
                    R14.PopFrameReg(ref calleeSaveStart);
                }
                if ((calleeSaveMask & 0x80) != 0) {
                    R15.PopFrameReg(ref calleeSaveStart);
                }
            }

            internal void ClearFrame(uint calleeSaveMask,
                                     bool framePointerOmitted)
            {
                if (!framePointerOmitted) {
                    VTable.Assert((calleeSaveMask & 0x100) == 0,
                                  "EBP should not be callee saved");
                    EBP.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x2) != 0) {
                    EBX.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x100) != 0) {
                    EBP.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x8) != 0) {
                    ESI.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x4) != 0) {
                    EDI.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x10) != 0) {
                    R12.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x20) != 0) {
                    R13.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x40) != 0) {
                    R14.ClearFrameReg();
                }
                if ((calleeSaveMask & 0x80) != 0) {
                    R15.ClearFrameReg();
                }

            }
#endif

            internal UIntPtr GetFramePointer()
            {
                return EBP.value;
            }

            private struct RegLocation
            {

                [Inline]
                internal void SetCalleeReg(UIntPtr *regField)
                {
                    this.pending = false;
                    this.value = *regField;
                    this.head = regField;
                    *regField = UIntPtr.Zero;
                }

                internal void ClearCalleeReg()
                {
                    VTable.Deny(this.pending);
                    UIntPtr *scan = this.head;
                    while (scan != null) {
                        UIntPtr temp = *scan;
                        *scan = value;
                        scan  = (UIntPtr *) temp;
                    }
                    this.head = null;
                }

                internal void ScanLiveReg(uint kind,
                                          NonNullReferenceVisitor visitor)
                {
                    switch (kind) {
                      case 0: {
                          // Value is not a traceable heap pointer
                          break;
                      }
                      case 1: {
                          // Value is a pointer variable
                          VTable.Deny(this.head == null);
                          if (value != UIntPtr.Zero) {
                              fixed (UIntPtr *valueField = &this.value) {
                                  visitor.Visit(valueField);
                              }
                          }
                          ClearCalleeReg();
                          break;
                      }
                      case 2: {
                          // Value is unchanged since function entry
                          VTable.Deny(this.pending);
                          this.pending = true;
                          break;
                      }
                      case 3:
                      default: {
                          VTable.NotReached("ScanLiveReg 3 or default");
                          break;
                      }
                    }
                }

                internal void PopFrameReg(ref UIntPtr *calleeSaveStart)
                {
                    if (this.head != null && !this.pending) {
                        ClearCalleeReg();
                    }
                    if (this.head == null) {
                        this.value = *calleeSaveStart;
                    } else {
                        VTable.Assert(this.pending, "pending should be true");
                        VTable.Assert(*calleeSaveStart == this.value,
                                      "values are not equal");
                    }
                    this.pending = false;
                    *calleeSaveStart = (UIntPtr) this.head;
                    this.head = calleeSaveStart;
                    calleeSaveStart--;
                }

                internal void ClearFrameReg() {
                    UIntPtr *scan = this.head;
                    while (scan != null) {
                        UIntPtr temp = *scan;
                        *scan = value;
                        scan = (UIntPtr *) temp;
                    }
                    this.head = null;
                    this.pending = false;
                    this.value = UIntPtr.Zero;
                }

                internal bool pending;
                internal UIntPtr value;
                internal UIntPtr *head;

            }

#if X86
            RegLocation EBX;
            RegLocation EDI;
            RegLocation ESI;
            RegLocation EBP;
#elif AMD64
            RegLocation EBX;
            RegLocation EDI;
            RegLocation ESI;
            RegLocation EBP;
            RegLocation R12;
            RegLocation R13;
            RegLocation R14;
            RegLocation R15;
#endif
        }

        [RequiredByBartok]
        private static int callSiteTableCount;
        [AccessedByRuntime("referenced from halexn.cpp")]
        private static UIntPtr *codeBaseStartTable;
        [RequiredByBartok]
        private static UIntPtr **returnAddressToCallSiteSetNumbers;
        [RequiredByBartok]
        private static int **callSiteSetCount;

        private static int CallSiteTableNumber(UIntPtr returnAddr)
        {
            UIntPtr address = returnAddr;
            for (int i = 0; i < callSiteTableCount; i++) {
                UIntPtr baseAddress = codeBaseStartTable[i];
                if (address < baseAddress) {
                    continue;
                }
                UIntPtr relativeAddress = address - baseAddress;
                UIntPtr *ptr = returnAddressToCallSiteSetNumbers[i];
                int callSiteCount = *(callSiteSetCount[i]);
                if (relativeAddress >= ptr[0] &&
                    relativeAddress <= ptr[callSiteCount]) {
                    return i;
                }
            }
            return -1;
        }

        private static int CallSiteSetNumber(UIntPtr returnAddr, int index)
        {
            UIntPtr codeBaseAddr = codeBaseStartTable[index];
            UIntPtr relativeAddr = returnAddr - codeBaseAddr;
            UIntPtr *callSiteTable = returnAddressToCallSiteSetNumbers[index];
            int callSiteCount = *(callSiteSetCount[index]);
            int left = 0;
            int right = callSiteCount;
            // Loop invariant:
            //   callSiteTable[left] <= returnAddress < callSiteTable[right]
            while (left < right-1) {
                int mid = (left + right)/2;
                if (callSiteTable[mid] <= relativeAddr) {
                    left = mid;
                } else {
                    right = mid;
                }
            }
            return left;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FullDescriptor {
            internal UIntPtr mask;
            internal int variableData;
        }

        private const uint ESCAPE32_TAG = 0x0;
        private const uint ESCAPE16_TAG = 0x1;
        private const uint ESCAPE8_TAG  = 0x2;

        // Check whether the specified stack frame contains the
        // transition record: if so then we're done scanning this
        // segment of the stack.  NB: the caller must ensure that the
        // framePointer has been recomputed in a
        // framePointerOmitted-method.
        private static bool FrameContainsTransitionRecord(UIntPtr *framePointer,
                                                          UIntPtr *stackPointer,
                                                          TransitionRecord *stopMarker) {
            bool result = false;
            if ((framePointer >= stopMarker) && (stackPointer < stopMarker)) {
                result = true;
            }
            return result;
        }

        // Returns the return address in the calling method
        private static
        bool WalkStackFrame(ref UIntPtr *framePointer,
                            ref UIntPtr *stackPointer,
                            ref CalleeSaveLocations calleeSaves,
                            ref UIntPtr returnAddr,
                            NonNullReferenceVisitor threadReferenceVisitor,
                            NonNullReferenceVisitor pinnedReferenceVisitor,
                            UIntPtr frameDescriptorEntryPtr,
                            TransitionRecord *stopMarker,
                            Thread thread)
            // BUGBUG: Remove thread argument when ThreadContext is on stack!
        {
            Trace.Log(Trace.Area.Stack,
                      "WalkStackFrame: fp={0:x}, sp={1:x}, returnAddr={2:x}, desc={3:x}",
                      __arglist(framePointer, stackPointer,
                                returnAddr, frameDescriptorEntryPtr));
            bool framePointerOmitted;
            uint liveRegs, calleeSaveMask;
            //uint frameDescriptorEntry = (uint) frameDescriptorEntryPtr;
            UIntPtr frameDescriptorEntry = frameDescriptorEntryPtr;
            if ((frameDescriptorEntry & 0x1) != 0) {
                // Compact descriptor
                UIntPtr mask;
                framePointerOmitted =
                    (((frameDescriptorEntry >> 1) & 0x1) != 0);
                int inbetweenSlots;
                if (framePointerOmitted) {
                    // No frame pointer
                    inbetweenSlots = Constants.InbetweenSlotsNoFP;
                    mask =
                        frameDescriptorEntry >> Constants.CompactStackBitMaskStartNoFP;
                    calleeSaveMask =
                        (uint)((frameDescriptorEntry >> Constants.CompactEntryMaskStart)
                                & Constants.CompactEntryMaskNoFP);
                    liveRegs = (uint)((frameDescriptorEntry >>
                                       Constants.CompactCalleeSaveUseStartNoFP) &
                                      Constants.CompactCalleeSaveUseMaskNoFP);
                    // when frame pointer is omitted, the stack top
                    // equals stackPointer plus frameSize
                    int frameSize =
                        unchecked((int)(frameDescriptorEntry >>
                                        Constants.CompactFrameSizeStartNoFP) &
                                  Constants.CompactFrameSizeMaskNoFP);
                    framePointer = stackPointer + frameSize;
                } else {
                    // Use frame pointer
                    inbetweenSlots = Constants.InbetweenSlotsFP;
                    mask = frameDescriptorEntry >> Constants.CompactStackBitMaskStartFP;
                    calleeSaveMask =
                        (uint)((frameDescriptorEntry >> Constants.CompactEntryMaskStart)
                                & Constants.CompactEntryMaskFP);
                    liveRegs =
                        (uint) ((frameDescriptorEntry >>
                                 Constants.CompactCalleeSaveUseStartFP) &
                                Constants.CompactCalleeSaveUseMaskFP);
                }

                Trace.Log(Trace.Area.Stack,
                          "Compact desc: fpo={0}, mask={1:x}, inbetweenSlots={2}, calleeSaveMask={3:x}, liveRegs={4:x}",
                          __arglist(framePointerOmitted, mask, inbetweenSlots,
                                    calleeSaveMask, liveRegs));
                if (FrameContainsTransitionRecord(framePointer,
                                                  stackPointer,
                                                  stopMarker)) {
                    return true; // true: done
                }
                uint stackArgSize =
                    (uint)((frameDescriptorEntry >> Constants.CompactArgMaskStart)
                                           & Constants.CompactArgMask);
                UIntPtr *p = framePointer + stackArgSize + inbetweenSlots;
                Trace.Log(Trace.Area.Stack,
                          "Compact desc: stackArgSize={0:x}, p={1:x}",
                          __arglist(stackArgSize, p));
                // Process the arguments
                if (threadReferenceVisitor != null) {
                    for (int i = 0; mask != 0 && i <= stackArgSize; i++) {
                        if ((mask & 0x1) != 0 && *p != UIntPtr.Zero) {
                            // BUGBUG: threadReferenceVisitor.Visit(p);
                            VisitIfNotContext(thread, threadReferenceVisitor,
                                              p);
                        }
                        mask >>= 1;
                        p--;
                    }
                } else {
                    for (int i = 0; mask != 0 && i <= stackArgSize; i++) {
                        mask >>= 1;
                    }
                }
                // Process the local variables
                bool transitionRecord = ((calleeSaveMask & 0x1) != 0);
                uint savedRegs = (uint) (calleeSaveMask >> 1);
                uint countRegs = ((savedRegs & 1) +
                                  ((savedRegs >> 1) & 1) +
                                  ((savedRegs >> 2) & 1) +
                                  ((savedRegs >> 3) & 1) +
                                  ((savedRegs >> 4) & 1) +
                                  ((savedRegs >> 5) & 1) +
                                  ((savedRegs >> 6) & 1) +
                                  ((savedRegs >> 7) & 1));
                int transitionRecordSize =
                    transitionRecord ?
                    sizeof(TransitionRecord)/sizeof(UIntPtr) :
                    0;
                p = framePointer - 1 - countRegs - transitionRecordSize;
                Trace.Log(Trace.Area.Stack,
                          "Compact desc: savedRegs={0:x}, countRegs={0:x}, transSize={0:x}, p={0:x}", __arglist(savedRegs, countRegs, transitionRecordSize));
                if (threadReferenceVisitor != null) {
                    while (mask != 0) {
                        if ((mask & 1) != 0 && *p != UIntPtr.Zero) {
                            // BUGBUG: threadReferenceVisitor.Visit(p);
                            VisitIfNotContext(thread, threadReferenceVisitor,
                                              p);
                        }
                        mask >>= 1;
                        p--;
                    }
                }
            } else {
                // Full descriptor
                FullDescriptor *desc =
                    ((FullDescriptor *) frameDescriptorEntryPtr);
                UIntPtr mask = (UIntPtr) desc->mask;
                framePointerOmitted = ((mask & 1) != 0);
                bool pinnedVars;
                uint frameTag;
                if (framePointerOmitted) {
                    // no frame pointer
                    calleeSaveMask = (uint) ((mask >> Constants.FullEntryMaskStart) &
                                         Constants.FullEntryMaskNoFP);
                    pinnedVars = (((mask >> Constants.FullPinnedPosNoFP) & 1) != 0);
                    liveRegs = (uint) (( mask >> Constants.FullCalleeSaveUseStartNoFP) &
                                       Constants.FullCalleeSaveUseMaskNoFP);
                    frameTag = (uint) ((mask >> Constants.FullRecordSizePosNoFP) &
                                       Constants.FullRecordMask);
                    // when frame pointer is omitted, the stack top
                    // equals stackPointer plus frameSize
                    int frameSize = unchecked((int)(mask >> Constants.FullFrameSizeStartNoFP));
                    framePointer = stackPointer + frameSize;
                } else {
                    // use frame pointer
                    calleeSaveMask = (uint) ((mask >> Constants.FullEntryMaskStart) &
                                             Constants.FullEntryMaskFP);
                    pinnedVars = (((mask >> Constants.FullPinnedPosFP) & 1) != 0);
                    liveRegs = (uint) (( mask >> Constants.FullCalleeSaveUseStartFP) &
                                       Constants.FullCalleeSaveUseMaskFP);
                    frameTag = (uint) ((mask >> Constants.FullRecordSizePosFP) &
                                       Constants.FullRecordMask);
                }

                if (FrameContainsTransitionRecord(framePointer,
                                                  stackPointer,
                                                  stopMarker)) {
                    return true; // true: done
                }
                switch (frameTag) {
                  case ESCAPE8_TAG: {
                      int count = *(byte *) &desc->variableData;
                      byte pinnedCount = *((byte*)&desc->variableData+1);
                      sbyte *offsets = (sbyte *)&desc->variableData+2;
                      if (threadReferenceVisitor != null) {
                          for (int i = 0; i < count; i++) {
                              UIntPtr *loc = framePointer + offsets[i];
                              if (*loc != UIntPtr.Zero) {
                                  // BUGBUG: threadReferenceVisitor.Visit(loc);
                                  VisitIfNotContext(thread, threadReferenceVisitor, loc);
                              }
                          }
                      }
                      if (pinnedVars && pinnedReferenceVisitor != null) {
                          int last = count + pinnedCount;
                          for (int i = count; i < last; i++) {
                              UIntPtr *loc = framePointer + offsets[i];
                              if (*loc != UIntPtr.Zero) {
                                  pinnedReferenceVisitor.Visit(loc);
                              }
                          }
                      }
                      break;
                  }
                  case ESCAPE16_TAG: {
                      int count = *((short *) &desc->variableData);
                      short pinnedCount = *((short*)&desc->variableData+1);
                      short *offsets = (short *)&desc->variableData+2;
                      if (threadReferenceVisitor != null) {
                          for (int i = 0; i < count; i++) {
                              UIntPtr *loc = framePointer + offsets[i];
                              if (*loc != UIntPtr.Zero) {
                                  // BUGBUG: threadReferenceVisitor.Visit(loc);
                                  VisitIfNotContext(thread, threadReferenceVisitor, loc);
                              }
                          }
                      }
                      if (pinnedVars && pinnedReferenceVisitor != null) {
                          int last = count + pinnedCount;
                          for (int i = count; i < last; i++) {
                              UIntPtr *loc = framePointer + offsets[i];
                              if (*loc != UIntPtr.Zero) {
                                  pinnedReferenceVisitor.Visit(loc);
                              }
                          }
                      }
                      break;
                  }
                  case ESCAPE32_TAG: {
                      int count = *((int *) &desc->variableData);
                      int pinnedCount = *((int *) &desc->variableData+1);
                      int *offsets = (int *) &desc->variableData+2;
                      if (threadReferenceVisitor != null) {
                          for (int i = 0; i < count; i++) {
                              UIntPtr *loc = framePointer + offsets[i];
                              if (*loc != UIntPtr.Zero) {
                                  // BUGBUG: threadReferenceVisitor.Visit(loc);
                                  VisitIfNotContext(thread, threadReferenceVisitor, loc);
                              }
                          }
                      }
                      if (pinnedVars && pinnedReferenceVisitor != null) {
                          int last = count + pinnedCount;
                          for (int i = count; i < last; i++) {
                              UIntPtr *loc = framePointer + offsets[i];
                              if (*loc != UIntPtr.Zero) {
                                  pinnedReferenceVisitor.Visit(loc);
                              }
                          }
                      }
                      break;
                  }
                  default: {
                      VTable.NotReached("GC mask switch failed");
                      break;
                  }
                }
            }
            if (liveRegs != 0 && threadReferenceVisitor != null) {
                calleeSaves.ScanLiveRegs(liveRegs, threadReferenceVisitor);
            }
            UIntPtr nextReturnAddr;
            if (framePointerOmitted) {
                nextReturnAddr = *framePointer;
                stackPointer = framePointer + 1;
            } else {
                nextReturnAddr = *(framePointer + 1);
                stackPointer = framePointer + 2;
            }
            // In Singularity, the final return address of a thread is zero
            if (nextReturnAddr != UIntPtr.Zero) {
                calleeSaves.PopFrame(framePointer, calleeSaveMask,
                                     framePointerOmitted);
            } else {
                calleeSaves.ClearFrame(calleeSaveMask, framePointerOmitted);
            }
            UIntPtr *calcedfp = (UIntPtr *) calleeSaves.GetFramePointer();
            if (calcedfp != null) {
                framePointer = calcedfp;
            }
            returnAddr = nextReturnAddr;
            return false; // false: not done scanning: proceed to next frame
        }

#if SINGULARITY_PROCESS
        // BUGBUG: Get rid of this when ThreadContext is on stack! --rusa
        [Inline]
        private static
        void VisitIfNotContext(Thread thread,
                               NonNullReferenceVisitor threadReferenceVisitor,
                               UIntPtr *p)
        {
            if (*p != (UIntPtr) thread.context &&
                (int *) *p != &thread.context->gcStates) {
                threadReferenceVisitor.Visit(p);
            }
        }
#else
        [Inline]
        private static
        void VisitIfNotContext(Thread thread,
                               NonNullReferenceVisitor threadReferenceVisitor,
                               UIntPtr *p)
        {
            threadReferenceVisitor.Visit(p);
        }
#endif

        [RequiredByBartok]
        private static ushort **callSetSiteNumberToIndex;
        [RequiredByBartok]
        private static UIntPtr **activationDescriptorTable;

        [PreInitRefCounts]
        internal static
        int ScanStacks(NonNullReferenceVisitor VisitThreadReference,
                       NonNullReferenceVisitor VisitPinnedReference)
        {
            int limit = Thread.threadTable.Length;
            int countThreads = 0;
            for (int i = 0; i < limit; i++) {
                Thread t = Thread.threadTable[i];
                if (t != null) {
                    CollectorStatistics.Event(GCEvent.StackScanStart, i);
                    ScanStack(t, VisitThreadReference, VisitPinnedReference);
                    CollectorStatistics.Event(GCEvent.StackScanComplete, i);
                    countThreads++;
                }
            }
            return countThreads;
        }

        [PreInitRefCounts]
        internal static
        uint ScanStack(Thread thread,
                       NonNullReferenceVisitor VisitThreadReference,
                       NonNullReferenceVisitor VisitPinnedReference)
        {
            Trace.Log(Trace.Area.Stack,
                      "Scanning stack for thread {0:x}",
                      __arglist(thread.threadIndex));

            uint numStackFrames = 0;
            threadBeingProcessed = thread;
            CalleeSaveLocations calleeSaves = new CalleeSaveLocations();
#if SINGULARITY_KERNEL
            TransitionRecord *marker = (TransitionRecord *) thread.context.stackMarkers;
#elif SINGULARITY_PROCESS
            TransitionRecord *marker = null;
            if (thread.context != null) {
                marker = (TransitionRecord *) thread.context->stackMarkers;
            }
#else
            TransitionRecord *marker = (TransitionRecord *)
                MixinThread(thread).asmStackMarker;
#endif
            while (marker != null) {
                Trace.Log(Trace.Area.Stack,
                          "Transition record: old={0:x}, callAddr={1:x}, stackBottom={2:x}, EBX={3:x}, EDI={4:x}, ESI={5:x}, EBP={6:x}", __arglist(marker->oldTransitionRecord, marker->callAddr, marker->stackBottom, marker->EBX, marker->EDI, marker->ESI, marker->EBP));

                TransitionRecord *stopMarker = marker->oldTransitionRecord;
                UIntPtr returnAddr = marker->callAddr;
                UIntPtr *fp = (UIntPtr *) marker->EBP;
                UIntPtr *sp = (UIntPtr *) marker->stackBottom;
                calleeSaves.SetCalleeSaves(marker);
                numStackFrames +=
                    ScanStackSegment(ref calleeSaves, returnAddr, fp, sp,
                                     stopMarker, VisitThreadReference,
                                     VisitPinnedReference, thread);
                marker = marker->oldTransitionRecord;
            }
            threadBeingProcessed = null;
            return numStackFrames;
        }

        [PreInitRefCounts]
        private static
        uint ScanStackSegment(ref CalleeSaveLocations calleeSaves,
                              UIntPtr returnAddr,
                              UIntPtr *fp,
                              UIntPtr *sp,
                              TransitionRecord *stopMarker,
                              NonNullReferenceVisitor VisitThreadReference,
                              NonNullReferenceVisitor VisitPinnedReference,
                              Thread thread)
            // BUGBUG: Remove thread argument when ThreadContext is on stack!
        {
#if SINGULARITY
            UIntPtr unlinkBegin;
            UIntPtr unlinkLimit;

            fixed (byte *begin =
                   &Microsoft.Singularity.Memory.Stacks.UnlinkStackBegin) {
                unlinkBegin = (UIntPtr)begin;
            }
            fixed (byte *limit =
                   &Microsoft.Singularity.Memory.Stacks.UnlinkStackLimit) {
                unlinkLimit = (UIntPtr)limit;
            }
#endif
            uint numStackFrames = 0;
            while (true) {
#if SINGULARITY
                if (returnAddr >= unlinkBegin && returnAddr <= unlinkLimit) {
                    returnAddr = WalkStack(ref fp, ref sp);
                }
#endif

                // Exit loop if we have reached the of the stack segment
                if (fp >= stopMarker && sp < stopMarker) {
                    break;
                }
                int tableIndex = CallSiteTableNumber(returnAddr);
                if (tableIndex < 0) {
                    break;
                }
                int callSiteSet = CallSiteSetNumber(returnAddr, tableIndex);
                if (callSiteSet < 0) {
                    break;
                }
                ushort *callSiteToIndexTable =
                    callSetSiteNumberToIndex[tableIndex];
                int activationIndex = (int) callSiteToIndexTable[callSiteSet];
                VTable.Assert(activationIndex >= 0);
                UIntPtr *descriptorTable =
                    activationDescriptorTable[tableIndex];
                UIntPtr frameDescriptorEntry = descriptorTable[activationIndex];
                bool done = WalkStackFrame(ref fp, ref sp, ref calleeSaves,
                                           ref returnAddr,
                                           VisitThreadReference,
                                           VisitPinnedReference,
                                           frameDescriptorEntry,
                                           stopMarker, thread);
                if (done) {
                    break;
                }
                numStackFrames++;
            }
            calleeSaves.ClearCalleeSaves();
            return numStackFrames;
        }

        private static UIntPtr WalkStack(ref UIntPtr *framePointer,
                                         ref UIntPtr *stackPointer) {
            // the frame pointer points to the ebp in the old frame
            // since at the end of WalkStackFrame, it sets return addr
            // and pop up a frame. However, since we are using linked
            // stack, "pop up a frame" didn't actually set the frame
            // to the caller's frame, it just goes from new frame to the
            // old frame, therefore, we need to get return addr and pop
            // a frame again.
            stackPointer = framePointer + 2;
            UIntPtr returnAddr = *(framePointer + 1);
            framePointer = (UIntPtr*) *framePointer;
            return returnAddr;
        }
    }
}
