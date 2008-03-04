////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   DistroBuilder.cs
//
//  Note:   Main entry point for DistroBuilder tool, used to collect
//          application metadata into a manifest.
//
using System;
using System.IO;
using System.Xml;
using System.Collections;
using System.Security.Cryptography;

public class DistroBuilder
{
    //
    // Fields in the distro build class
    //

    XmlNode systemPolicy; // the external xml policy

    // manifest and well-defined nodes
    XmlDocument systemManifest;
    XmlNode manifestRoot;
    XmlNode drivers;

    // internal node that we build and add to the manifest
    XmlNode fileList;

    // all files in the distro (full names, plus the separate directory prefix)
    string distroDirectoryName;
    SortedList applicationFiles;
    SortedList manifestFiles;
    SortedList otherFiles;

    // key filenames
    string outFilePath; // system manifest path+name
    string outFileName; // just the name of the manifest
    string iniFileName; // ini file

    // const name of the kernel file
    const string kernelFileName = "kernel.dmp";

    // set up an object that can build the distribution
    public DistroBuilder(string policyFile, string outputFile, string iniFile,
                         string distroDirectoryName,
                         string distroDescriptionFileName)
    {
        // copy output file names.
        outFilePath = outputFile;
        iniFileName = iniFile;

        // load the system policy.
        XmlDocument policyDoc = new XmlDocument();
        policyDoc.Load(policyFile);
        systemPolicy = GetChild(policyDoc, "distribution");

        // set up Xml pieces.
        systemManifest = new XmlDocument();
        manifestRoot = AddElement(systemManifest, "system");

        fileList = AddElement(null, "files");
        drivers = AddElement(null, "drivers");

        // now set up the lists of files in the distro:
        applicationFiles = new SortedList();
        manifestFiles = new SortedList();
        otherFiles = new SortedList();

        FileInfo allFiles = new FileInfo(distroDescriptionFileName);
        StreamReader stream = allFiles.OpenText();
        string s = stream.ReadLine();
        while (s != null) {
            if (s.EndsWith(".x86")) {
                applicationFiles.Add(s, s);
            }
            else if (s.EndsWith(".manifest")) {
                manifestFiles.Add(s, s);
            }
            else {
                otherFiles.Add(s, s);
            }
            s = stream.ReadLine();
        }
        stream.Close();

        // add the output file to the list
        string [] a = outFilePath.Split('\\');
        outFileName = a[a.Length-1];

        FileInfo outFile = new FileInfo(outFilePath);
        outFilePath = outFile.FullName;
        if (!otherFiles.Contains(outFilePath)) {
            otherFiles.Add(outFilePath, outFilePath);
        }

        // now get the fully path for the distro directory name
        FileInfo directoryInfo = new FileInfo(distroDirectoryName);
        this.distroDirectoryName = directoryInfo.FullName;
    }

    //////////////////////////////////////////////// Methods to help with XML.
    //
    private XmlNode AddElement(XmlNode parent, string name)
    {
        XmlNode element = systemManifest.CreateNode(XmlNodeType.Element, name, "");
        if (parent != null) {
            parent.AppendChild(element);
        }
        return element;
    }

    private void AddAttribute(XmlNode node, string name, string value)
    {
        XmlAttribute attr = systemManifest.CreateAttribute(name);
        attr.Value = value;
        node.Attributes.Append(attr);
    }

    private XmlNode AddImported(XmlNode parent, XmlNode root, bool deep)
    {
        XmlNode element = systemManifest.ImportNode(root, true);
        if (parent != null) {
            parent.AppendChild(element);
        }
        return element;
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
        return parent[name];
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
        XmlAttribute xa = node.Attributes[attrib];
        return xa != null ? xa.Value : null;
    }

    private static string GetAttribute(XmlNode node, string attrib, string value)
    {
        XmlAttribute xa = node.Attributes[attrib];
        return xa != null ? xa.Value : value;
    }

    private static int GetAttribute(XmlNode node, string attrib, int value)
    {
        XmlAttribute xa = node.Attributes[attrib];
        return xa != null ? Int32.Parse(xa.Value) : value;
    }

    //////////////////////////////////////////////////////////////////////////

    // return true if the node's name is in names.
    private bool TestNodeIs(XmlNode node, string [] names)
    {
        foreach (string name in names) {
            if (node.Name == name) {
                return true;
            }
        }
        return false;
    }


    // [TODO] this attribute create code should be in a function!
    // add a DriverCategory node into the <drivers>
    private void AddDriverNode(string name, string path,
                               string signature, string iclass,
                               ArrayList values)
    {
        // create the new node
        XmlNode driver = AddElement(null, "driver");

        // set its name, signature, and path
        AddAttribute(driver, "name", name);
        AddAttribute(driver, "signature", signature);
        AddAttribute(driver, "path", path);
        if (iclass != "") {
            AddAttribute(driver, "class", iclass);
        }

        // add any enumerates via copy.
        foreach (XmlNode node in values) {
            driver.AppendChild(node.CloneNode(true));
        }

        // and insert this node into the registry:
        drivers.AppendChild(driver);
    }

    // given a driverCategory that potentially has many device signatures, split
    // it by device and create an entry in the drivers for each device
    private void CreateRegistryEntriesFromCategory(XmlNode category,
                                                   string name,
                                                   string path)
    {
        ArrayList values = new ArrayList();

        // first parse the entries in the driver category to fill out the
        // Endpoint, FixedHardware, and DynamicHardware sets
        string iclass = GetAttribute(category, "class", "");

        // now populate these with children based on a few basic matching rules
        foreach (XmlNode node in category.ChildNodes) {
            if (node.Name == "enumerates" ||
                node.Name == "endpoints" ||
                node.Name == "fixedHardware" ||
                node.Name == "dynamicHardware" ||
                node.Name == "configs") {
                values.Add(AddImported(null, node, true));
            }
            else if (node.Name != "device") {
                //
            }
        }

        // get every device signature in this driver category
        XmlNode[] signatureTags = GetChildren(category, "device");

        // there's a chance (for example, with the Hal) that there are no
        // signatures.  In this case, we process the category once, with a null
        // signature
        if (signatureTags.Length == 0) {
            AddDriverNode(name, path, "", iclass, values);
        }
        else {
            foreach (XmlNode signature in signatureTags) {
                string value = GetAttribute(signature, "signature", "");
                AddDriverNode(name, path, value, iclass, values);
            }
        }
    }

    // This is the main method for creating a driver registry.
    public void CreateDriverRegistry()
    {
        foreach (DictionaryEntry de in manifestFiles) {
            string filename = (string) de.Key;
            if (filename.EndsWith(".manifest")) {
                // get the manifest as an Xml document
                XmlDocument manifestDoc = new XmlDocument();
                manifestDoc.Load(filename);

                XmlNode application = GetChild(manifestDoc, "application");

                // Read the name and path from the manifest
                string name = GetAttribute(application, "name");

                // Console.WriteLine("{0}:", filename);
                foreach (XmlNode process in GetChildren(application, "process")) {
                    string path = GetAttribute(process, "path", "");

                    foreach (XmlNode categories in GetChildren(process, "categories")) {
                        foreach (XmlNode category in GetChildren(categories, "category")) {
                            if (GetAttribute(category, "name") != "driver") {
                                continue;
                            }

                            string iclass = GetAttribute(category, "class", "");

                            // now process all DriverCategories
                            CreateRegistryEntriesFromCategory(category, name, path);
                        }
                    }
                }
            }
        }
    }

    private bool NamesMatch(string target, string prefix, bool isPrefix)
    {
        int len = isPrefix ? prefix.Length :
            (target.Length > prefix.Length ? target.Length : prefix.Length);

        return (String.Compare(target, 0, prefix, 0, len, true) == 0);
    }


    // Remove unwanted drivers from the rot using the policy.
    private void RemoveNodes(XmlNode root, XmlNode policy)
    {
        foreach (XmlNode rule in policy.ChildNodes) {
            string attrName = "";
            string attrVal = "";
            bool isPrefix = false;

            if (rule.Name == "driver") {
                attrName = "name";
            }
            else if (rule.Name == "device") {
                attrName = "signature";
                isPrefix = true;
            }
            else {
                continue;
            }
            ArrayList removals = new ArrayList();
            attrVal = GetAttribute(rule, attrName);
            foreach (XmlNode candidate in root.ChildNodes) {
                string target = GetAttribute(candidate, attrName);

                if (NamesMatch(target, attrVal, isPrefix)) {
                    removals.Add(candidate);
                }
            }
            foreach (XmlNode oldnode in removals) {
                root.RemoveChild(oldnode);
            }
        }
    }

    // in addition, we use the imperative mechanism to create the "follows"
    // ordering, which lets us specify a more total order on the initialization
    // of drivers
    private void ApplyOrdering(XmlNode root, XmlNode policy)
    {
        foreach (XmlNode rule in policy.ChildNodes) {
            string signature = GetAttribute(rule, "signature");
            string name = GetAttribute(rule, "name");
            foreach (XmlNode candidate in root.ChildNodes) {
                string candidatesignature = GetAttribute(candidate, "signature");
                if (candidatesignature.StartsWith(signature)) {
                    XmlNode tag = AddElement(candidate, rule.Name);
                    AddAttribute(tag, "name", name);
                }
            }
        }
    }

    // and this is the master method for applying the imperative policy
    private void ApplyPolicyToRegistry()
    {
        // get its driversConfig:
        XmlNode policy = GetChild(systemPolicy, "driverRegistryConfig");

        // go through each imperative command
        foreach (XmlNode child in policy.ChildNodes) {
            if (child.Name == "remove") {
                RemoveNodes(drivers, child);
            }
            else if (child.Name == "ordering") {
                ApplyOrdering(drivers, child);
            }
        }
    }

    // This prints the distribution manifest when all is done
    public void PrintManifestFile()
    {
        XmlTextWriter w = new XmlTextWriter(outFilePath,
                                            System.Text.Encoding.UTF8);
        w.Formatting = Formatting.Indented;
        systemManifest.Save(w);
        w.Close();
    }

    // [TODO] there ought to be some policy that decides what should
    //                  to in the distribution, but for now we just take as
    //                  input a list of files that have been built, and assume
    //                  it is the output of this alluded-to step

    // Every file in the distro must be added to a list, both so that the file
    // can be added to SingBoot.ini, and so that it can have a filename
    // associated with it in the namespace
    private void AddFileNode(string pathName, int index, bool visible,
                             int manifestIndex)
    {
        XmlNode node = AddElement(null, "file");

        // get values for all the attributes of this file:
        bool isExecutable = pathName.EndsWith(".x86");
        bool isManifest = pathName.EndsWith(".manifest");

        string [] nameSet = pathName.Split('\\');
        string shortName = nameSet[nameSet.Length - 1].ToLower();
        string distroName = pathName.Remove(0, distroDirectoryName.Length);
        distroName = distroName.Replace('\\', '/');

        // add the attributes:
        AddAttribute(node, "distroName", distroName);
        AddAttribute(node, "name", shortName);
        AddAttribute(node, "id", index.ToString());

        if (isExecutable) {
            AddAttribute(node, "executable", "true");
            if (manifestIndex > 2) {
                AddAttribute(node, "manifestId", manifestIndex.ToString());
            }
        }
        else if (isManifest) {
            AddAttribute(node, "manifest", "true");
        }

        // add the file to the master file list
        fileList.AppendChild(node);
    }

    // This calls the above method to populate the fileList with all files that
    // are to be included in the distribution
    private void BuildFileList()
    {
        int position = 2;

        // first of all, let's set the kernel.dmp and output manifest file
        // entries
        string kernelPath = null;
        foreach (DictionaryEntry de in otherFiles) {
            string filename = (string) de.Key;
            if (filename.EndsWith("\\" + kernelFileName)) {
                kernelPath = filename;
                break;
            }
        }
        AddFileNode(kernelPath, 0, false, -1);
        AddFileNode(outFilePath, 1, false, -1);

        // now let's build the file list:
        // get the policy on what goes in the bootscript
        XmlNode policy = GetDescendant(systemPolicy, new string[] {"bootScript",
                                                                   "folders"});
        foreach (XmlNode rule in policy.ChildNodes) {
            if (rule.Name != "folder") {
                continue;
            }
            string folder = GetAttribute(rule, "name");
            foreach (DictionaryEntry de in applicationFiles) {
                string applicationName = (string) de.Key;
                int substitutePosition = applicationName.LastIndexOf (".x86");
                string manifestName = applicationName.Substring(0,substitutePosition)
                                                             + ".manifest";
                string shortAppName =
                    applicationName.Replace(distroDirectoryName, "");
                if (shortAppName.StartsWith("\\" + folder)) {
                    if (manifestFiles.ContainsKey(manifestName)) {
                        AddFileNode(applicationName, position, true,
                                    ++position);
                        AddFileNode(manifestName, position, true, -1);
                    }
                    else {
                        AddFileNode(applicationName, position, true, -1);
                    }
                    position++;
                }
            }
            foreach (DictionaryEntry de in otherFiles) {
                string fileName = (string) de.Key;
                string shortFileName = fileName.Replace(distroDirectoryName,
                                                        "");
                if (shortFileName.StartsWith("\\" + folder) &&
                    !shortFileName.EndsWith(kernelFileName)) {
                    AddFileNode(fileName, position++, true, -1);
                }
            }
            // NB - we don't copy a manifest unless we've associated it with an
            // app
        }
    }

    // Given only a list of drivers, a list of files, and some policy, we need
    // to create a system manifest.  This is how we do it for now.
    public void InstallPolicies()
    {
        // build the file list that goes into the manifest
        BuildFileList();

        // set up the initConfig tag and append the file list to it
        XmlNode initConfig = GetChild(systemPolicy, "initConfig");
        XmlNode newInitConfig = systemManifest.ImportNode(initConfig, true);
        newInitConfig.AppendChild(fileList);

        // get the namingConventions
        XmlNode namingConventions = GetChild(systemPolicy, "namingConventions");
        XmlNode newNaming = systemManifest.ImportNode(namingConventions, true);

        // get process grouping data
        XmlNode processConfig = GetChild(systemPolicy, "processConfig");
        XmlNode newProcessConfig = null;
        if (processConfig != null) {
            newProcessConfig = systemManifest.ImportNode(processConfig, true);
        }

        // get service configurations
        XmlNode serviceConfig = GetChild(systemPolicy, "serviceConfig");
        XmlNode newServiceConfig = systemManifest.ImportNode(serviceConfig,
                                                             true);

        // apply the imperative policy to the Driver Registry (this prunes it)
        ApplyPolicyToRegistry();

        // add everything to the manifest
        manifestRoot.AppendChild(drivers);
        manifestRoot.AppendChild(newNaming);
        manifestRoot.AppendChild(newServiceConfig);
        if (newProcessConfig != null) {
            manifestRoot.AppendChild(newProcessConfig);
        }
        manifestRoot.AppendChild(newInitConfig);
    }

    // When everything is set, we still need the Singboot.ini
    // file.  This is how we make it:
    public void MakeIniFile(string distrodir)
    {
        StreamWriter stream = new StreamWriter(iniFileName, false);

        string ruler = "########################################";
        stream.WriteLine(ruler);
        stream.WriteLine("# SingBoot.ini");
        stream.WriteLine("# Build Date: {0}", System.DateTime.Now.ToString());
        stream.WriteLine(ruler);

        MD5 md5 = MD5.Create();

        foreach (XmlNode file in fileList) {
            string installedFileName = GetAttribute(file, "distroName");
            string originalFileName =
                distrodir + installedFileName.Replace("/", "\\");

            FileInfo fileInfo = new FileInfo(originalFileName);
            using (FileStream fs = fileInfo.OpenRead()) {
                byte [] hash = md5.ComputeHash(fs);
                stream.Write("Hash-MD5=");
                foreach (byte b in hash) {
                    stream.Write("{0:x2}", b);
                }
            }

            stream.WriteLine(" Size={0} Path={1}",
                             fileInfo.Length, installedFileName);
        }
        // NB Trailing ruler helps identify the end of file
        stream.WriteLine(ruler);
        stream.Close();
    }

    // Print the correct command line args for the program
    static void PrintUsage()
    {
        Console.WriteLine(
            "Usage:\n" +
            "    distrobuilder [options]\n" +
            "Options:\n" +
            "    /dir:<path>         - Set distribution root directory\n" +
            "    /out:<file>         - Set name of output system manifest.\n" +
            "    /policy:<file>      - Set input system policy file.\n" +
            "    /desc:<desc>        - Distribution description.\n" +
            "    /ini:<ini>          - Set output boot.ini file.\n" +
            "Summary:\n" +
            "    Builds a system manifest for a collection of application manifests and\n" +
            "    a file list..\n" +
            "");
    }

    // main entry point:
    static void Main(string[] args)
    {
        string outfile = null;
        string policyfile = null;
        string distrodir = null;
        string distrodesc = null;
        string inifile = null;

        // parse the cmdline
        foreach (string arg in args) {
            if (arg.StartsWith("/dir:")) {
                distrodir = arg.Remove(0,5);
            }
            else if (arg.StartsWith("/out:")) {
                outfile = arg.Remove(0,5);
            }
            else if (arg.StartsWith("/policy:")) {
                policyfile = arg.Remove(0,8);
            }
            else if (arg.StartsWith("/desc:")) {
                distrodesc = arg.Remove(0,6);
            }
            else if (arg.StartsWith("/ini:")) {
                inifile = arg.Remove(0,5);
            }
        }

        if (policyfile == null || outfile == null || distrodesc == null ||
            distrodir == null  || inifile == null) {
            Console.WriteLine("Error - Invalid Command Line");
            PrintUsage();
            return;
        }

        // check all input files
        if (!File.Exists(policyfile)) {
            Console.WriteLine("Error:  file '{0}' not found\n", policyfile);
            return;
        }
        if (!File.Exists(distrodesc)) {
            Console.WriteLine("Error:  file '{0}' not found\n", distrodesc);
            return;
        }
        if (!Directory.Exists(distrodir)) {
            Console.WriteLine("Error:  directory '{0}' not found\n", distrodir);
            return;
        }

        DistroBuilder p = new DistroBuilder(
            policyfile, outfile, inifile, distrodir, distrodesc);

        // first aggregate all drivers into a <DriverRegistry>
        p.CreateDriverRegistry();
        // now apply policy to the system config
        p.InstallPolicies();
        // then output the manifest
        p.PrintManifestFile();
        // lastly make the ini file
        p.MakeIniFile(distrodir);
    }
}
