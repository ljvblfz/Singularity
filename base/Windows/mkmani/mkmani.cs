////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   DistroBuilder.cs
//
//  Note:   Builds a manifest by analyzing a list of assemblies

using System;
using System.IO;
using System.Xml;
using System.Collections;

public class MkMani
{
    // Print the correct command line args for the program
    static void Usage()
    {
        Console.WriteLine("Usage:\n" +
                          "    mkmani /out:<manifest> /app:<app> [options] [assemblies]\n" +
                          "Options:\n" +
                          "    /app:<app>          - Set name of application.\n" +
                          "    /cache:<path>       - Root of file cache.\n" +
                          "    /out:<manifest>     - Set name of output manifest.\n" +
                          "    /x86:<image.x86>    - Set name of .x86 image file.\n" +
                          "    /r:assembly         - Reference an assembly.\n" +
                          "    /ref:assembly       - Reference an assembly.\n" +
                          "    /codegen:xxx        - Add a code generation parameter.\n" +
                          "    /linker:xxx         - Add a linker parameter.\n" +
                          "");
    }

    static int Main(string[] args)
    {
        ArrayList infiles = new ArrayList();
        ArrayList codegen = new ArrayList();
        ArrayList linker = new ArrayList();
        string outfile = null;
        string appname = null;
        string x86file = "";
        string cacheDirectory = null;

        // Temporaries for command-line parsing
        bool needHelp = (args.Length == 0);

        for (int i = 0; i < args.Length && !needHelp; i++) {
            string arg = (string) args[i];

            if (arg.Length >= 2 && (arg[0] == '-' || arg[0] == '/')) {
                string name = null;
                string value = null;
                int n = arg.IndexOf(':');
                if (n > -1) {
                    name = arg.Substring(1, n - 1).ToLower();
                    if (n < arg.Length + 1) {
                        value = arg.Substring(n + 1);
                    }
                }
                else {
                    name = arg.Substring(1).ToLower();
                }

                bool badArg = false;
                switch (name) {
                    case "h":
                    case "help":
                    case "?":
                        needHelp = true;
                        break;

                    case "a":
                    case "app":
                        badArg = (value == null);
                        appname = value;
                        break;

                    case "ca":
                    case "cache":
                        badArg = (value == null);
                        cacheDirectory = value.TrimEnd('/', '\\') + "\\";
                        break;

                    case "co":
                    case "code":
                    case "codegen":
                        badArg = (value == null);
                        codegen.Add(value);
                        break;

                    case "li":
                    case "link":
                    case "linker":
                        badArg = (value == null);
                        linker.Add(value);
                        break;

                    case "o":
                    case "out":
                        badArg = (value == null);
                        outfile = value;
                        break;

                    case "r":
                    case "ref":
                        badArg = (value == null);
                        if (value != null) {
                            infiles.Add(value);
                        }
                        break;

                    case "x":
                    case "x86":
                        badArg = (value == null);
                        x86file = value;
                        break;

                    default:
                        Console.WriteLine("Unknown command line argument: {0}", arg);
                        needHelp = true;
                        break;
                }
                if (badArg) {
                    Console.WriteLine("Invalid command line argument: {0}", arg);
                    needHelp = true;
                }
            }
            else {
                infiles.Add(arg);
            }
        }

        if (appname == null || outfile == null || cacheDirectory == null ||
            infiles.Count == 0) {

            Console.WriteLine("Arguments missing from command line.");
            needHelp = true;
        }

        if (needHelp) {
            Usage();
            return 1;
        }

        // check all input files
        foreach (string filename in infiles) {
            if (!File.Exists(filename)) {
                Console.WriteLine("Error: Assembly '{0}' not found.", filename);
                return 2;
            }
        }

        if (!Directory.Exists(cacheDirectory)) {
            Console.WriteLine("Error: Cache directory '{0}' not found.", cacheDirectory);
            return 2;
        }

        // initialize the empty app manifest.
        ManifestBuilder mb = new ManifestBuilder(cacheDirectory, infiles);

        // create the app manifest
        if (mb.CreateNewManifest(appname,
                                 (x86file != "") ? Path.GetFullPath(x86file) : x86file)) {

            // Add the codegen flags.
            foreach (string param in codegen) {
                mb.AddCodegenParameter(param);
            }

            // Add the linker flags.
            foreach (string param in linker) {
                mb.AddLinkerParameter(param);
            }

            // output the xml document:
            XmlTextWriter writer = new XmlTextWriter(outfile,
                                                     System.Text.Encoding.UTF8);
            writer.Formatting = Formatting.Indented;
            mb.Save(writer);
            writer.Close();

            return 0;
        }
        else {
            // Error
            return 1;
        }
    }
}
