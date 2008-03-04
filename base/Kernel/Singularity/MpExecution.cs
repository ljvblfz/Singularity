///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   MpExecution.cs
//
//  Note:
//          The purpose of this class is to control the execution state of
//  processors on MP systems.  Specifically, to stop processors running
//  while the debugger is active and while a GC is running.
//
//  NB These functions may be called with the debugger
//  communications lock held.  Any calls to DebugStub.Print or
//  its ilk will cause processor to spin on lock forever.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

using Microsoft.Singularity.Hal;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.X86;

namespace Microsoft.Singularity
{
    [CLSCompliant(false)]
    public class MpExecution
    {
        // Enumeration of Processor states
        const int Uninitialized = 0x00;   // Processor state uninitialized
        const int Running       = 0x01;   // Processor running normally
        const int TargetFrozen  = 0x02;   // Processor should stop
        const int TargetThaw    = 0x04;   // Processor should continue
        const int FreezeActive  = 0x08;   // Processor is active during freeze
        const int FreezeOwner   = 0x10;   // Processor initiating freeze

        static internal volatile bool FreezeRequested;

        static SpinLock                 freezeLock;
        static volatile int             freezeCount;
        static unsafe ProcessorContext* activeCpuContext;
        static unsafe ProcessorContext* ownerCpuContext;

        [ NoHeapAllocation ]
        static internal unsafe void AddProcessorContext(ProcessorContext* context)
        {
            // Add processor to list of processors in MP system.  Careful
            // to avoid adding processor mid-freeze or without lock.
          start:
            freezeLock.Acquire();
            try {
                if (FreezeRequested)
                    goto start;
                ProcessorContext* head = Processor.processorTable[0].context;
                context->nextProcessorContext = head->nextProcessorContext;
                head->nextProcessorContext = context;
                context->ipiFreeze = Running;
                // From this point on the processor is visible
                // in the debugger
                DebugStub.AddProcessor(context->cpuId);
            }
            finally {
                freezeLock.Release();
            }
        }

        [ AccessedByRuntime("referenced from halkd.cpp") ]
        [ NoHeapAllocation ]
        [ Conditional("SINGULARITY_MP") ]
        static internal unsafe void FreezeAllProcessors()
        {
            // This method is only called on MP systems when the
            // number of running processors is greater than 1.
            freezeLock.Acquire();
            try {
                freezeCount++;
                if (FreezeRequested == true) {
                    // Processors are already frozen
                    return;
                }

                ownerCpuContext  = Processor.GetCurrentProcessorContext();
                activeCpuContext = ownerCpuContext;
                if (activeCpuContext->nextProcessorContext ==
                    activeCpuContext) {
                    return;
                }
                FreezeRequested = true;

                activeCpuContext->ipiFreeze = FreezeActive | FreezeOwner;
                HalDevices.FreezeProcessors();

                // Wait until all running processors have gone into freeze
                ProcessorContext* context = activeCpuContext->nextProcessorContext;
                do {
                    if ((context->ipiFreeze & (TargetFrozen|FreezeActive))
                        != 0) {
                        context = context->nextProcessorContext;
                    }
                } while (context->nextProcessorContext != activeCpuContext);
            }
            finally {
                freezeLock.Release();
            }
        }

        [ NoHeapAllocation ]
        static internal unsafe void
        FreezeProcessor(ref ThreadContext threadContext)
        {
            ProcessorContext* context = Processor.GetCurrentProcessorContext();

            if (context->ipiFreeze == Uninitialized) {
                // Processor was still being initialized when IPI issued
                while (FreezeRequested == true)
                    ; // just spin until thaw occurs
                return;
            }

            context->ipiFreeze = TargetFrozen;

            while (context->ipiFreeze != TargetThaw && FreezeRequested) {
                if ((context->ipiFreeze & FreezeActive) == FreezeActive) {
                    //
                    // This processor has been made the active processor
                    //
                    activeCpuContext = context;

                    // Pass state over to debugger stub
                    if (DebugStub.TrapForProcessorSwitch(ref threadContext)) {
                        // We're returning to Freeze owner, make it active
                        context->ipiFreeze         &= ~FreezeActive;
                        ownerCpuContext->ipiFreeze |= FreezeActive;
                    }
                }
            }

            context->ipiFreeze = Running;
        }

        // Return true maps to ContinueNextProcessor
        // Return false maps to ContinueProcessorReselected
        [ AccessedByRuntime("referenced from halkd.cpp") ]
        [ NoHeapAllocation ]
        static internal unsafe bool SwitchFrozenProcessor(int cpuId)
        {
            ProcessorContext* currentContext = Processor.GetCurrentProcessorContext();
            if (currentContext->cpuId == cpuId) {
                // No processor to switch to.
                return false;
            }

            ProcessorContext* context = currentContext->nextProcessorContext;
            do {
                if (context->cpuId == cpuId) {
                    currentContext->ipiFreeze &= ~FreezeActive;
                    context->ipiFreeze |= FreezeActive;
                    goto WaitForWakeupOrReselection;
                }
                context = context->nextProcessorContext;
            } while (context != activeCpuContext);

            // cpuId not found so no processor to switch to.
            return false;

          WaitForWakeupOrReselection:
            // If this processor is in the frozen state already, return
            // (it'll fall back into FreezeProcessor() loop). NB Initiating
            // processor can never be in this state.
            if (currentContext->ipiFreeze == TargetFrozen) {
                return true;
            }

            // This processor is the freeze owner, wait to be
            // reselected as the active processor.
            while ((currentContext->ipiFreeze & FreezeActive) != FreezeActive)
                ;

            return false;
        }

        [ AccessedByRuntime("referenced from halkd.cpp") ]
        [ NoHeapAllocation ]
        [ Conditional("SINGULARITY_MP") ]
        static internal unsafe void ThawAllProcessors()
        {
            // This method is only called on MP systems when the
            // number of running processors is greater than 1.
            freezeLock.Acquire();
            try {
                if (FreezeRequested == false) {
                    return;
                }

                freezeCount--;
                if (freezeCount != 0) {
                    return;
                }

                FreezeRequested = false;

                ProcessorContext* context = activeCpuContext;
                do {
                    context->ipiFreeze = TargetThaw;
                    context = context->nextProcessorContext;
                } while (context != activeCpuContext);
            }
            finally {
                freezeLock.Release();
            }
        }

        // -------------------------------------------------------------------
        // haryadi -- circular buffer for ping pong integer storage
        private const  int MPBUFFERSIZE = 5;

        private struct IntrPingPong
        {
            public SpinLock spinLock;
            public int  head;
            public int  tail;
            public int  [] buffer;
        }

        public struct ApImage
        {
            // public UIntPtr virtAddr;
            // public UIntPtr phyAddr;
            // public UIntPtr length;
            public UIntPtr entryPoint;
        }

        private struct IntrApImage
        {
            public SpinLock spinLock;
            public int  head;
            public int  tail;
            public ApImage [] buffer;
        }

        static private IntrPingPong [] intrPingPong;
        static private IntrApImage [] intrApImage;

        /////////////////////////////////////////////////////////////
        // PingPongInt routines

        [ NoHeapAllocation ]
        static internal unsafe void StartPingPongInt(int from, int to,
                                                     byte vector)
        {
            intrPingPong[to].spinLock.Acquire();
            try {
                HalDevices.SendFixedIPI(vector, from, to);
            }
            finally {
                intrPingPong[to].spinLock.Release();
            }
        }

        [ NoHeapAllocation ]
        static internal unsafe int GetIntrPingPong(int cpuId)
        {
            int head = intrPingPong[cpuId].head;
            int tail = intrPingPong[cpuId].tail;
            int max  = MPBUFFERSIZE;
            int retval;

            // empty
            if (head == tail) {
                return -1;
            }
            retval = intrPingPong[cpuId].buffer[head];
            intrPingPong[cpuId].head = (head + 1) % max;

            // DebugStub.WriteLine("HSG: Get p{0} it[{1}] = {2}",
            // __arglist(cpuId, head, retval));
            return retval;
        }

        [ NoHeapAllocation ]
        static internal unsafe bool PutIntrPingPong(int cpuId,
                                                             int task)
        {
            int tail = intrPingPong[cpuId].tail;
            int head = intrPingPong[cpuId].head;
            int max  = MPBUFFERSIZE;

            // full
            if ((tail == max - 1 && head == 0) ||
                (tail + 1 == head)) {
                return false;
            }
            intrPingPong[cpuId].buffer[tail] = task;
            intrPingPong[cpuId].tail = (tail + 1) % max;

            // DebugStub.WriteLine("HSG: Put p{0} it[{1}] = {2}",
            // __arglist(cpuId, tail, task));
            return true;
        }

        /////////////////////////////////////////////////////////////
        // ApImage routines

        [ NoHeapAllocation ]
        static internal void SendApImage(int from, int to)
        {
            byte vector = (byte)EVectors.ApImage;
            intrApImage[to].spinLock.Acquire();
            try {
                HalDevices.SendFixedIPI(vector, from, to);
            }
            finally {
                intrApImage[to].spinLock.Release();
            }
        }

        [ NoHeapAllocation ]
        static internal bool GetIntrApImage(int cpuId,
                                            out ApImage apImage)
        {
            int head = intrApImage[cpuId].head;
            int tail = intrApImage[cpuId].tail;
            int max  = MPBUFFERSIZE;

            // empty
            if (head == tail) {
                apImage.entryPoint = 0;
                return false;
            }
            apImage = intrApImage[cpuId].buffer[head];
            intrApImage[cpuId].head = (head + 1) % max;

            return true;
        }


        [ NoHeapAllocation ]
        static internal bool PutIntrApImage(int cpuId,
                                            UIntPtr entryPoint)
        {
            int tail = intrApImage[cpuId].tail;
            int head = intrApImage[cpuId].head;
            int max  = MPBUFFERSIZE;

            // full
            if ((tail == max - 1 && head == 0) ||
                (tail + 1 == head)) {
                return false;
            }
            intrApImage[cpuId].buffer[tail].entryPoint = entryPoint;
            intrApImage[cpuId].tail = (tail + 1) % max;
            return true;
        }


        /////////////////////////////////////////////////////////////
        // haryadi: AbiCall structures

        // *************** DEPRECATED if MpCall is working ***************

        // Note: AbiCall buffers should not be treated as queue.
        // When BSP receives IPI and the corresponding AbiCall buffer, the
        // buffer should not be released (unlike queue). The BSP will call
        // the actual ABI and store the return value in the same buffer.
        // The AP will obtain the return value from the buffer and then release
        // the buffer.
        public struct AbiCall
        {
            // We need to know the 'position', so
            // that when the abi is completed we know
            // where we must put the value
            public int  position;
            public int  retVal;
            public int  argVal;

            public bool validRetVal;
            public bool valid;
            public bool served;
        }

        private struct IntrAbiCall
        {
            public SpinLock spinLock;
            public int  head;
            public int  tail;
            public AbiCall [] buffer;
        }

        static private IntrAbiCall [] intrAbiCall;

        /*
        /////////////////////////////////////////////////////////////
        // AbiCall routines

        // PutAbiCall: the caller passes the ABI arguments through
        // the "AbiCall". The rest of the function just store the
        // argVal and initialize the slot
        [ NoHeapAllocation ]
        static internal int PutAbiCall(int cpuId,
                                       AbiCall abiCall)
        {
            int pos = -1;
            intrAbiCall[cpuId].spinLock.Acquire();
            try {
                // Loop to find free abi call slot. This is slow.  In
                // the future we should have a helper queue that
                // points to free slots, hence avoid traversing.
                for (int i = 0; i < intrAbiCall[cpuId].buffer.Length; i++) {
                    if (!intrAbiCall[cpuId].buffer[i].valid) {

                        // initialize fields and copy the abi arguments
                        intrAbiCall[cpuId].buffer[i].valid = true;
                        intrAbiCall[cpuId].buffer[i].served = false;
                        intrAbiCall[cpuId].buffer[i].validRetVal = false;
                        intrAbiCall[cpuId].buffer[i].retVal = 0;
                        intrAbiCall[cpuId].buffer[i].argVal = abiCall.argVal;

                        // the caller need to know on which abi call it
                        // should wait, so we copy back for the caller
                        abiCall = intrAbiCall[cpuId].buffer[i];
                        pos = i;
                        break;
                    }
                }
            }
            finally {
                intrAbiCall[cpuId].spinLock.Release();
            }
            return pos;
        }

        // SendAbiCall: Send notice to the receiver processor that
        // there is abi call that needs to be served
        [ NoHeapAllocation ]
        static internal void SendAbiCall(int from, int to)
        {
            byte vector = (byte)EVectors.AbiCall;
            intrAbiCall[to].spinLock.Acquire();
            try {
                HalDevices.SendFixedIPI(vector, from, to);
            }
            finally {
                intrAbiCall[to].spinLock.Release();
            }
        }

        // WaitAbiCall: The caller should wait until the retval is
        // ready. Currently we just loop. In the future, busy loop is
        // not necessary when scheduler comes into play.
        [ NoHeapAllocation ]
        static internal void WaitAbiCall(int cpuId, int pos,
                                         out AbiCall abiCall)
        {
            // wait for validRetVal;
            while (!intrAbiCall[cpuId].buffer[pos].validRetVal) {
                // spin loop until ready
            }

            // if reach here we have a validRetVal, copy back to caller
            intrAbiCall[cpuId].spinLock.Acquire();
            try {
                abiCall = intrAbiCall[cpuId].buffer[pos];
            }
            finally {
                intrAbiCall[cpuId].spinLock.Release();
            }
        }

        // ReleaseAbiCall: release the abi slot
        [ NoHeapAllocation ]
        static internal void ReleaseAbiCall(int cpuId, int pos)
        {
            intrAbiCall[cpuId].spinLock.Acquire();
            try {
                if (pos < intrAbiCall[cpuId].buffer.Length) {
                    intrAbiCall[cpuId].buffer[pos].valid = false;
                }
            }
            finally {
                intrAbiCall[cpuId].spinLock.Release();
            }
        }

        // GetAbiCall: We should find valid but !served abi calls. The
        // caller should loop until all abi calls are served.
        [ NoHeapAllocation ]
        static internal bool GetAbiCall(int cpuId, out AbiCall abiCall)
        {
            bool isUnservedAbi = false;
            intrAbiCall[cpuId].spinLock.Acquire();
            try {
                abiCall = intrAbiCall[cpuId].buffer[0]; // init

                for (int i = 0; i < intrAbiCall[cpuId].buffer.Length; i++) {
                    if (intrAbiCall[cpuId].buffer[i].valid &&
                        !intrAbiCall[cpuId].buffer[i].served) {

                        intrAbiCall[cpuId].buffer[i].served = true;
                        abiCall = intrAbiCall[cpuId].buffer[i];
                        isUnservedAbi = true;
                        break;
                    }
                }
            }
            finally {
                intrAbiCall[cpuId].spinLock.Release();
            }
            return isUnservedAbi;
        }

        // ReturnAbiCall: The abi call has finished, copy the return
        // value and set validRetVal to true
        [ NoHeapAllocation ]
        static internal void ReturnAbiCall(int cpuId, AbiCall abiCall)
        {
            intrAbiCall[cpuId].spinLock.Acquire();
            try {
                int pos = abiCall.position;
                if (abiCall.position < intrAbiCall[cpuId].buffer.Length) {
                    intrAbiCall[cpuId].buffer[pos] = abiCall;
                    intrAbiCall[cpuId].buffer[pos].validRetVal = true;
                }
            }
            finally {
                intrAbiCall[cpuId].spinLock.Release();
            }
        }
        */


        /////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////
        // haryadi: MpCall structures

        // Note: MpCall buffers should not be treated as queue.
        // When BSP receives IPI and the corresponding MpCall buffer, the
        // buffer should not be released (unlike queue). The BSP will call
        // the actual ABI and store the return value in the same buffer.
        // The AP will obtain the return value from the buffer and then release
        // the buffer.
        public class MpCall
        {
            public MpCall() {}

            // We need to know the 'position', so
            // that when the abi is completed we know
            // where we must put the value
            public int abiNum;
            public int position;

            // public int argVal;
            // public int retVal;

            public const int MAX_RET_SIZE = 20;
            public const int MAX_ARG_SIZE = 100;
            public byte [] retBuffer = new byte [MAX_RET_SIZE];
            public byte [] argBuffer = new byte [MAX_ARG_SIZE];

            public bool validRetVal;
            public bool valid;
            public bool served;
        }

        private struct IntrMpCall
        {
            public SpinLock spinLock;
            public int  head;
            public int  tail;
            public MpCall [] buffer;
        }

        static private IntrMpCall [] intrMpCall;


        /////////////////////////////////////////////////////////////
        // MpCall routines


        [ NoHeapAllocation ]
        static internal MpCall ReserveMpCall(int cpuId)
        {
            intrMpCall[cpuId].spinLock.Acquire();
            try {
                // Loop to find free abi call slot. This is slow.  In
                // the future we should have a helper queue that
                // points to free slots, hence avoid traversing.
                for (int i = 0; i < intrMpCall[cpuId].buffer.Length; i++) {
                    if (!intrMpCall[cpuId].buffer[i].valid) {

                        intrMpCall[cpuId].buffer[i].valid = true;
                        intrMpCall[cpuId].buffer[i].served = false;
                        intrMpCall[cpuId].buffer[i].validRetVal = false;

                        // the caller need to know on which abi call it
                        // should wait, so we copy back for the caller
                        return intrMpCall[cpuId].buffer[i];
                    }
                }
            }
            finally {
                intrMpCall[cpuId].spinLock.Release();
            }
            return null;
        }


        // SendMpCall: Send notice to the receiver processor that
        // there is abi call that needs to be served
        [ NoHeapAllocation ]
        static internal void SendMpCall(int from, int to)
        {
            byte vector = (byte)EVectors.AbiCall;
            intrMpCall[to].spinLock.Acquire();
            try {
                HalDevices.SendFixedIPI(vector, from, to);
            }
            finally {
                intrMpCall[to].spinLock.Release();
            }
        }

        // WaitMpCall: The caller should wait until the retval is
        // ready. Currently we just loop. In the future, busy loop is
        // not necessary when scheduler comes into play.
        [ NoHeapAllocation ]
        static internal void WaitMpCall(int cpuId, int pos)
        {
            // wait for validRetVal;
            while (!intrMpCall[cpuId].buffer[pos].validRetVal) {
                // spin loop until ready
            }
        }

        // ReleaseMpCall: release the abi slot
        [ NoHeapAllocation ]
        static internal void ReleaseMpCall(int cpuId, int pos)
        {
            intrMpCall[cpuId].spinLock.Acquire();
            try {
                if (pos < intrMpCall[cpuId].buffer.Length) {
                    intrMpCall[cpuId].buffer[pos].valid = false;
                }
            }
            finally {
                intrMpCall[cpuId].spinLock.Release();
            }
        }

        // GetMpCall: We should find valid but !served abi calls. The
        // caller should loop until all abi calls are served.
        [ NoHeapAllocation ]
        static internal MpCall GetMpCall(int cpuId)
        {
            intrMpCall[cpuId].spinLock.Acquire();
            try {
                for (int i = 0; i < intrMpCall[cpuId].buffer.Length; i++) {
                    if (intrMpCall[cpuId].buffer[i].valid &&
                        !intrMpCall[cpuId].buffer[i].served) {

                        intrMpCall[cpuId].buffer[i].served = true;
                        return intrMpCall[cpuId].buffer[i];
                    }
                }
            }
            finally {
                intrMpCall[cpuId].spinLock.Release();
            }
            return null;
        }

        // ReturnMpCall: The abi call has finished, copy the return
        // value and set validRetVal to true
        [ NoHeapAllocation ]
        static internal void ReturnMpCall(int cpuId, int pos)
        {
            intrMpCall[cpuId].spinLock.Acquire();
            try {
                if (pos < intrMpCall[cpuId].buffer.Length) {
                    intrMpCall[cpuId].buffer[pos].validRetVal = true;
                }
            }
            finally {
                intrMpCall[cpuId].spinLock.Release();
            }
        }


        /////////////////////////////////////////////////////////////
        // Initialization function

        internal static void Initialize()
        {
            freezeLock = new SpinLock();
            InitializePingPong();
            InitializeApImage();
            InitializeAbiCall();
            InitializeMpCall();
        }

        // initialize circular buffer for all processors
        internal static void InitializePingPong()
        {
            intrPingPong =
                new IntrPingPong [MpBootInfo.MAX_CPU];
            for (int i = 0; i < intrPingPong.Length; i++) {
                intrPingPong[i].head = 0;
                intrPingPong[i].tail = 0;
                intrPingPong[i].buffer = new int [MPBUFFERSIZE];
                intrPingPong[i].spinLock = new SpinLock();
            }
        }

        // initialize circular buffer for all processors
        internal static void InitializeApImage()
        {
            intrApImage =
                new IntrApImage [MpBootInfo.MAX_CPU];
            for (int i = 0; i < intrApImage.Length; i++) {
                intrApImage[i].head = 0;
                intrApImage[i].tail = 0;
                intrApImage[i].buffer = new ApImage [MPBUFFERSIZE];
                intrApImage[i].spinLock = new SpinLock();
            }
        }

        // initialize circular buffer for abi calls
        internal static void InitializeAbiCall()
        {
            intrAbiCall =
                new IntrAbiCall [MpBootInfo.MAX_CPU];
            for (int i = 0; i < intrAbiCall.Length; i++) {
                intrAbiCall[i].head = 0;
                intrAbiCall[i].tail = 0;
                intrAbiCall[i].buffer = new AbiCall [MPBUFFERSIZE];
                for (int j = 0; j < intrAbiCall[i].buffer.Length; j++) {
                    intrAbiCall[i].buffer[j].position = j;
                    intrAbiCall[i].buffer[j].valid = false;
                }
                intrAbiCall[i].spinLock = new SpinLock();
            }
        }


        // initialize circular buffer for abi calls
        internal static void InitializeMpCall()
        {
            intrMpCall =
                new IntrMpCall [MpBootInfo.MAX_CPU];
            for (int i = 0; i < intrMpCall.Length; i++) {
                intrMpCall[i].head = 0;
                intrMpCall[i].tail = 0;
                intrMpCall[i].buffer = new MpCall [MPBUFFERSIZE];
                for (int j = 0; j < intrMpCall[i].buffer.Length; j++) {
                    intrMpCall[i].buffer[j] = new MpCall();
                    intrMpCall[i].buffer[j].position = j;
                    intrMpCall[i].buffer[j].valid = false;
                }
                intrMpCall[i].spinLock = new SpinLock();
            }
        }


        // Test ..
        internal static void Test()
        {

        }
    }
}
