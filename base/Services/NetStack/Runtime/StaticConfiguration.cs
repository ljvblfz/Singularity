///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Netstack / Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   StaticConfiguration.cs
//

using System.Collections;

#if SINGULARITY
using Drivers.Net;
using Microsoft.Singularity.Io;
#endif

namespace NetStack.Runtime
{
    /// <summary> A class that Initializes, Starts, and Stops the default
    /// set of IP modules.
    /// </summary>
    public class StaticConfiguration
    {
        static ArrayList modules;
        static bool running = false;
        static bool initialized = false;

        public static void Initialize()
        {
            Core.Log("StaticConfiguration.Initialize() {0}", initialized);
            modules = new ArrayList();
            modules.Add(Core.Instance());
            modules.Add(new IPModule());
            modules.Add(new ArpModule());
            modules.Add(new IcmpModule());
            modules.Add(new TcpModule());
            modules.Add(new UdpModule());

            foreach (INetModule! module in modules)
            {
                bool success = module.Initialize(null);
                Core.Log("Initializing {0}...{1}",
                         module.ModuleName, success ? "okay" : "fail");
            }
            initialized = true;
        }

        public static void Start()
        {
            if (running)
            {
                return;
            }
            running = true;

            Core.Instance().Activate();
            Core.Instance().StartModule();
        }

        public static void Stop()
        {
            if (!running)
            {
                return;
            }
            running = false;

            modules.Reverse();
            foreach (INetModule! module in modules)
            {
                Core.Log("Stopping {0}...", module.ModuleName);
                bool success = module.StopModule();
                Core.Log("{0}\n", success ? "okay" : "fail");
            }
            Core.Log("Stopped all.\n");
        }
    }
}
