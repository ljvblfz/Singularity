////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   HostName.cs
//
//  Note:   Simple Singularity test program.
//
using System;
using System.Diagnostics;
using System.Net.IP;

using Microsoft.Singularity;
using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Directory;
using NetStack.Contracts;
using NetStack.Channels.Public;

using Microsoft.Contracts;
using Microsoft.SingSharp.Reflection;
using Microsoft.Singularity.Applications;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Configuration;
[assembly: Transform(typeof(ApplicationResourceTransform))]

namespace Microsoft.Singularity.Applications.Network 
{
    [ConsoleCategory(HelpMessage="Delete network routing information", DefaultAction=true)]
    internal class Parameters {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        [Endpoint]
        public readonly TRef<IPContract.Imp:Start> ipRef;

        [StringParameter( "n", Default=null,  HelpMessage="domain name to query")]
        internal string name;

        reflective internal Parameters();

        internal int AppMain() {
            HostName.AppMain(this);
            return 0; 
        }
    }
    /// <summary>
    /// Class for configuring Host Name.
    /// </summary>
    public class HostName
    {
        internal static int AppMain(Parameters! config)
        {

            IPContract.Imp ipConn = (config.ipRef).Acquire(); 
            if (ipConn == null)
            {
                Console.WriteLine("Could not initialize IP endpoint.");
                return 1;
            }
            ipConn.RecvReady();
            
            try
            {
                if (config.name == null)
                {
                    char[]! in ExHeap repHost, repDomain;
                    ipConn.SendGetHostName();
                    ipConn.RecvHostName(out repHost);

                    ipConn.SendGetDomainName();
                    ipConn.RecvDomainName(out repDomain);

                    Console.WriteLine("{0}.{1}", Bitter.ToString(repHost), Bitter.ToString(repDomain));
                    delete repHost;
                    delete repDomain;
                    return 0; // success
                }
                else
                {
                    ipConn.SendSetHostName(Bitter.FromString2(config.name));

                    switch receive
                    {
                        case ipConn.Err() :
                            Console.WriteLine("Failure setting host name \"{0}\"", config.name);
                            return 1; // failure;

                        case ipConn.OK() :
                            Console.WriteLine("Success setting host name");
                            break;

                        case ipConn.ChannelClosed() :
                            Console.WriteLine("Failure setting host name \"{0}\" (channel closed)", config.name);
                            return 1; // failure;
                    }
                }
            }
            finally
            {
                delete ipConn;
            }

            return 0; // success
        }
    } // end class HostName
}
