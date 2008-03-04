//------------------------------------------------------------------------------
// <copyright company='Microsoft Corporation'>
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//   Information Contained Herein is Proprietary and Confidential.
// </copyright>
//------------------------------------------------------------------------------


using System;
using System.Diagnostics;
using Microsoft.VisualStudio.WebHost;
using System.Globalization;

using Microsoft.Singularity;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Configuration;
using Microsoft.Singularity.Channels;
using Microsoft.Contracts;
using Microsoft.SingSharp.Reflection;
using Microsoft.Singularity.Applications;
[assembly: Transform(typeof(ApplicationResourceTransform))]

namespace Microsoft.VisualStudio.WebServer {


    [ConsoleCategory(HelpMessage="cassini [options] A web server",
                     DefaultAction=true)]
    internal sealed class Parameters
    {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        [BoolParameter( "v", Default=false , HelpMessage="Verbose")]
        internal bool verbose;

        [BoolParameter( "s", Default=false , HelpMessage="Silent")]
        internal bool silent;

        [BoolParameter( "quitURL", Default=false , HelpMessage="Allow special URL to terminate")]
        internal bool quitURL;

        [StringParameter( "port", Default=null , HelpMessage="Port ID to listen to")]
        internal string portString;

        [StringParameter( "vpath", Default="", HelpMessage="Root of Virtual Path")]
        internal string virtualPath;

        [StringParameter( "client", Default="", HelpMessage="Client IP")]
        internal string client;

        [StringParameter( "apparg", Default="", HelpMessage="Arg to pass to client app")]
        internal string appArg;

        [StringParameter( "app", Mandatory=true, Default="", HelpMessage="Name of the app to run")]
        internal string app;

        reflective internal Parameters();

        internal int AppMain() {
            return WebServerApp.AppMain(this);
        }
    }

    public sealed class WebServerApp {

        internal static int AppMain(Parameters! config) {
            bool verbose = config.verbose;
            bool silent =  config.silent;

            string virtualPath = config.virtualPath;
            if (virtualPath != null) {
                virtualPath = virtualPath.Trim();
            }
            if ((virtualPath == null) || (virtualPath.Length == 0)) {
                virtualPath = "/";
            }
            else {
                if (virtualPath.StartsWith("/") == false) {
                    if (!silent) {
                        ShowUsage();
                    }
                    return -1;
                }
            }

            string physicalPath = "\\";
            /*
              HACKHACK FIXME: the file system is not wired up
              in Singularity; avoid using it!

            string physicalPath = (string)commandLine.Options["path"];
            if (physicalPath != null) {
                physicalPath = physicalPath.Trim();
            }
            if ((physicalPath == null) || (physicalPath.Length == 0)) {
                if (!silent) {
                    ShowUsage();
                }
                return -1;
            }
            else {
                if (Directory.Exists(physicalPath) == false) {
                    if (!silent) {
                        ShowMessage("The physical path '"+ physicalPath + "' does not exist!");
                    }
                    return -2;
                }

                // added this to resolve paths like "."
                physicalPath = Path.GetFullPath(physicalPath);
            }
            */

            int port = 0;
            string portText = config.portString;
            if (portText != null) {
                portText = portText.Trim();
            }
            if ((portText != null) && (portText.Length != 0)) {
                try {
                    port = Int32.Parse(portText);
                    if ((port < 1) || (port > 65535)) {
                        if (!silent) {
                            ShowUsage();
                        }
                        return -1;
                    }
                }
                catch {
                    if (!silent) {
                        ShowMessage("Invalid port '" + portText + "'");
                    }
                    return -3;
                }
            }
            else {
                port = 80;
            }

            // clientIP was added so that we could test using Cassini remotely on
            // lab machines. This now allows the client to be localhost or clientIP.
            string clientIP = config.client;
            if (clientIP != null) {
                clientIP = clientIP.Trim();
            }
            if ((clientIP == null) || (clientIP.Length == 0)) {
                clientIP = "localhost";
            }

            //
            // Singularity-specific initialization
            //
            string webApp = config.app;
            bool quitURL = config.quitURL;

            // FIXME: should generalize this so more than one arg can be passed to
            // the webapp!
            string[] appArgs = null;

            if (config.appArg != null) {
                appArgs = new string[1];
                appArgs[0] = config.appArg;
            }

            if ((webApp == null) || (webApp.Length == 0)) {
                webApp = "HelloWebApp.x86";
            }

            webApp.Trim();

            if (!Dispatcher.Initialize(webApp, appArgs, verbose, quitURL))
            {
                if (!silent) {
                    ShowMessage("Invalid web application name \"" + webApp + "\"");
                    return -5;
                }
            }

            // ====================
            // Actually start the server below
            // ===================

            try {
                Server server = new Server(port, virtualPath, physicalPath, clientIP);
                server.Start();

                String s1 = String.Format(  Environment.NewLine +
                                        "Running Web Server on port {0}." + Environment.NewLine + Environment.NewLine +
                                        "Application '{1}' is mapped to '{2}'." + Environment.NewLine + Environment.NewLine,
                                        port, virtualPath, physicalPath);
                String s2 = String.Format(  "http://localhost:{0}{1}",
                                            port, virtualPath);
                if (!s2.EndsWith("/"))
                    s2 += "/";
                Console.WriteLine(s1+s2+Environment.NewLine);

                // Currently no console-read support in Singularity. Run forever!
                //Console.WriteLine("Hit Enter to stop the server");
                //Console.ReadLine();
                //server.Stop();
            }
            catch (Exception ex) {
                if (!silent) {
                    ShowMessage("Error opening port " + port + ": " + ex.Message);
                }
                return -5;
            }

            return 0;
        }

        private static void ShowUsage() {
            string usageString;

            usageString = "Invalid usage.\n\n";
            usageString += "cassini [options]\n";
            usageString += "Options:\n";
            usageString += "  -app:<application>       run application.\n";
            usageString += "  -apparg:<argument>       supply argument to application.\n";
            usageString += "  -path:<physicalpath>\n";
            usageString += "  -port:<port>\n";
            usageString += "  -quitURL                 quit if URL ending in \"quit\" is received.\n";
            usageString += "  [-silent]\n";
            usageString += "  [-verbose]               show files served\n";
            usageString += "  [-vpath:<virtualpath>]\n";
            usageString += "  [-client:<clientIP>]\n";

            ShowMessage(usageString);
        }

        private static void ShowMessage(String msg) {
            Console.WriteLine("Visual Web Developer Web Server:\n" + msg);
        }
    }
}
