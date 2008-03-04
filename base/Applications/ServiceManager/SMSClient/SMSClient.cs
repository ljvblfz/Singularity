///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Applications\ServiceManager\SMSClient\SMSClient.sg
//
//  Note:   Service Manager client program
//
using System;
using Microsoft.SingSharp;
using Microsoft.SingSharp.Reflection;
using Microsoft.Singularity;
using Microsoft.Singularity.Applications;
using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Configuration;
using Microsoft.Singularity.Contracts;
using Microsoft.Singularity.Directory;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.ServiceManager;
[assembly: Transform(typeof(ApplicationResourceTransform))]

namespace Microsoft.Singularity.Applications.ServiceManager
{
    [ConsoleCategory(HelpMessage="Service management client", DefaultAction=true)]
    internal class DefaultConfig
    {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        reflective internal DefaultConfig();

        internal int AppMain()
        {
            return SMSClient.DefaultMain(this);
        }
    }

    [ConsoleCategory(Action="start", HelpMessage="Start a service")]
    internal class StartConfig
    {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        [StringParameter("service", Mandatory=true, Position=0)]
        internal string service;

        [StringParameter("type", Mandatory=false, Position=1)]
        internal string type;

        reflective internal StartConfig();

        internal int AppMain()
        {
            return SMSClient.StartService(this);
        }
    }

    [ConsoleCategory(Action="stop", HelpMessage="Stop a service")]
    internal class StopConfig
    {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        //[StringParameter("service", Mandatory=true, Position=0)]
        //internal string service;

        [LongParameter("id", Mandatory=true, Position=0)]
        internal long id;

        reflective internal StopConfig();

        internal int AppMain()
        {
            return SMSClient.StopService(this);
        }
    }

    [ConsoleCategory(Action="restart", HelpMessage="Restart a service")]
    internal class RestartConfig
    {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        [StringParameter("service", Mandatory=true, Position=0)]
        internal string service;

        reflective internal RestartConfig();

        internal int AppMain()
        {
            return SMSClient.RestartService(this);
        }
    }

    [ConsoleCategory(Action="list", HelpMessage="Show a list of services")]
    internal class ListConfig
    {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        reflective internal ListConfig();

        internal int AppMain()
        {
            return SMSClient.ListServices(this);
        }
    }

    public class SMSClient
    {
        private static void GetEndpoint(out ServiceManagementContract.Imp! ep)
        {
            ErrorCode error;
            DirectoryServiceContract.Imp    ds;
            ServiceManagementContract.Exp!  scServer;
            ServiceManagementContract.NewChannel(out ep, out scServer);

            ds = DirectoryService.NewClientEndpoint();
            SdsUtils.Bind(ServiceManagementContract.ModuleName, ds,
                          scServer, out error);
            delete ds;
        }

        internal static int StartService(StartConfig! config)
        {
            int         res = 1;
            ServiceType type = ServiceType.Unknown;
            ServiceInfo* in ExHeap          info;
            ServiceManagementContract.Imp!  ep;
            ServiceControlContract.Imp!     client;
            ServiceControlContract.Exp!     server;

            switch (config.type)
            {
                case "Resilient":
                    type = ServiceType.Resilient;
                    break;
                case "Default":
                    type = ServiceType.Default;
                    break;
                default:
                    type = ServiceType.Default;
                    break;
            }

            Console.Write("Starting service: " + config.service + " ... ");
            GetEndpoint(out ep);
            ep.RecvSuccess();

            ServiceControlContract.NewChannel(out client, out server);
            info = new[ExHeap] ServiceInfo(0, config.service, config.service,
                                           type);
            ep.SendBind(info, server);
            switch receive {
                case ep.AckBind():
                    break;
                case ep.NotFound(rejected):
                    delete rejected;
                    Console.WriteLine("Doesn't exist.");
                    goto exit;
                    break;
                case ep.PermissionDenied(rejected):
                    delete rejected;
                    Console.WriteLine("Permission denied");
                    goto exit;
                    break;
            }

            client.RecvSuccess();
            client.SendStartService();
            switch receive {
                case client.RecvAckStartService():
                    Console.WriteLine("done.");
                    res = 0;
                    break;
                case client.NakStartService(error):
                    Console.WriteLine("Error code: " + error);
                    break;
                case client.ChannelClosed():
                    Console.WriteLine("Channel is closed.");
                    break;
            }
exit:
            delete client;
            delete ep;
            return res;
        }

        internal static int StopService(StopConfig! config)
        {
            int res = 1;
            ServiceManagementContract.Imp!  ep;
            ServiceControlContract.Imp!     client;
            ServiceControlContract.Exp!     server;

            Console.Write("Stopping service: " + config.id + " ... ");
            GetEndpoint(out ep);
            ep.RecvSuccess();

            ServiceControlContract.NewChannel(out client, out server);
            ep.SendGetControl((int)config.id, server);
            switch receive {
                case ep.RecvAckGetControl():
                    break;
                case ep.NotFound(rejected):
                    delete rejected;
                    Console.WriteLine("Doesn't exist.");
                    goto exit;
                    break;
                case ep.PermissionDenied(rejected):
                    delete rejected;
                    Console.WriteLine("Permission denied");
                    goto exit;
                    break;
                case ep.TryAgain(rejected):
                    delete rejected;
                    Console.WriteLine("Busy!");
                    goto exit;
                    break;
            }

            client.RecvSuccess();
            client.SendStopService();
            switch receive {
                case client.AckStopService():
                    Console.WriteLine("done.");
                    res = 0;
                    break;
                case client.NakStopService(error):
                    Console.WriteLine("Error code: " + error);
                    break;
            }
exit:
            delete client;
            delete ep;
            return res;
        }

        internal static int RestartService(RestartConfig! config)
        {
            Console.WriteLine("-- Sorry, not implemented yet.");
            return 1;
        }

        internal static int ListServices(ListConfig! config)
        {
            bool    next = false;
            int     count = 0;
            ServiceManagementContract.Imp! ep;

            GetEndpoint(out ep);
            ep.RecvSuccess();
            ep.SendBeginEnumeration();

            Console.WriteLine();
            Console.WriteLine("PID Task Name");
            Console.WriteLine("=== ===================");
            do {
                switch receive {
                    case ep.RecvCurrent(info):
                        ++count;
                        Console.WriteLine("{0,3} {1,-19}",
                                          info->Id, info->Name);
                        delete info;
                        ep.SendMoveNext();
                        next = true;
                        break;
                    case ep.RecvEnumerationTerminated():
                        next = false;
                        break;
                    case ep.ChannelClosed():
                        next = false;
                        break;
                }
            } while (next);
            Console.WriteLine();
            Console.WriteLine(count + " services are found.");

            delete ep;
            return 0;
        }

        internal static int DefaultMain(DefaultConfig! config)
        {
            Console.WriteLine("Usage: svconf <command> [name] [type]");
            Console.WriteLine("  @start     Start the service");
            Console.WriteLine("  @stop      Stop the service");
            //Console.WriteLine("  @restart   Restart the service");
            Console.WriteLine("  @list      Show a list of running services");
            Console.WriteLine("  type: Default or Resilient");

            return 0;
        }
    }
}

