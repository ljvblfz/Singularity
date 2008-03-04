///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Register.cs
//
//  Note:   PnP Device Type Registration and Base Initialization.
//

using Microsoft.Singularity;
using Microsoft.Singularity.Io;
using System;
using System.Threading;
using System.Diagnostics;

namespace Microsoft.Singularity.Intel8255x
{
    public class Factory {
        public static IDevice! DeviceCreate(IoConfig! config, String! instanceName)
        {
            return new Driver((PciDeviceConfig)config);
        }
    }

    internal enum SCBStatAck : byte {
        CX  = 1 << 7,
        FR  = 1 << 6,
        CNA = 1 << 5,
        RNR = 1 << 4,
        MDI = 1 << 3,
        SWI = 1 << 2,
        ER  = 1 << 1,
        FCP = 1 << 0
    }

    internal enum SCBStatus : byte {
        RUS = 0x3 << 6,
        CUS = 0xf << 2
    }

    internal enum SCBCommand : ushort {
        CX  = 1 << 15,
        FR  = 1 << 14,
        CNA = 1 << 13,
        RNR = 1 << 12,
        ER  = 1 << 11,
        FCP = 1 << 10,
        SI  = 1 << 9,
        M   = 1 << 8,
        CUC = 0xf << 4,
        RUC = 0x7
    }

    public class Driver : IDevice {
        public static IDevice! DeviceCreate(IoConfig! config)
        {
            return new Driver((PciDeviceConfig)config);
        }

        private PciDeviceConfig config;

        private IoMemoryRange eepromBase;
        private IoMemory eepromMemory;

        private IReadWriteRegister16    scbCommand;
        private IReadOnlyRegister8      scbStatus;
        private IReadWriteRegister8     scbStatAck;

        private IoIrq                   irq;
        private Thread                  irqWorker;
        private bool                    irqWorkerStop = false;
        private Thread                  irqMaker;
        private bool                    irqMakerStop = false;

        internal Driver(PciDeviceConfig config)
        {
            this.config = config;
        }

        private void DebugPrint(string format, __arglist)
        {
            DebugStub.Print(format, __arglist);
        }

        private void InitializeMemoryRegisters()
        {
            IoMemoryRange range = (IoMemoryRange)config.Ranges[0];
            scbStatus  = ReadOnlyMemoryRegister8.Create(range, 0);
            scbStatAck = ReadWriteMemoryRegister8.Create(range, 1);
            scbCommand = ReadWriteMemoryRegister16.Create(range, 2);
        }

        private void InitializeIoPortRegisters()
        {
            IoPortRange range = (IoPortRange)config.Ranges[1];
            scbStatus  = ReadOnlyPortRegister8.Create(range, 0);
            scbStatAck = ReadWritePortRegister8.Create(range, 1);
            scbCommand = ReadWritePortRegister16.Create(range, 2);
        }

        public void Initialize()
        {
            if (config.MemorySpaceEnabled) {
                InitializeMemoryRegisters();
            } else {
                Debug.Assert(config.IoSpaceEnabled == true);
                InitializeIoPortRegisters();
            }

            eepromBase = (IoMemoryRange)config.Ranges[2];
            eepromMemory = eepromBase.MemoryAtOffset(0, 65536, Access.Read);

            DebugPrint("RevisionId {0} SCB Status 0x{1:x2} " +
                       "Command 0x{2:x4}\n",
                       __arglist(config.RevisionId,
                                 (byte)GetScbStatus(), (ushort)GetScbCommand()));

            irq = config.GetIrq().IrqAtOffset(0);
            irq.RegisterInterrupt();
            irqWorker = new Thread(new ThreadStart(this.IrqWorkerMain),
                                   new Scheduler.Activity()
                                   );
            irqWorker.Start();
            irqWorker.Name = String.Format("i8255x I/O {0} ({0})",
                                           config.ToString());

            irqMaker = new Thread(new ThreadStart(this.IrqMakerMain),
                                  new Scheduler.Activity()
                                  );
            irqMaker.Start();
        }

        public void Finalize()
        {
            irqWorkerStop = true;
            irqMakerStop = true;
            irq.ReleaseInterrupt();

#if CAN_JOIN
            if (irqMaker != null) {
                irqMaker.Join();
                irqMaker = null;
            }
            if (irqWorker != null) {
                irqWorker.Join();
                irqWorker = null;
            }
#endif
        }

        private SCBStatus GetScbStatus()
        {
            return (SCBStatus)scbStatus.Read();
        }

        private SCBStatAck GetScbStatAck()
        {
            return (SCBStatAck)scbStatAck.Read();
        }

        private void SetScbStatAck(SCBStatAck value)
        {
            scbStatAck.Write((byte)value);
        }

        private SCBCommand GetScbCommand()
        {
            return (SCBCommand)scbCommand.Read();
        }

        private void SetScbCommand(SCBCommand value)
        {
            scbCommand.Write((ushort)value);
        }

        private void IrqMakerMain()
        {
            for (;;) {
                Thread.Sleep(1000);
                // Trigger Software Interrupt
                scbCommand.Write((ushort)SCBCommand.SI);
            }
        }

        private void IrqWorkerMain()
        {
            Tracing.Log(Tracing.Audit, "Start of worker thread.");
            uint iters = 0;
            for (;;) {
                irq.WaitForInterrupt();
                DebugPrint("+ IrqWorkerMain iteration {0}\n", __arglist(iters));

                SCBStatAck s = GetScbStatAck();
                DebugPrint("Clearing Interupt : StatAck 0x{0:x4}\n",
                           __arglist((ushort)s));
                SetScbStatAck(s); // Write bits back to clear
                scbCommand.Write(0);
                irq.AckInterrupt();

                DebugPrint("- IrqWorkerMain iteration {0}\n", __arglist(iters));
            }
        }
    }
}
