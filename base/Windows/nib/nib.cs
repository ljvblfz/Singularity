///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   nib.cs
//
//  Note:   Native image building tool.
//

using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading;

#if !SINGULARITY
using System.Xml;
using Windows;
#else
using Microsoft.Singularity.Xml;
using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Directory;
using Microsoft.Singularity;
using Microsoft.Singularity.Applications;
#endif

namespace Microsoft.Singularity.Applications
{
    public class NativeImageBuilder
    {
        //
        // Global flags.
        //
#if !SINGULARITY
        public static string Bartok        = "bartok.exe";
#else
        public static string Bartok        = "bartok";
#endif
        public static bool   DoClean       = false;
        public static bool   DoCodegen     = true;
        public static bool   DoLinker      = true;
        public static bool   DoManifest    = true;
        public static bool   DoLogBartok   = false;
        public static bool   ForceCodegen  = false;
        public static bool   ForceLinker   = false;
        public static bool   ForceManifest = false;
        public static bool   Verbose       = false;

        //
        // Fields in the native image building class
        //
        private Options options;
        // private string currentFile; // For warning and error messages.
        private static string machine;
        private string cacheDirectory;
        private string libCacheDirectory;
        private string nativeDirectory;
        private string tempDirectory;
        private string optionsFile;

#if !SINGULARITY
        private XmlDocument optManifest;
        private XmlDocument appManifest;
#else
        private XmlNode optManifest;
        private XmlNode appManifest;
#endif

        // set up an object that can build native images based on the system manifest.
        public NativeImageBuilder(string cacheDirectory,
                                  string libCacheDirectory,
                                  string nativeDirectory,
                                  string tempDirectory)
        {
            this.options = null;
            this.cacheDirectory = cacheDirectory;
            this.libCacheDirectory = libCacheDirectory;
            this.nativeDirectory = nativeDirectory;
            this.tempDirectory = tempDirectory;
        }

        public bool LoadOptionsManifest(string optionsFile)
        {
            this.optionsFile = optionsFile;
            this.options = ParseOptionsManifest(optionsFile, out optManifest);
            return (options != null);
        }

        //////////////////////////////////////////////// Methods to help with XML.
        //
        private XmlNode AddElement(XmlNode parent, string name)
        {
#if !SINGULARITY
            XmlNode element = appManifest.CreateNode(XmlNodeType.Element, name, "");
            if (parent != null) {
                parent.AppendChild(element);
            }
            return element;
#else
            XmlNode element = appManifest.CreateNode(name);
            if (parent != null) {
                parent.AddChild(element);
            }
            return element;

#endif
        }

        private void AddAttribute(XmlNode node, string name, string value)
        {
#if !SINGULARITY
            XmlAttribute attr = appManifest.CreateAttribute(name);
            attr.Value = value;
            node.Attributes.Append(attr);
#else
            node.AddAttribute(name,value);
#endif
        }

        private void InsertAttribute(XmlNode node, string name, string value)
        {
#if !SINGULARITY
            XmlAttribute attr = appManifest.CreateAttribute(name);
            attr.Value = value;
            node.Attributes.Prepend(attr);
#else
            node.PrependAttribute(name,value);
#endif
        }

        // Return an array with every child of the parent whose name matches
        // <name>
        private static XmlNode[] GetChildren(XmlNode parent, string name)
        {
            ArrayList result = new ArrayList();

            foreach (XmlNode child in parent.ChildNodes) {
                if (child.Name == name) {
                    result.Add(child);
                }
            }

            if (result.Count == 0) {
                return new XmlNode[0];
            }

            XmlNode[] children = new XmlNode [result.Count];
            for (int i = 0; i < result.Count; i++) {
                children[i] = (XmlNode)result[i];
            }
            return children;

        }

        // Get the first child named `name'
        private static XmlNode GetChild(XmlNode parent, string name)
        {
#if !SINGULARITY
            return parent[name];
#else
            return parent.GetChild(name);
#endif
        }

        // Get end node along a path of first matches
        private static XmlNode GetDescendant(XmlNode root, string [] path)
        {
            XmlNode parent = root;

            foreach (string pathelement in path) {
                parent = GetChild(parent, pathelement);
                if (parent == null) {
                    return null;
                }
            }
            return parent;
        }

        // Get the named attribute if it exists.
        private static string GetAttribute(XmlNode node, string attrib)
        {
#if !SINGULARITY
            XmlAttribute xa = node.Attributes[attrib];
            return xa != null ? xa.Value : null;
#else
            return node.GetAttribute(attrib, null);
#endif
        }

        private static string GetAttribute(XmlNode node, string attrib, string value)
        {
#if !SINGULARITY
            XmlAttribute xa = node.Attributes[attrib];
            return xa != null ? xa.Value : value;
#else
            string val = node.GetAttribute(attrib,null);
            return  val != null ? val : value;
#endif
        }

        private static int GetAttribute(XmlNode node, string attrib, int value)
        {
#if !SINGULARITY
            XmlAttribute xa = node.Attributes[attrib];
            return xa != null ? Int32.Parse(xa.Value) : value;
#else
            string val = node.GetAttribute(attrib,null);
            return  val != null ? Int32.Parse(val) : value;
#endif
        }


#if SINGULARITY

        public bool RunProgram(string appname, string program, string arguments, StreamWriter log, PipeMultiplexer! outputMux)
        {
            string commandName;
            string[]! args;
            Process p = null;

            UnicodePipeContract.Imp! childStdInImp;
            UnicodePipeContract.Exp! childStdInExp;
            UnicodePipeContract.Imp! childStdOutImp;
            UnicodePipeContract.Exp! childStdOutExp;

            NibHelper.BreakCommandLine(program + " " + arguments, new char[] {' '},
                         out commandName, out args);

            DirectoryServiceContract.Imp:Ready! ds = NibHelper.GetDirectoryServiceContract();
            WriteLine("\t\t[{0,-30}] {1}", appname, program);
            try {
                if (log != null) {
                    UnicodePipeContract.NewChannel(out childStdInImp, out childStdInExp);
                    UnicodePipeContract.NewChannel(out childStdOutImp, out childStdOutExp);

                    p = Binder.CreateProcess(ds,args,(!)childStdInExp,(!)childStdOutImp);
                    if (p == null)  return false;

                    p.Start();

                    bool done = false;
                    assert childStdOutExp != null;
                    char[] buffer = null;
                    while (!done) {
                        switch receive {
                        case childStdOutExp.Write(i_buffer,i_offset, i_count):
                             //copy buffer;
                            buffer =  new char[i_count];
                            int i=0;
                            int j = i_offset;
                            for (; i < i_count; ) {
                                buffer[i++] = i_buffer[j++];
                            }
                            childStdOutExp.SendAckWrite(i_buffer);
                            Console.WriteLine(buffer);
                            log.Write(buffer, 0, i_count);
                            if (i_count < buffer.Length) {
                                break;
                            }
                            buffer = null;
                            continue;
                        case childStdOutExp.ChannelClosed():
                            done = true;
                            continue;
                        }
                    }
                    try {
                        delete childStdInExp;  // kill our end of the input pipe;
                    } catch(Exception ex) {
                        Console.WriteLine("error deleting endpoint");
                        ShowException(ex);
                    }
                }
                else {
                    p = Binder.CreateProcess(ds,args,outputMux);
                    if (p == null)  return false;
                    p.Start();
                }
                if (p != null) {
                    p.Join();
                }
            } finally {
                NibHelper.ReleaseDirectoryServiceContract(ds);
            }
            return true;
        }
#else
        public bool RunProgram(string appname, string program, string arguments, StreamWriter log)
        {
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = (log != null);
            p.StartInfo.FileName = program;
            p.StartInfo.Arguments = arguments;
            int exit = -1;

            long peakPagedMemorySize = 0;
            long peakVirtualMemorySize = 0;
            long peakWorkingSet = 0;
            try {
                p.Start();
                p.PriorityClass = ProcessPriorityClass.BelowNormal;

                // In Win32, the usual sequence is to create a process with the initial thread
                // suspended, then add the process to a job object.  But the .Net framework
                // doesn't provide any way to do this; the process is not created until
                // Process.Create(), and there is no flag in ProcessStartInfo to indicate that
                // the initial thread should be created suspended.  So, we just start the
                // process and immediately try to set the relevant parameters.

                if (_BuildingParallel) {
                    if (_JobHandle != IntPtr.Zero) {
                        if (!Kernel32.AssignProcessToJobObject(_JobHandle, p.Handle))
                            Console.WriteLine("FAILED to assign process to job: " + Kernel32.GetLastErrorText());
                    }

                    try {
                        p.ProcessorAffinity = _ChildProcessorAffinityMask;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("FAILED to set processor affinity of child process!");
                        ShowException(ex);
                    }
                }

                WriteLine("\t\t[{0,-30}] {1}", appname, program);

                if (Verbose) {
                    WriteLine("\t{0}", arguments);
                }
                if (log != null) {
                    StreamReader reader = p.StandardOutput;
                    char[] buffer = new char [4096];

                    for (;;) {
                        int read = reader.ReadBlock(buffer, 0, buffer.Length);
                        if (_ShowDots)
                            Console.Write(".");
                        log.Write(buffer, 0, read);
                        if (read < buffer.Length) {
                            break;
                        }
                        try {
                            peakPagedMemorySize
                                = Math.Max(p.PeakPagedMemorySize64, peakPagedMemorySize);
                            peakVirtualMemorySize
                                = Math.Max(p.PeakVirtualMemorySize64, peakVirtualMemorySize);
                            peakWorkingSet
                                = Math.Max(p.PeakWorkingSet64, peakWorkingSet);
                        }
                        catch (Exception) {
                        }
                    }
                }
                if (_ShowDots)
                    Console.WriteLine();
                p.WaitForExit();
                exit = p.ExitCode;
            }
            catch (Exception e) {
                Console.WriteLine("nib: Exception: {0}", e);
            }
            if (log != null) {
                log.WriteLine("# peak memory: paged={0}KB, virtual={1}KB, working={2}KB",
                              peakPagedMemorySize / 1024,
                              peakWorkingSet / 1024,
                              peakVirtualMemorySize / 1024);
            }

            if (exit != 0) {
                if (true) {
                    WriteLine("nib: Error: [{0}] {1} failed with exit code: {2}", appname, program, exit);
                }
                return false;
            }
            else {
                return true;
            }
        }

#endif

        //////////////////////////////////////////////////////////////////////////
        //
        class OAssembly
        {
            public readonly string Name;
            public readonly int MajorVersion;   // -1 if null.
            public readonly int MinorVersion;   // -1 if null.
            public readonly int Build;          // -1 if null.
            public readonly int Revision;       // -1 if null.
            public string Locale;
            public string PublicKey;
            public string Cache;
            // products:
            public string Final;

            public OAssembly(string name, string version)
            {
                this.Name = name;
                this.MajorVersion = -1;
                this.MinorVersion = -1;
                this.Build = -1;
                this.Revision = -1;

                if (version != null) {
                    int offset = 0;
                    this.MajorVersion = ParseInt(version, ref offset);
                    this.MinorVersion = ParseInt(version, ref offset);
                    this.Build = ParseInt(version, ref offset);
                    this.Revision = ParseInt(version, ref offset);
                }
            }

            private static int ParseInt(string s, ref int offset)
            {
                int num = -1;

                while (offset < s.Length && s[offset] >= '0' && s[offset] <= '9') {
                    if (num == -1) {
                        num = (s[offset++] - '0');
                    }
                    else {
                        num = num * 10 + (s[offset++] - '0');
                    }
                }
                if (offset < s.Length && s[offset] == '.') {
                    offset++;
                }
                return num;
            }

#if xxx
            public override String ToString()
            {
                return String.Format("{0}.{1}.{2}.{3}.{4}.{5}.{6}",
                                     Name,
                                     MajorVersion,
                                     MinorVersion,
                                     Build,
                                     Revision,
                                     Locale != null ? Locale : "",
                                     PublicKey != null ? PublicKey.Substring(0,8) : "");
            }
#endif
            // Only non-null fields in the target should be compared.
            // So that a 1.0.3000.3 in "this" matches 1.0.3000 in target.
            public bool IsMatch(OAssembly target)
            {
                if (String.Compare(Name, target.Name, true) != 0) {
                    return false;
                }
                if (target.Locale != null) {
                    if (Locale == null || String.Compare(Locale, target.Locale, true) != 0) {
                        return false;
                    }
                }
                if (target.PublicKey != null) {
                    if (PublicKey == null ||
                        String.Compare(PublicKey, target.PublicKey, true) != 0) {
                        return false;
                    }
                }
                if (target.MajorVersion != -1 && MajorVersion != target.MajorVersion) {
                    return false;
                }
                if (target.MinorVersion != -1 && MinorVersion != target.MinorVersion) {
                    return false;
                }
                if (target.Build != -1 && Build != target.Build) {
                    return false;
                }
                if (target.Revision != -1 && Revision != target.Revision) {
                    return false;
                }
                return true;
            }
        }

        class OReplacement : OAssembly
        {
            public OReplacement(string name, string version)
                : base(name, version)
            {
            }

            public OAssembly[] replacements;
        }

        class OCodegen
        {
            public string[] parameters;
        }

        class OLibrary
        {
            public readonly string Name;
            public string Cache;
            // products:
            public string Final;

            public OLibrary(string name)
            {
                this.Name = name;
            }
        }

        class OLinker
        {
            public string[] parameters;
            public OLibrary[] libraries;
        }

        class Options
        {
            public OReplacement[] assemblies;
            public OCodegen codegen;
            public OLinker linker;
        }

        //////////////////////////////////////////////////////////////////////////
        //
#if !SINGULARITY
        private Options ParseOptionsManifest(string optionsFile,
                                                    out XmlDocument optManifest)
#else
        private Options ParseOptionsManifest(string optionsFile,
                                                    out XmlNode optManifest)
#endif
        {
            // <options>
            //   <assembly name= version= publickey= locale= />
            //     <replacement name= cache= version= publickey= locale= />
            //   </assembly>
            //   <codegen>
            //     <parameter value= />
            //   </codegen>
            //   <linker>
            //     <parameter value= />
            //     <library name= cache= />
            //   </linker>
            // <options>

#if SINGULARITY
            // read the file
            optManifest = NibHelper.OpenXmlDocument(optionsFile);
            if (optManifest != null) Console.WriteLine("parse of {0} successful", optionsFile);
            else Console.WriteLine("parse of {0} failed", optionsFile);
#else
            optManifest = new XmlDocument();
            optManifest.Load(optionsFile);
#endif
            XmlNode options = GetChild(optManifest, "options");
            if (options == null) {
                WriteLine("nib: Error: missing top-level <options> tag.");
                return null;
            }
            Options opts = new Options();

            XmlNode[] assemblies = GetChildren(options, "assembly");
            if (assemblies.Length != 0) {
                opts.assemblies = new OReplacement[assemblies.Length];
                for (int a = 0; a < assemblies.Length; a++) {
                    XmlNode anode = assemblies[a];
                    string name = GetAttribute(anode, "name");
                    string version = GetAttribute(anode, "version");

                    if (name == null) {
                        WriteLine("nib: Error: <assembly> tag missing 'name' attribute.");
                        return null;
                    }

                    OReplacement assembly = new OReplacement(name, version);
                    assembly.PublicKey = GetAttribute(anode, "publickey");
                    assembly.Locale = GetAttribute(anode, "locale");

                    XmlNode[] replacements = GetChildren(anode, "replacement");
                    if (replacements.Length == 0) {
                        WriteLine("nib: Error: <assembly> tag missing <replacement> child.");
                        return null;
                    }

                    assembly.replacements = new OAssembly[replacements.Length];
                    for (int r = 0; r < replacements.Length; r++) {
                        XmlNode rnode = replacements[r];
                        name = GetAttribute(rnode, "name");
                        version = GetAttribute(rnode, "version");
                        if (name == null) {
                            WriteLine("nib: Error: <replacement> tag missing 'name' attribute.");
                            return null;
                        }

                        OAssembly replacement = new OAssembly(name, version);
                        replacement.PublicKey = GetAttribute(rnode, "publickey");
                        replacement.Locale = GetAttribute(rnode, "locale");
                        replacement.Cache = GetAttribute(rnode, "cache");
                        assembly.replacements[r] = replacement;
                    }
                    opts.assemblies[a] = assembly;
                }
            }

            // Parse the codegen section.
            XmlNode codegen = GetChild(options, "codegen");
            if (codegen == null) {
                WriteLine("nib: Error: <options> tag missing <codegen> child.");
            }
            opts.codegen = new OCodegen();

            XmlNode[] parameters = GetChildren(codegen, "parameter");
            if (parameters.Length != 0) {
                string[] pars = new string [parameters.Length];
                for (int p = 0; p < parameters.Length; p++) {
                    pars[p] = GetAttribute(parameters[p], "value");
                    if (pars[p] == null) {
                        WriteLine("nib: Error: <parameter> tag missing 'value' attribute.");
                        return null;
                    }
                }
                opts.codegen.parameters = pars;
            }

            // Parse the linker section.
            XmlNode linker = GetChild(options, "linker");
            if (linker == null) {
                WriteLine("nib: Error: <options> tag missing <linker> child.");
            }
            opts.linker = new OLinker();

            parameters = GetChildren(linker, "parameter");
            if (parameters.Length != 0) {
                string[] pars = new string [parameters.Length];
                for (int p = 0; p < parameters.Length; p++) {
                    pars[p] = GetAttribute(parameters[p], "value");
                    if (pars[p] == null) {
                        WriteLine("nib: Error: <parameter> tag missing 'value' attribute.");
                        return null;
                    }
                }
                opts.linker.parameters = pars;
            }

            XmlNode[] libraries = GetChildren(linker, "library");
            if (libraries.Length != 0) {
                opts.linker.libraries = new OLibrary [libraries.Length];
                for (int l = 0; l < libraries.Length; l++) {
                    XmlNode lnode = libraries[l];
                    string name = GetAttribute(lnode, "name");
                    if (name == null) {
                        WriteLine("nib: Error: <library> tag missing 'name' attribute.");
                        return null;
                    }

                    OLibrary library = new OLibrary(name);
                    library.Cache = GetAttribute(lnode, "cache");
                    opts.linker.libraries[l] = library;
                }
            }

            return opts;
        }

        class Application
        {
            public readonly string Name;
            public string Path;
            public string Cache;
            public OAssembly[] assemblies;
            // products:
            public string obj;
            public string info;
            public string super;
            public string pdb;
            public string x86;
            public string xman; // Output manifest
            public string map;
            public string log;

            public OCodegen codegen;
            public OLinker linker;

            public Application(string name)
            {
                this.Name = name;
            }
        }

        ///////////////////////////////////////////////////// Manifest Processing.
        //
        // <application name=>
        //   <process id= main= path= cache=>
        //     <assemblies>
        //       <assembly name= version= publickey= cache= />
        //     </assemblies>
        //     <categories>
        //       <category id= name= class=>
        //         <device signature=  />
        //         <ioPortRange id= baseAddress= rangeLength= />
        //         <ioIrqRange id= baseAddress= rangeLength= />
        //         <ioDmaRange id= baseAddress= rangeLength= />
        //         <ioMemoryRange addressBits= alignment= rangeLength= fixed= />
        //         <extension id= startStateId= contractName= endpointEnd= assembly=...>
        //           <imp>
        //             <inherit name= />
        //           </imp>
        //           <exp>
        //             <inherit name= />
        //           </exp>
        //         </extension>
        //         <serviceProvider id= startStateId= contractName= endpointEnd= assembly=...>
        //           <imp>
        //             <inherit name= />
        //           </imp>
        //           <exp>
        //             <inherit name= />
        //           </exp>
        //         </serviceProvider>
        //       </category>
        //     </categories>
        //     <codegen>
        //       <parameter value= />
        //     </codegen>
        //     <linker>
        //       <parameter value= />
        //     </linker>
        //   </process>
        // </application>
        //
#if !SINGULARITY
        private Application ParseApplicationManifest(string applicationFile,
                                                     out XmlDocument appManifest)
        {
            appManifest = new XmlDocument();
            appManifest.Load(applicationFile);
#else
        private Application ParseApplicationManifest(string applicationFile,
                                                     out XmlNode appManifest)
        {
            appManifest = NibHelper.CreateXmlDocument(applicationFile);
#endif

            XmlNode application  = GetChild(appManifest, "application");
            if (application == null) {
                WriteLine("nib: Error: missing top-level <application> tag.");
                return null;
            }
            string name = GetAttribute(application, "name");
            if (name == null) {
                WriteLine("nib: Error: <application> tag missing 'name' attribute.");
                return null;
            }
            XmlNode process = GetChild(application, "process");
            if (process == null) {
                WriteLine("nib: Error: <application> tag missing <process> child tag.");
                return null;
            }
            Application app = new Application(name);

            app.Path = GetAttribute(process, "path");
            app.Cache = GetAttribute(process, "cache");

            XmlNode assemblyHolder = GetChild(process, "assemblies");
            if (assemblyHolder == null) {
                WriteLine("nib: Error: <process> tag missing <assemblies> child tag.");
                return null;
            }

            XmlNode[] assemblies = GetChildren(assemblyHolder, "assembly");
            if (assemblies.Length != 0) {
                app.assemblies = new OAssembly[assemblies.Length];
                for (int a = 0; a < assemblies.Length; a++) {
                    XmlNode anode = assemblies[a];
                    name = GetAttribute(anode, "name");
                    string version = GetAttribute(anode, "version");
                    if (name == null) {
                        WriteLine("nib: Error: <assembly> tag missing 'name' attribute.");
                        return null;
                    }

                    OAssembly assembly = new OAssembly(name, version);
                    assembly.Cache = GetAttribute(anode, "cache");
                    assembly.Locale = GetAttribute(anode, "locale");
                    assembly.PublicKey = GetAttribute(anode, "publickey");

                    app.assemblies[a] = assembly;
                }
            }

            // Parse the optional codegen section.
            XmlNode codegen = GetChild(process, "codegen");
            if (codegen != null) {
                app.codegen = new OCodegen();

                XmlNode[] parameters = GetChildren(codegen, "parameter");
                if (parameters.Length != 0) {
                    string[] pars = new string [parameters.Length];
                    for (int p = 0; p < parameters.Length; p++) {
                        pars[p] = GetAttribute(parameters[p], "value");
                        if (pars[p] == null) {
                            WriteLine("nib: Error: <parameter> tag missing 'value' attribute.");
                            return null;
                        }
                    }
                    app.codegen.parameters = pars;
                }
            }

            // Parse the optional linker section.
            XmlNode linker = GetChild(process, "linker");
            if (linker != null) {
                app.linker = new OLinker();

                XmlNode[] parameters = GetChildren(linker, "parameter");
                if (parameters.Length != 0) {
                    string[] pars = new string [parameters.Length];
                    for (int p = 0; p < parameters.Length; p++) {
                        pars[p] = GetAttribute(parameters[p], "value");
                        if (pars[p] == null) {
                            WriteLine("nib: Error: <parameter> tag missing 'value' attribute.");
                            return null;
                        }
                    }
                    app.linker.parameters = pars;
                }

    #if false
                // For now, we don't allow libraries in the application linker section.
                XmlNode[] libraries = GetChildren(linker, "library");
                if (libraries.Length != 0) {
                    app.linker.libraries = new OLibrary [libraries.Length];
                    for (int l = 0; l < libraries.Length; l++) {
                        XmlNode lnode = libraries[l];
                        string lname = GetAttribute(lnode, "name");
                        if (lname == null) {
                            WriteLine("nib: Error: <library> tag missing 'name' attribute.");
                            return null;
                        }

                        OLibrary library = new OLibrary(lname);
                        library.Cache = GetAttribute(lnode, "cache");
                        app.linker.libraries[l] = library;
                    }
                }
    #endif
            }

            return app;
        }

        private bool SetMaxChildId(XmlNode root, string xmlGroup)
        {
            XmlNode group = GetChild(root, xmlGroup);
            if (group != null) {
                int maxId = -1;
                foreach (XmlNode node in group.ChildNodes) {
                    int id = GetAttribute(node, "id", -1);
                    if (id == -1) {
                        id = maxId + 1;
                        InsertAttribute(node, "id", id.ToString());
                    }
                    maxId = Math.Max(maxId, id);
                }
                InsertAttribute(group, "length", (maxId + 1).ToString());
                return true;
            }
            else {
                group = AddElement(root, xmlGroup);
                AddAttribute(group, "length", "0");
            }
            return false;
        }

        private bool InstallApplicationManifest()
        {
            XmlNode application = GetChild(appManifest, "application");
            if (application == null) {
                WriteLine("nib: Error: missing top-level <application> tag.");
                return false;
            }

            int maxProcessId = -1;
            foreach (XmlNode process in GetChildren(application, "process")) {
                int maxCategoryId = -1;

                maxProcessId = Math.Max(maxProcessId, GetAttribute(process, "id", -1));
                // look for categories node

                foreach (XmlNode categories in GetChildren(process, "categories")) {
                    foreach (XmlNode category in GetChildren(categories, "category")) {
                        maxCategoryId++;
                        SetMaxChildId(category, "endpoints");
                        string name = GetAttribute(category, "name", "");
                        if (name == "driver") {
                            SetMaxChildId(category, "fixedHardware");
                            SetMaxChildId(category, "dynamicHardware");
                            SetMaxChildId(category, "configs");
                        }
                        // set these for all categories
                        SetMaxChildId(category, "StringParameters");
                        SetMaxChildId(category, "StringArrayParameters");
                        SetMaxChildId(category, "LongParameters");
                        SetMaxChildId(category, "BoolParameters");
                    }
                }
                AddAttribute(process, "categories", (maxCategoryId + 1).ToString());
            }
            AddAttribute(application, "processes", (maxProcessId + 1).ToString());
            return true;
        }
        //
        //////////////////////////////////////////////////////////////////////////

        private OAssembly[] FindReplacements(OAssembly assembly)
        {
            if (options == null) {
                return null;
            }

            OAssembly[] assemblies = options.assemblies;

            for (int a = 0; a < assemblies.Length; a++) {
                if (assembly.IsMatch(assemblies[a])) {
                    return ((OReplacement)assemblies[a]).replacements;
                }
            }
            return null;
        }

        private OAssembly[] FindReplacements(OAssembly[] assemblies)
        {
            // Short circuit search if there are no replacement assemblies.
            if (options == null || options.assemblies == null) {
                return assemblies;
            }

            ArrayList list = new ArrayList();

            for (int a = 0; a < assemblies.Length; a++) {
                OAssembly[] replacements = FindReplacements(assemblies[a]);
                if (replacements != null) {
                    for (int r = 0; r < replacements.Length; r++) {
                        if (Verbose) {
                            WriteLine("Replacing {0} with {1}", assemblies[a], replacements[r]);
                        }
                        list.Add(replacements[r]);
                    }
                }
                else {
                    list.Add(assemblies[a]);
                }
            }

            OAssembly[] final = new OAssembly[list.Count];
            for (int a = 0; a < list.Count; a++) {
                final[a] = (OAssembly)list[a];
            }
            return final;
        }

        public void DeleteIfExists(string file)
        {
            if (File.Exists(file)) {
                File.Delete(file);
                if (Verbose) {
                    WriteLine("deleted {0}", file);
                }
            }
        }

        private static string ReplaceSuffix(string value, string old, string suffix)
        {
            if (value.EndsWith(old)) {
                return value.Substring(0, value.Length - old.Length) + suffix;
            }
            return value + suffix;
        }

        // Returns true of the application was built or is up to date.
#if !SINGULARITY
        private bool BuildApplication(string applicationFile)
        {
#else
        private bool BuildApplication(string applicationFile, PipeMultiplexer! outputMux)
        {
#endif
            StreamWriter log = null;
            DateTime logBegin = DateTime.Now;
            bool doCodegen = DoCodegen;
            bool doLinker = DoLinker;
            bool doManifest = DoManifest;
            bool doForceCodegen = ForceCodegen;
            bool doForceLinker = ForceLinker;
            bool doForceManifest = ForceManifest;

            try {
                Application app = ParseApplicationManifest(applicationFile, out appManifest);
                if (app == null) {
                    // parse failed.
                    return false;
                }
                if (app.Path == null || app.Path == "") {
                    WriteLine(
                        "nib: Warning: No executable in manifest: {0}.",
                        applicationFile);
                    doCodegen = false;
                    doLinker = false;
                    doForceCodegen = false;
                    doForceLinker = false;

                    // This is hack just for the sake of the kernel.
                    app.Path = app.Name + ".x86";
                }

                WriteLine("\t{0}", applicationFile);

                app.obj = tempDirectory + app.Name + ".obj";
                app.info = tempDirectory + app.Name + "_info.obj";
                app.super = tempDirectory + app.Name + "_superObj.obj";
                app.map = tempDirectory + app.Name + ".map";
                app.log = tempDirectory + app.Name + ".log";
                if (nativeDirectory != null) {
                    app.x86 = nativeDirectory + app.Path;
                }
                else {
                    app.x86 = cacheDirectory + app.Cache.Replace('/', '\\');
                }
                app.xman = ReplaceSuffix(app.x86, ".x86", ".manifest");
                app.pdb = tempDirectory + app.Path + ".pdb";

                DateTime written;
                DateTime update =
                    optionsFile == null ? DateTime.MinValue : File.GetLastWriteTime(optionsFile);
                DateTime newest = File.GetLastWriteTime(applicationFile);
                if (update < newest) {
                    update = newest;
                }

                if (Verbose) {
                    WriteLine("Name = [{0}]", app.Name);
                    WriteLine("Path = [{0}]", app.Path);
                    WriteLine("Cache= [{0}]", app.Cache);
                    WriteLine("obj  = [{0}]", app.obj);
                    WriteLine("info = [{0}]", app.info);
                    WriteLine("super= [{0}]", app.super);
                    WriteLine("pdb  = [{0}]", app.pdb);
                    WriteLine("x86  = [{0}]", app.x86);
                    WriteLine("xman = [{0}]", app.xman);
                    WriteLine("map  = [{0}]", app.map);
                    WriteLine("log  = [{0}]", app.log);
                }

                if (DoClean) {
                    DeleteIfExists(app.obj);
                    DeleteIfExists(app.info);
                    DeleteIfExists(app.super);
                    DeleteIfExists(app.pdb);
                    DeleteIfExists(app.x86);
                    DeleteIfExists(app.xman);
                    DeleteIfExists(app.map);
                    DeleteIfExists(app.log);
                    return true;
                }

#if VERY_VERBOSE
                WriteLine("  >>> Pre replace ***");
                for (int a = 0; a < app.assemblies.Length; a++) {
                    if (Verbose) {
                        WriteLine("  {0}", app.assemblies[a]);
                        WriteLine("     {0}", app.assemblies[a].Cache);
                        WriteLine("     {0}", app.assemblies[a].Final);
                    }
                }
#endif
                // Now that we've parsed the manifest, we replace any mapped assemblies.
                OAssembly[] assemblies = FindReplacements(app.assemblies);

#if VERY_VERBOSE
                WriteLine("  <<< Post replace ***");
#endif
                // Now, find the final code (if it isn't already cached).
                for (int a = 0; a < assemblies.Length; a++) {
                    string test = cacheDirectory + assemblies[a].Name;
                    if (File.Exists(test)) {
                        assemblies[a].Final = test;
                    }
                    else {
                        if (assemblies[a].Cache == null) {
                            WriteLine("nib: Error: Couldn't find assembly: {0}", assemblies[a]);
                            return false;
                        }
                        string assemblyCachePath = assemblies[a].Cache.Replace('/', '\\');
                        string finalAssemblyPath = Path.Combine(cacheDirectory, assemblyCachePath);
                        assemblies[a].Final = finalAssemblyPath;
                    }

#if VERY_VERBOSE
                    if (Verbose) {
                        WriteLine("  {0}", assemblies[a]);
                        WriteLine("     {0}", assemblies[a].Cache);
                        WriteLine("     {0}", assemblies[a].Final);
                    }
#endif
                }

                if (doCodegen) {
                    // Verify that all of the assemblies exist.
                    bool errorFound = false;
                    for (int a = 0; a < assemblies.Length; a++) {
                        if (!File.Exists(assemblies[a].Final)) {
                            WriteLine("nib: Error: Couldn't assembly file: {0}",
                                              assemblies[a].Final);
                            errorFound = true;
                        }
                    }
                    if (errorFound) return false;

                    // Find the newest assembly.
                    newest = update;
                    for (int a = 0; a < assemblies.Length; a++) {
                        written = File.GetLastWriteTime(assemblies[a].Final);
                        if (newest < written) {
                            newest = written;
                        }
                    }

                    DateTime target = DateTime.MinValue;
                    if (File.Exists(app.obj)) {
                        target = File.GetLastWriteTime(app.obj);
                    }

                    if (newest >= target || doForceCodegen) {
                        // Codegen is necessary.

                        // <option>
                        // /out: ..\obj\Prototype.MarkSweep\Shell.obj
                        // /outdir: ..\obj\Prototype.MarkSweep
                        // <assembly>

                        StringBuilder sb = new StringBuilder(4096);
                        if (machine == "x64") sb.Append(" /x64 ");
                        if (options != null) {
                            foreach (string arg in options.codegen.parameters) {
                                sb.Append(" ");
                                sb.Append(arg);
                            }
                        }
                        if (app.codegen != null) {
                            foreach (string arg in app.codegen.parameters) {
                                sb.Append(" ");
                                sb.Append(arg);
                            }
                        }
                        sb.Append(" /out: \"");
                        sb.Append(app.obj);
                        sb.Append("\"");
                        sb.Append(" /outdir: \"");
                        sb.Append(tempDirectory.Trim('\\'));
                        sb.Append("\"");
                        for (int a = 0; a < assemblies.Length; a++) {
                            sb.Append(" \"");
                            sb.Append(assemblies[a].Final);
                            sb.Append("\"");
                        }

                        log = new StreamWriter(app.log);
                        log.WriteLine("# Name = [{0}]", app.Name);
                        log.WriteLine("# Path = [{0}]", app.Path);
                        log.WriteLine("# Cache= [{0}]", app.Cache);
                        log.WriteLine("# obj  = [{0}]", app.obj);
                        log.WriteLine("# info = [{0}]", app.info);
                        log.WriteLine("# super= [{0}]", app.super);
                        log.WriteLine("# pdb  = [{0}]", app.pdb);
                        log.WriteLine("# x86  = [{0}]", app.x86);
                        log.WriteLine("# map  = [{0}]", app.map);
                        log.WriteLine("# log  = [{0}]", app.log);
                        log.WriteLine("# target = {0}", target);
                        log.WriteLine("#newest = {0}", newest);

                        log.WriteLine("#{0} {1}", Bartok, sb.ToString());
                        if (Verbose) {
                            WriteLine(sb.ToString());
                        }
#if !SINGULARITY
                        if (!RunProgram(app.Name, Bartok, sb.ToString(), log)) {
#else
                        if (!RunProgram(app.Name, Bartok, sb.ToString(), log, outputMux)) {
#endif
                            return false;
                        }
                    }
                    else {
                        if (Verbose) {
                            WriteLine("nib: Codegen unneeded: {0}", app.obj);
                        }
                    }
                }

                // Now that we've parsed the manifest, we replace any mapped libraries.
                OLibrary[] libraries =
                    options != null ? options.linker.libraries : new OLibrary[0];

                // Now, find the final code (if it isn't already cached).
                for (int l = 0; l < libraries.Length; l++) {
                    string test = libCacheDirectory + libraries[l].Name;
                    //WriteLine(" looking for final code @ {0}", test);
                    if (File.Exists(test)) {
                        libraries[l].Final = test;
                        if (libraries[l].Cache == null) {
                            libraries[l].Cache = libraries[l].Name;
                        }
                    }
                    else {
                        if (libraries[l].Cache == null) {
                            WriteLine("nib: Error: Couldn't find library: {0}",
                                              libraries[l]);
                            return false;
                        }

                        libraries[l].Final = Path.Combine(cacheDirectory,
                            libraries[l].Cache.Replace('/', '\\'));
                    }

                    if (Verbose) {
                        WriteLine("  {0}", libraries[l].Cache);
                        WriteLine("     {0}", libraries[l].Final);
                    }
                }

                if (doLinker) {
                    // Verify that all of the libraries exist.
                    for (int l = 0; l < libraries.Length; l++) {
                        if (!File.Exists(libraries[l].Final)) {
                            WriteLine("nib: Error: Couldn't find library: {0}", libraries[l].Final);
                            return false;
                        }
                    }

                    if (!File.Exists(app.obj)) {
                        WriteLine("nib: Error: Couldn't find file: {0}", app.obj);
                        return false;
                    }
                    if (!File.Exists(app.super)) {
                        WriteLine("nib: Error: Couldn't find file: {0}", app.super);
                        return false;
                    }

                    // Find the newest library.
                    newest = update;
                    for (int l = 0; l < libraries.Length; l++) {
                        written = File.GetLastWriteTime(libraries[l].Final);
                        if (newest < written) {
                            newest = written;
                        }
                    }
                    written = File.GetLastWriteTime(app.obj);
                    if (newest < written) {
                        newest = written;
                    }
                    written = File.GetLastWriteTime(app.super);
                    if (newest < written) {
                        newest = written;
                    }

                    DateTime target = DateTime.MinValue;
                    if (File.Exists(app.x86)) {
                        target = File.GetLastWriteTime(app.x86);
                    }


                    if (newest >= target || doForceLinker) {
                        // Linking is necessary

                        // <option>
                        // /out:..\obj\Prototype.MarkSweep\Shell.x86
                        // /pdb:..\obj\Prototype.MarkSweep\Shell.x86.pdb
                        // ..\obj\Prototype.MarkSweep\Shell.obj
                        // ..\obj\Prototype.MarkSweep\Shell_superObj.obj
                        // <library>
                        StringBuilder sb = new StringBuilder(4096);
                        if (options != null) {
                            foreach (string arg in options.linker.parameters) {
                                sb.Append(" ");
                                sb.Append(arg);
                            }
                        }
                        if (app.linker != null) {
                            foreach (string arg in app.linker.parameters) {
                                sb.Append(" ");
                                sb.Append(arg);
                            }
                        }
                        sb.Append(" /machine:");
                        sb.Append(machine);
                        sb.Append(" /out:");
                        sb.Append(app.x86);
                        sb.Append(" /pdb:");
                        sb.Append(app.pdb);
                        sb.Append(" /map:");
                        sb.Append(app.map);
                        sb.Append(" ");
                        sb.Append(app.obj);
                        sb.Append(" ");
                        sb.Append(app.super);
                        for (int l = 0; l < libraries.Length; l++) {
                            sb.Append(" ");
                            sb.Append(libraries[l].Final);
                        }
                        String args = sb.ToString();

#if !SINGULARITY
                        if (log != null) {
                            log.WriteLine("#{0} {1}", "link.exe", args);
                        }
                        if (!RunProgram(app.Name, "link.exe", args, null)) {
                            return false;
                        }
#else
                        if (log != null) {
                            log.WriteLine("#{0} {1}", "link", args);
                        }
                        if (!RunProgram(app.Name, "link", args, null, outputMux)) {
                            return false;
                        }
#endif

    #if CALL_MKNAME
                        args = "/u " + app.x86;
                        if (log != null) {
                            log.WriteLine("#{0} {1}", "mkname.exe", args);
                        }
#if !SINGULARITY
                        if (!RunProgram(app.Name, "mkname.exe", args, null)) {
#else
                        if (!RunProgram(app.Name, "mkname.exe", args, null, PipeMultiplexer! outputMux)) {
#endif
                            return false;
                        }
    #endif
                    }
                    else {
                        if (Verbose) {
                            WriteLine("nib: Linker unneeded: {0}", app.x86);
                        }
                    }
                }

                if (doManifest) {
                    // Find the newest library.
                    newest = File.GetLastWriteTime(applicationFile);
                    if (newest < update) {
                        newest = update;
                    }
                    DateTime target = DateTime.MinValue;
                    if (File.Exists(app.xman)) {
                        target = File.GetLastWriteTime(app.xman);
                    }

                    if (newest >= target || doForceManifest) {
                        //////////////////////////////////////////////////////////
                        // Update the installed manifest and write it out.
                        //
                        if (!InstallApplicationManifest()) {
                            return false;
                        }
#if SINGULARITY
                        Console.WriteLine(" manifest ->{0}",app.xman);
                        FileStream fs =new FileStream("/fs/temp/outfile", FileMode.Create,
                            FileAccess.Write, FileShare.None);
                        XmlWriter writer = new XmlWriter(fs, System.Text.Encoding.UTF8);
                        //writer.Formatting = Formatting.Indented;
                        NibHelper.SaveXmlDocument(writer, appManifest);
                        writer.Close();
#else
                        XmlTextWriter writer = new XmlTextWriter(app.xman,
                                                                 System.Text.Encoding.UTF8);
                        writer.Formatting = Formatting.Indented;
                        appManifest.Save(writer);
                        writer.Close();
#endif
                    }
                    else {
                        if (Verbose) {
                            WriteLine("nib: Manifest generation unneeded: {0}", app.xman);
                        }
                    }
                }
            }
            finally {
                if (log != null) {
                    TimeSpan elapsed = DateTime.Now - logBegin;
                    log.WriteLine("# {0} seconds elapsed.", elapsed.TotalSeconds);
                    log.Close();
                }
                appManifest = null;
            }
            return true;
        }

        #region Parallel Build Support

        // Per-worker state
        string _PrintPrefix = "";
        IntPtr _ChildProcessorAffinityMask;

        // Global state
        static bool _ShowDots = true;
        readonly static Object _PrintLock = new Object();
        static IntPtr _JobHandle;

        void WriteLine(string line)
        {
            if (_BuildingParallel)
                Monitor.Enter(_PrintLock);

            if (_PrintPrefix != null && _PrintPrefix.Length != 0)
                Console.WriteLine(_PrintPrefix + line);
            else {
#if SINGULARITY
                DebugStub.WriteLine(line);
#endif
                Console.WriteLine(line);
            }
            if (_BuildingParallel)
                Monitor.Exit(_PrintLock);
        }

        void WriteLine(string format, params object[] args)
        {
            WriteLine(String.Format(format, args));
        }


        class QueueJob
        {
            public string Filename;
            public bool Result;
        }

        // Contains
        static readonly Object _JobQueueMonitor = new Object();
        static readonly Queue _WaitingJobQueue = new Queue();
        static readonly ArrayList _FinishedJobList = new ArrayList();
        static bool _BuildingParallel;

        class WorkerThread
        {
            public string DebugPrefix = "";

            // zero-based processor index
            public int Id;
            public Thread Thread;

            public TimeSpan KernelTime;
            public TimeSpan UserTime;


            public WorkerThread(NativeImageBuilder nib, int id)
            {
                this.Id = id;
                this.nib = nib;
                this.DebugPrefix = String.Format("{0}> ", id);
                this._printPrefix = String.Format("{0}> ", id);
            }

            string _printPrefix;

            NativeImageBuilder nib;

            void DebugLine(string line)
            {
#if !SINGULARITY
                Debug.WriteLine(DebugPrefix + line);
#else
                DebugStub.WriteLine(DebugPrefix + line);
#endif
            }

            void DebugLine(string format, params object[] args)
            {
                DebugLine(String.Format(format, args));
            }

            void WriteLine(string line)
            {
                Console.WriteLine(DebugPrefix + line);
            }

            void WriteLine(string format, params object[] args)
            {
                WriteLine(String.Format(format, args));
            }

            public void ThreadMain()
            {
                try {
                    RunQueuedJobs();
                }
                catch (Exception e) {
                    ShowException(e);
                    Environment.Exit(1);
                }
            }

            private void RunQueuedJobs()
            {
                DebugLine("worker thread started");
#if SINGULARITY
                // build a pipe mux
                // Use a mux to splice our own output together with the child
                // processes we will run.
                // Make ourselves a new output pipe
                UnicodePipeContract.Exp! newOutputExp;
                UnicodePipeContract.Imp! newOutputImp;
                UnicodePipeContract.NewChannel(out newOutputImp, out newOutputExp);

                // Retrieve our real stdOut and start using the one we just created
                // instead
                UnicodePipeContract.Imp stdOut = ConsoleOutput.Swap(newOutputImp);
                PipeMultiplexer! outputMux = PipeMultiplexer.Start(stdOut, newOutputExp);
#endif
                for (;;) {
                    QueueJob job;

                    lock (_JobQueueMonitor)
                    {
                        if (_WaitingJobQueue.Count == 0) {
                            DebugLine("no more jobs in queue -- exiting");
                            break;
                        }

                        job = (QueueJob)_WaitingJobQueue.Dequeue();

                    }

                    // Execute the job.

                    DebugLine("starting job - " + job.Filename);
                    job.Result = false;
                    try {
#if !SINGULARITY
                        job.Result = nib.BuildApplication(job.Filename);
#else
                        job.Result = nib.BuildApplication(job.Filename, outputMux);
#endif
                    }
                    catch (Exception e) {
                        Console.WriteLine("Failed building {0}\n", job.Filename);
                        throw e;
                    }

                    DebugLine("job finished - " + job.Filename);

                    if (!job.Result)
                        DebugLine("job FAILED - " + job.Filename);

                    lock (_JobQueueMonitor)
                    {
                        _FinishedJobList.Add(job);
                    }
                }
#if !SINGULARITY
                long creationTime;
                long exitTime;
                long kernelTime;
                long userTime;
                if (Kernel32.GetThreadTimes(Kernel32.GetCurrentThread(), out creationTime, out exitTime, out kernelTime, out userTime)) {
                    this.KernelTime = new TimeSpan(kernelTime);
                    this.UserTime = new TimeSpan(userTime);
                }
                else {
                    WriteLine("Failed to get thread times!  error = " + Marshal.GetLastWin32Error());
                }
#else
                    WriteLine("need to fix up thread times");
                    outputMux.Dispose();
#endif
            }
        }

        static void ShowException(Exception chain)
        {
            for (Exception ex = chain; ex != null; ex = ex.InnerException) {
                Console.WriteLine("{0}: {1}", ex.GetType().FullName, ex.Message);
            }
        }

        #endregion


        // Print the correct command line args for the program
        static void Usage()
        {
            Console.WriteLine(
                "Usage:\n" +
                "    nib /cache:<path> [options] app.manifest [app1.manifest ...]\n" +
                "Options:\n" +
                "    /apps:<file>        - File containing list of app.manifests.\n" +
                "    /bartok:<file>      - Specify Bartok to use.\n" +
                "    /c                  - Code generation only, don't link.\n" +
                "    /clean              - Clean up output only.\n" +
                "    /cache:<path>       - Root of file cache.\n" +
                "    /force              - Force generation even if output files are newer.\n" +
                "    /link               - Link only, don't compile.\n" +
                "    /libcache:<path>    - Root of Architecture-dependent cache.\n" +
                "    /machine:<arch>     - Binary target architecture.\n" +
                "    /manifest           - Generate reified manifest only.\n" +
                "    /native:<path>      - Root of native files.\n" +
                "    /options:<file>     - Options manifest file.\n" +
                "    /temp:<path>        - Temporary directory.\n" +
                "    /v                  - Verbose logging output.\n" +
                "    /par                - Disable parallel builds.\n" +
                "    /par:<nn>           - Set to use <nn> parallel processors.\n" +
                "Summary:\n" +
                "    Create a native image for each application manifest.\n" +
                "");
        }

        static bool ReadInfilesFromFile(string filePath, ArrayList infiles)
        {
            try {
                using (StreamReader sr = new StreamReader(filePath)) {
                    string line;
                    while ((line = sr.ReadLine()) != null) {
                        infiles.Add(line);
                    }
                }
                return true;
            }
            catch (FileNotFoundException) {
                Console.WriteLine("File not found {0}\n", filePath);
                return false;
            }
        }

        static int Main(string[] args)
        {
            ArrayList infiles = new ArrayList();
            string optionsFile = null;
            string cacheDirectory = null;
            string libCacheDirectory = null;
            string tempDirectory = null;
            string nativeDirectory = null;
            DateTime beg = DateTime.Now;

#if !SINGULARITY
            string bartok = Environment.GetEnvironmentVariable("BARTOK");
            if (bartok != null) {
                Bartok = bartok;
            }
#endif

#if !SINGULARITY
            int maxWorkerThreads = Kernel32.GetProcessorCount();
#else
            int maxWorkerThreads = 1;
#endif
            int numWorkerThreads = maxWorkerThreads;

            // Temporaries for command-line parsing
            bool needHelp = (args.Length == 0);

#if !SINGULARITY
            for (int i = 0; i < args.Length && !needHelp; i++) {
#else
            for (int i = 1; i < args.Length && !needHelp; i++) {
#endif
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

#if SINGULARITY
                        case "app":
                            if (value != null) {
                                 infiles.Add(value);
                            }
                            else {
                                badArg = true;
                            }
                            break;
#endif
                        case "apps":
                            if (value != null) {
#if !SINGULARITY
                                badArg = !ReadInfilesFromFile(value, infiles);
#else
                                Console.WriteLine("'apps' not supported");
                                needHelp = true;
                                break;
#endif
                            }
                            else {
                                badArg = true;
                            }
                            break;

                        case "bartok":
                            if (value != null) {
                                Bartok = value.Trim(new char[] {'"'});
                                if (!File.Exists(Bartok)) {
                                    Console.WriteLine("Bartok not found: {0}",
                                                      Bartok);
                                    badArg = true;
                                }
                            }
                            else {
                                badArg = true;
                            }
                            break;

                        case "c":
                            DoCodegen = true;
                            DoLinker = false;
                            DoManifest = false;
                            break;

                        case "clean":
                            DoClean = true;
                            break;

                        case "cache":
                            if (value != null) {
                                cacheDirectory = value.TrimEnd('/', '\\') + "\\";
                            }
                            else {
                                badArg = true;
                            }
                            break;

                        case "force":
                            ForceCodegen = true;
                            ForceLinker = true;
                            ForceManifest = true;
                            break;

                        case "libcache":
                            if (value != null) {
#if !SINGULARITY
                                libCacheDirectory = value.TrimEnd('/', '\\') + "\\";
#else
                                libCacheDirectory = value + "/";
#endif
                            }
                            else {
                                badArg = true;
                            }
                            break;

                        case "link":
                            DoCodegen = false;
                            DoLinker = true;
                            DoManifest = true;
                            break;

                        case "manifest":
                            DoCodegen = false;
                            DoLinker = false;
                            DoManifest = true;
                            break;

                        case "log":
#if !SINGULARITY
                            cacheDirectory = value.TrimEnd('/', '\\') + "\\";
#else
                            cacheDirectory = value + "/";
#endif
                            break;

                        case "native":
                            if (value != null) {
#if  true //!SINGULARITY
                            nativeDirectory = value.TrimEnd('/', '\\') + "\\";
#else
                            nativeDirectory = value + "/";
#endif
                            }
                            else {
                                badArg = true;
                            }
                            break;

                        case "options":
                            if (value != null) {
                                optionsFile = value;
                            }
                            else {
                                badArg = true;
                            }
                            break;

                        case "temp":
                            if (value != null) {
#if  true //!SINGULARITY
                                tempDirectory = value.TrimEnd('/', '\\') + "\\";
#else
                                tempDirectory = value + "/";
#endif
                            }
                            else {
                                badArg = true;
                            }
                            break;

                        case "v":
                            Verbose = true;
                            break;

                        case "par":
                            if (value != null) {
                                bool force = false;
                                if (value.EndsWith("!")) {
                                    value = value.Substring(0, value.Length - 1);
                                    force = true;
                                }
                                int count;
                                try {
#if !SINGULARITY
                                    count = Convert.ToInt32(value);
#else
                                    count = Int32.Parse(value);
#endif
                                }
                                catch {
                                    badArg = true;
                                    break;
                                }
                                if (count < 1) {
                                    badArg = true;
                                    break;
                                }
                                if (count > maxWorkerThreads && !force) {
                                    Console.WriteLine("nib: Warning: Cannot use {0} worker threads; this machine only has {1} processors.", count, maxWorkerThreads);
                                    Console.WriteLine("nib: Using {0} worker threads.", maxWorkerThreads);
                                    count = maxWorkerThreads;
                                }
                                else {
                                    numWorkerThreads = count;
                                }
                            }
                            else {
                                // Parallel builds disabled.
                                numWorkerThreads = 1;
                            }
                            break;

                        case "machine":
                            if (value != null) {
                                machine = value;
                            }
                            else {
                                badArg = true;
                            }
                            break;

                        default:
                            Console.WriteLine("nib: Unknown command line argument: {0}", arg);
                            needHelp = true;
                            break;
                    }
                    if (badArg) {
                        Console.WriteLine("nib: Invalid command line argument: {0}", arg);
                        needHelp = true;
                    }
                }
                else {
                    infiles.Add(arg);
                }
            }

            if (machine == null) {
                Console.WriteLine("No machine architecture specified. Terminating.");
                Usage();
                return -1;
            }

            if (infiles.Count == 0 ||
                cacheDirectory == null || tempDirectory == null) {

                Console.WriteLine("nib: Arguments missing from command line.");
                needHelp = true;
            }

            if (needHelp) {
                Usage();
                return 1;
            }

            // check all input files
            foreach (string filename in infiles) {
                if (!File.Exists(filename)) {
                    Console.WriteLine("nib: Error: Application manifest '{0}' not found.",
                                      filename);
                    return 2;
                }
            }

            // check all input files
            if (optionsFile != null && !File.Exists(optionsFile)) {
                Console.WriteLine("nib: Error: Options manifest '{0}' not found.",
                                  optionsFile);
                return 2;
            }
            if (!Directory.Exists(cacheDirectory)) {
                Console.WriteLine("nib: Error: Cache directory '{0}' not found.",
                                  cacheDirectory);
                return 2;
            }
            if (!Directory.Exists(tempDirectory)) {
                Console.WriteLine("nib: Error: Temp directory '{0}' not found.",
                                  tempDirectory);
                return 2;
            }

            foreach (string filename in infiles) {
                QueueJob job = new QueueJob();
                job.Filename = filename;
                _WaitingJobQueue.Enqueue(job);
            }

#if !SINGULARITY
            if (Kernel32.IsCurrentProcessInJob()) {
                Console.WriteLine("nib: Current process is already in a job.  Accounting disabled.");
                _JobHandle = IntPtr.Zero;
            }
            else {
                try {
                    IntPtr job = Kernel32.CreateJobObject();
                    _JobHandle = job;
                }
                catch (Exception ex)
                {
                    ShowException(ex);
                    _JobHandle = IntPtr.Zero;
                }
            }
#else
        Console.WriteLine(" call to IsCurrentProcessJob");
#endif
            int worker_count = numWorkerThreads;

            if (worker_count > 1) {
                Console.WriteLine("nib: Using {0} processors.", worker_count);

                _BuildingParallel = true;
                _ShowDots = false;
            }
            else {
                Console.WriteLine("nib: Using 1 processor.");
                _BuildingParallel = false;
                _ShowDots = true;
            }

            WorkerThread[] workers = new WorkerThread[worker_count];
            for (int i = 0; i < worker_count; i++) {
                NativeImageBuilder nib = new NativeImageBuilder(cacheDirectory,
                                                                libCacheDirectory,
                                                                nativeDirectory,
                                                                tempDirectory);

                if (optionsFile != null && !nib.LoadOptionsManifest(optionsFile)) {
                    return 3;
                }

                if (_BuildingParallel) {
                    nib._PrintPrefix = String.Format("{0}> ", i);
                    nib._ChildProcessorAffinityMask = (IntPtr)(1 << i);
                }
                else {
                    nib._PrintPrefix = "";
                    nib._ChildProcessorAffinityMask = IntPtr.Zero;
                }

                WorkerThread worker = new WorkerThread(nib, i);
                Thread thread = new Thread(new ThreadStart(worker.ThreadMain));
#if !SINGULARITY
                thread.IsBackground = true;
#endif
                worker.Thread = thread;

                workers[i] = worker;
            }

            // Console.WriteLine("nib: starting worker threads...");

            for (int i = 0; i < worker_count; i++) {
                workers[i].Thread.Start();
            }

            // Console.WriteLine("nib: waiting for all worker threads...");

            for (int i = 0; i < worker_count; i++) {
                Thread thread = workers[i].Thread;
                thread.Join();
            }

            // Console.WriteLine("nib: all worker threads finished.");
            Console.WriteLine();

            TimeSpan elapsed = DateTime.Now - beg;

            // const string times_format_uk = "nib: {0,-30} : {1,-16} total = {2,-16} user + {3,-16} kernel";
            const string times_format    = "nib: {0,-32} : {1}";

#if !SINGULARITY
            if (_JobHandle != IntPtr.Zero) {
                try {
                    JOBOBJECT_BASIC_ACCOUNTING_INFORMATION job_info;
                    job_info = Kernel32.QueryJobObjectBasicAccountingInformation(_JobHandle);

                    TimeSpan total_job_time = job_info.TotalUserTime + job_info.TotalKernelTime;

                    Console.WriteLine(
                        times_format,
                        "CPU time consumed by jobs",
                        total_job_time);

                    Console.WriteLine(
                        times_format,
                        "    in user mode",
                        job_info.TotalUserTime);

                    Console.WriteLine(
                        times_format,
                        "    in kernel mode",
                        job_info.TotalKernelTime);

                    Console.WriteLine(
                        times_format,
                        "Elapsed time (wall time)",
                        elapsed);

                    Console.WriteLine("nib:");
                    if (elapsed.TotalMilliseconds > 0) {
                        double elapsed_ms = elapsed.TotalMilliseconds;
                        double total_job_ms = total_job_time.TotalMilliseconds;
                        double throughput_ratio = total_job_ms / elapsed_ms;
                        double throughput_percent = Math.Round(throughput_ratio * 100.0);
                        Console.WriteLine("nib:      Parallel throughput ratio: {0}%", throughput_percent);
                    }
                    else {
                        Console.WriteLine("nib:      Too little time elapsed to compute ratio.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAILED to query job info!");
                    ShowException(ex);
                }
            }
            else {
                Console.WriteLine("nib: Child process times not available.");
            }
#else
                Console.WriteLine("nib: Child process times not available.");
                Console.WriteLine(
                        times_format,
                        "Elapsed time (wall time)",
                        elapsed);
#endif
            int succeeded = 0;
            int failed = 0;
            foreach (QueueJob job in _FinishedJobList) {
                if (job.Result)
                    succeeded++;
                else
                    failed++;
            }

            if (_FinishedJobList.Count != infiles.Count) {
                // This is defensive check.  Previously worker
                // threads could raise and exception and leave nib
                // running to completion without placing job on
                // _FinishedJobList.
                failed += infiles.Count - _FinishedJobList.Count;
            }
#if !SINGULARITY
            if (_JobHandle != IntPtr.Zero)
                Kernel32.CloseHandle(_JobHandle);
#endif
            Console.WriteLine("nib:      {0} job(s) succeeded, {1} job(s) failed", succeeded, failed);

            return (failed != 0) ? 4 : 0;
        }
    }
}
