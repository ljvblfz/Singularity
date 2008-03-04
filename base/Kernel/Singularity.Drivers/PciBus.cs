////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   PciBus.cs
//
//  Note:
//

using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Configuration;

using System;
using System.Collections;
using System.Text;
using System.Configuration.Assemblies;
using System.Runtime.Remoting;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Microsoft.Singularity.Drivers
{
    // create the resource object for CTR to fill in
    [DriverCategory]
    [Signature("/pnp/PNP0A03")]
    [EnumeratesDevice("pci/...")]
    internal class PciBusResources : DriverCategoryDeclaration
    {
        [IoFixedPortRange(Base = 0x0cf8, Length = 0x08)]
        IoPortRange configPort;

        // Provide to unify creation.
        public static IDevice! DeviceCreate(IoConfig! config, String! instanceName)
        {
            Tracing.Log(Tracing.Debug, "Creating PCI Bus");

            return new PciBus(PciEnumerator.GetEnumerator(config));
        }
    }

    [CLSCompliant(false)]
    public class PciBus : IBusDevice
    {
        private PciEnumerator! enumerator;

        static ushort IdentifierFromUnits(uint nBus, uint nDevice, uint nFunction)
        {
            return (ushort)(nFunction | (nDevice << 3) | (nBus << 8));
        }

        public PciBus(PciEnumerator! enumerator)
        {
            this.enumerator = enumerator;
        }

        public void Initialize()
        {
            Tracing.Log(Tracing.Debug, "Initializing PciBus.");
        }

        public void Finalize()
        {
        }

        public SortedList Enumerate()
        {
            return enumerator.Enumerate();
        }

        public void Display()
        {
            foreach (PciConfig! config in enumerator.Enumerate()) {
                config.Print();
            }
        }
    }


    [CLSCompliant(false)]
    public class PciEnumerator
    {
        public const uint MAX_BUSES         = 256;
        public const uint MAX_DEVICES       = 32;
        public const uint MAX_FUNCTIONS     = 8;
        public const uint MAX_IDENTIFIER    = (MAX_BUSES * MAX_DEVICES * MAX_FUNCTIONS) - 1;

        private const ushort PCI_ADDRESS_PORT    = 0xcf8;
        private const ushort PCI_DATA_PORT       = 0xcfc;

        private uint numberOfBuses = 0;
        private IoPort! addressPort;
        private IoPort! dataPort;

        static ushort IdentifierFromUnits(uint nBus, uint nDevice, uint nFunction)
        {
            return (ushort)(nFunction | (nDevice << 3) | (nBus << 8));
        }

        public static PciEnumerator GetEnumerator(IoConfig config)
        {
            uint buses = Resources.GetPciNumberOfBuses();

#if DONT_USE_PNP_FOR_PCI
            if (config != null &&
                config.Ranges.Length >= 1 && config.Ranges[0] != null &&
                config.Ranges[0] is IoPortRange) {
                IoPortRange ports = (IoPortRange)config.Ranges[0];

                IoPort addrPort = ports.PortAtOffset(0, 4, Access.Write);
                IoPort dataPort = ports.PortAtOffset(4, 4, Access.ReadWrite);
                return new PciEnumerator(addrPort, dataPort, buses);
            }
            else {
#endif
                IoPortRange dev = new IoPortRange(PCI_ADDRESS_PORT, 8, Access.ReadWrite);

                return new PciEnumerator(dev.PortAtOffset(0, 4, Access.Write),
                                         dev.PortAtOffset(4, 4, Access.ReadWrite),
                                         buses);
#if DONT_USE_PNP_FOR_PCI
            }
#endif

        }

        private PciEnumerator(IoPort! addressPort, IoPort! dataPort, uint buses)
        {
            this.addressPort = addressPort;
            this.dataPort = dataPort;
            this.numberOfBuses = buses;
        }

        private uint Read32(uint identifier, uint offset)
        {
            if (identifier < 0 || identifier > MAX_IDENTIFIER) {
                throw new OverflowException("BAD_IDENTIFIER");
            }
            if ((offset & 0x3) != 0) {
                throw new Exception("BAD_OFFSET");
            }

            uint config = (((uint)offset & 0xfc) |
                           ((uint)identifier << 8) |
                           ((uint)1 << 31));

            addressPort.Write32(config);
            return dataPort.Read32();
        }

        private void Write32(uint identifier, uint offset, uint value)
        {
            if (identifier < 0 || identifier > MAX_IDENTIFIER) {
                throw new OverflowException("BAD_IDENTIFIER");
            }
            if ((offset & 0x3) != 0) {
                throw new Exception("BAD_OFFSET");
            }

            uint config = (((uint)offset & 0xfc) |
                           ((uint)identifier << 8) |
                           ((uint)1 << 31));

            addressPort.Write32(config);
            dataPort.Write32(value);
        }

        private PciPort PortForIdentifier(uint identifier)
        {
            if (identifier < 0 || identifier > MAX_IDENTIFIER) {
                throw new OverflowException("BAD_IDENTIFIER");
            }
            return new PciPort(addressPort, dataPort, (ushort)identifier);
        }

        public SortedList! Enumerate()
        {
            Tracing.Log(Tracing.Debug, "PCI Bus Enumerate");

            SortedList found = new SortedList();
            unchecked {
                Tracing.Log(Tracing.Debug, "    buses: {0}",
                            (UIntPtr)(uint)numberOfBuses);
            }

            for (uint bus = 0; bus < numberOfBuses; bus++) {
                for (uint device = 0; device < MAX_DEVICES; device++) {
                    PciConfig config;
                    uint identifier = IdentifierFromUnits(bus, device, 0);
                    uint u = Read32(identifier, 0);

                    if (u == ~0u || u == 0) {
                        continue;
                    }

                    u = Read32(identifier, 0x0c);

                    uint max_functions = (u & PciConfig.PCI_MULTIFUNCTION) == 0
                        ? (uint)1 : MAX_FUNCTIONS;
#if DEBUG_PCI_BUS
                    Tracing.Log(Tracing.Debug, "    {0}.{1}:C => max={2} [{3:x8}]",
                                bus, device, max_functions, u);
#endif

                    for (uint function = 0; function < max_functions; function++) {
                        identifier = IdentifierFromUnits(bus, device, function);
                        u = Read32(identifier, 0);
#if DEBUG_PCI_BUS
                        Tracing.Log(Tracing.Debug, "    {0}.{1}.{2}:0 => {3:x8}",
                                    bus, device, function, u);
#endif

                        if (u == ~0u || u == 0) {
                            continue;
                        }

                        u = Read32(identifier, 0x0c);
#if DEBUG_PCI_BUS
                        Tracing.Log(Tracing.Debug, "    {0}.{1}.{2}:C => {3:x8}",
                                    bus, device, function, u);
#endif

                        switch (u & PciConfig.PCI_TYPE_MASK) {
                            case PciConfig.PCI_DEVICE_TYPE:
                                config = new PciDeviceConfig(PortForIdentifier(identifier));
                                break;
                            case PciConfig.PCI_BRIDGE_TYPE:
                                config = new PciBridgeConfig(PortForIdentifier(identifier));
                                break;
                            case PciConfig.PCI_CARDBUS_TYPE:
                                config = new PciCardbusConfig(PortForIdentifier(identifier));
                                break;
                            default:
                                config = null;
                                break;
                        }

                        if (config != null) {
                            found.Add(String.Format("/bus{0,4:x4}/dev{1,4:x4}/func{2,4:x4}",
                                                    bus, device, function),
                                      config);

                        }
                    }
                }
            }

            return found;
        }
    }
}
