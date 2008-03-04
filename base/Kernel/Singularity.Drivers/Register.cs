////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Register.cs
//
//  Note:   PnP Device Type Registration and Base Initialization.
//

using Microsoft.Singularity.Drivers.IDE;
using Microsoft.Singularity.Drivers.Pci;
using Microsoft.Singularity.Io;

namespace Microsoft.Singularity.Drivers
{
    public sealed class Devices
    {
        public static void RegisterPnpResources()
        {
            // : /pnp/PNP0700 : Floppy Controller   : PC Standard
            // : /pnp/PNP0C01 : RAM                 : System Board
            // : /pnp/PNP0A03 : PCI                 : PCI Bus
            // : /pnp/PNP0501 : Generic Serial      : 16550A COM Port
            // : /pnp/PNP0501 : Generic Serial      : 16550A COM Port
            // : /pnp/PNP0400 : AT Parallel         : LPT Port
            // : /pnp/PNP0000 : ISA 8259 PIC        : AT Interrupt Controller
            // : /pnp/PNP0200 : ISA 8237 DMA        : AT DMA Controller
            // : /pnp/PNP0100 : ISA 8254 Timer      : AT Timer
            // : /pnp/PNP0B00 : ISA RTC Controller  : AT RTC
            // : /pnp/PNP0800 : Other               : ???
            // : /pnp/PNP0C02 : Other               : PnP Event Notification
            // : /pnp/PNP0C02 : Other               : PnP Event Notification
            // : /pnp/PNP0C04 : Other               : Math Coprocessor
            // : /pnp/PNP0303 : Keyboard controller : 101/102 Keyboard
            // : /pnp/PNP0F13 : Mouse Controller    : Logitech PS/2 Mouse

            PnpBios bios = new PnpBios(Resources.GetPnpBiosInfo());

            // in order for IoSystem accounting to work, we need to explicitly
            // tell it what the IoConfig of the root device is:
            IoSystem.AddRootDevice("/pnp0", bios, bios.ReportConfig());
        }

        // Now that we use metadata, this only registers drivers that do not run
        // in separate processes.  All external processes are registered through
        // the IoSystem.Initialize() code.
        public static void RegisterInternalDrivers()
        {
            // PCI Bus
            IoSystem.RegisterKernelDriver(
                typeof(PciBusResources),
                new IoDeviceCreate(PciBusResources.DeviceCreate));

            // Legacy PC IDE bus
            IoSystem.RegisterKernelDriver(
                typeof(LegacyIdeBus),
                new IoDeviceCreate(LegacyIdeBus.DeviceCreate));

            // nForce4 IDE bus
            IoSystem.RegisterKernelDriver(
                typeof(NvIdeBus),
                new IoDeviceCreate(NvIdeBus.DeviceCreate));
        }
    }
}
