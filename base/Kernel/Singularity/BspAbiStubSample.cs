///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

///////////////////////////////////////////////////////////////////
// This file will be automatically generated
// and overwritten again and again ...

using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;

using Microsoft.Singularity.Hal;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Memory;
using Microsoft.Singularity.Scheduling;
using Microsoft.Singularity.X86;

// haryadi: for Abi Call
using Microsoft.Singularity.V1.Services;

namespace Microsoft.Singularity
{
    [CLSCompliant(false)]
    public class BspAbiStub
    {

        [NoHeapAllocation]
        public static unsafe void ProcessMpCall(int cpu, MpExecution.MpCall mpCall)
        {
            switch (mpCall.abiNum) {
              case 86: CallHelloProcessABI(cpu, mpCall); break;
              default:
                DebugStub.WriteLine("HSG: **** Unknown abi call number {0}",
                                    __arglist(mpCall.abiNum));
                break;
            }
        }

        [NoHeapAllocation]
        private static unsafe void CallHelloProcessABI(int cpu, MpExecution.MpCall mpCall)
        {
            int p0, p1;
            int retval;

            // 2) get args from
            // argVal = mpCall.argVal;
            fixed (byte *baseArg = & mpCall.argBuffer[0] ) {
                Buffer.MoveMemory( (byte*) & p0 , baseArg+0, 4);
                Buffer.MoveMemory( (byte*) & p1 , baseArg+4, 4);
            }

            // 3) call the actual abi, save return values
            retval = ProcessService.HelloProcessABI(p0, p1);

            // 4) copy retval to abi
            // mpCall.retVal = retVal;
            fixed (byte *baseRet = & mpCall.retBuffer[0] ) {
                Buffer.MoveMemory( baseRet , (byte*) & retval , 4);
            }

            // 4) copy arguments to abi
            fixed (byte *baseArg = & mpCall.argBuffer[0] ) {
                Buffer.MoveMemory( baseArg+0 , (byte*) & p0 , 4);
                Buffer.MoveMemory( baseArg+4 , (byte*) & p1 , 4);
            }

            // 5) store abi back to buffer
            MpExecution.ReturnMpCall(cpu, mpCall.position);
        }
    }
}
