////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   mktests.cs
//
//  Examine assembly metadata to compile a manifest of test code in a Singularity
//  distribution.
//
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

using Bartok.MSIL;

public class mktests
{
    private static void Usage()
    {
        Console.WriteLine("Usage:\n" +
                          "    mktests /out:<test_manifest_file> [assemblies]\n");
    }

    public static int Main(string[] args)
    {
        DateTime timeBegin = DateTime.Now;
        ArrayList infiles = new ArrayList();
        string outfile = null;

        if (args.Length == 0) {
            Usage();
            return 0;
        }

        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];

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
                    case "out" :
                        if (value != null) {
                            outfile = value;
                        } else {
                            badArg = true;
                        }
                        break;

                    default :
                        badArg = true;
                        break;
                }

                if (badArg) {
                    Console.WriteLine("Malformed argument: \"" + arg + "\"");
                    Usage();
                    return 1;
                }
            }
            else {
                // This is just an assembly name
                infiles.Add(arg);
            }
        }

        if (outfile == null) {
            Console.WriteLine("You must specify the output file.");
            Usage();
            return 1;
        }

        if (infiles.Count == 0) {
            Console.WriteLine("You must specify at least one input file.");
            Usage();
            return 1;
        }

        ProcessAssemblies(infiles, outfile);

        TimeSpan elapsed = DateTime.Now - timeBegin;
        Console.WriteLine("mktests: {0} seconds elapsed.", elapsed.TotalSeconds);

        return 0;
    }

    private static void ProcessAssemblies(ArrayList infiles, string outfile)
    {
        MetaDataResolver resolver = new MetaDataResolver(infiles, new ArrayList(), new DateTime(),
                                                         false, false);

        MetaDataResolver.ResolveCustomAttributes(
            new MetaDataResolver[]{resolver});

        XmlDocument outDoc = new XmlDocument();
        XmlNode root = outDoc.CreateNode(XmlNodeType.Element, "testManifest", "");
        outDoc.AppendChild(root);

        foreach (MetaData md in resolver.MetaDataList) {
            ProcessAssembly(md, outDoc);
        }

        // Write out our constructed XML
        XmlTextWriter writer = new XmlTextWriter(outfile,
                                                 System.Text.Encoding.UTF8);
        outDoc.Save(writer);
        writer.Close();
    }

    private static void ProcessAssembly(MetaData md,
                                        XmlDocument outDoc)
    {
        // Look for the annotation that tells us that this assembly is a stand-alone
        // test app.
        MetaDataAssembly mda = (MetaDataAssembly)md.Assemblies[0];

        foreach (MetaDataCustomAttribute attrib in md.CustomAttributes) {
            MetaDataObject parent = attrib.Parent;
            MetaDataAssembly assembly = parent as MetaDataAssembly;

            // Is this an attribute attached to an entire assembly?
            if (assembly != null) {
                Debug.Assert(assembly == mda);

                switch (attrib.Name) {
                    case "Microsoft.Singularity.UnitTest.TestAppAttribute" :
                        // This assembly is a stand-alone test app
                        string group = GetNamedArg(attrib, "TestGroup") as string;

                        if (group == null) {
                            group = "default";
                        }

                        XmlNode groupNode = GetGroupNode(outDoc, group);
                        Debug.Assert(groupNode != null);
                        XmlNode newEntry = AddElement(outDoc, groupNode, "standAloneApp");
                        AddAttribute(outDoc, newEntry, "name", assembly.Name);
                        break;
                }
            }
        }
    }

    private static Object GetNamedArg(MetaDataCustomAttribute attrib, string argName)
    {
        foreach (MetaDataCustomAttribute.NamedArg arg in attrib.NamedArgs) {
            if (arg.Name == argName) {
                return arg.Value;
            }
        }

        return null;
    }

    private static XmlNode GetGroupNode(XmlDocument doc, string groupName)
    {
        XmlNode groupNode = doc.SelectSingleNode("child::group[name=\"" + groupName + "\"]");

        if (groupNode == null) {
            // No existing node for this group; create one.
            groupNode = AddElement(doc, doc.DocumentElement, "group");
            AddAttribute(doc, groupNode, "name", groupName);
        }

        return groupNode;
    }

    private static XmlNode AddElement(XmlDocument outDoc, XmlNode parent, string name)
    {
        XmlNode element = outDoc.CreateNode(XmlNodeType.Element, name, "");
        parent.AppendChild(element);
        return element;
    }

    private static void AddAttribute(XmlDocument outDoc, XmlNode node, string name, string value)
    {
        XmlAttribute attr = outDoc.CreateAttribute(name);
        attr.Value = value;
        node.Attributes.Append(attr);
    }
}
