///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   PseudoBus.cs
//
//  Note:
//

using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Io;

using System;
using System.Collections;

namespace Microsoft.Singularity.Drivers
{
    /// <summary>
    /// Bus for pseudo-devices.  A pseudo-device is typically a
    /// layer in I/O stack that may or may not be associated
    /// with one or more physical devices.
    /// </summary>
    public class PseudoBus : IBusDevice
    {
        public const string Name = "/pseudo0";

        private static PseudoBus instance = null;

        private SortedList! pseudoDevices;

        private PseudoBus()
        {
            pseudoDevices = new SortedList();
        }

        internal static PseudoBus! Instance
        {
            get {
                if (instance == null)
                    instance = new PseudoBus();
                return instance;
            }
        }

        internal void RegisterPseudoDevice(string! name, IoConfig config)
        {
            pseudoDevices.Add(name, config);
        }

        public static IDevice! DeviceCreate(IoConfig! configobj)
        {
            return Instance;
        }

        SortedList IBusDevice.Enumerate()
        {
            return pseudoDevices;
        }

        void IDevice.Initialize()
        {
            DebugStub.Print("Initializing PseudoBusDevice.\n");
        }

        void IDevice.Finalize()
        {
        }
    }

    public class PseudoConfig : IoConfig
    {
        static private int count = 0;

        public PseudoConfig(string className)
        {
            string[] ids = { String.Format("/pseudo0{0}/{1}", className, count++) };
            this.Ids = ids;
        }

        public override string! ToString()
        {
            assert Ids != null;
            assert Ids.Length > 0;
            string id = Ids[0];
            assert id != null;
            return id;
        }

        public override string ToPrint()
        {
            return ToString();
        }
    }
}
