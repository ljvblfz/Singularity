///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

namespace Microsoft.Singularity.Xml
{
    using System;
    using System.Collections;
    using System.Globalization;

    /// <summary>
    /// Summary description for XmlNode.
    /// </summary>
    public class XmlNode
    {
        private string name;
        private int depth;
        private string text;
        private Hashtable attributes;
        private ArrayList children;

        public XmlNode(string name, int depth)
        {
            this.name = name;
            this.depth = depth;
            attributes = new Hashtable();
            children = new ArrayList();
            this.text = "";
        }

        public ArrayList GetNamedChildren(string name)
        {
            ArrayList result = new ArrayList();
            foreach (XmlNode childNode in children) {
                if (childNode.Name == name) {
                    result.Add(childNode);
                }
            }
            return result;
        }

        public XmlNode GetNestedChild(String[] childNames)
        {
            XmlNode node = this;

            for (int i = 0; node != null && i < childNames.Length; i++) {
                node = node.GetNestedChild(childNames[i]);
            }
            return node;
        }

        public XmlNode GetNestedChild(String childName)
        {
            foreach (XmlNode childNode in children) {
                if (childNode.Name == childName) {
                    return childNode;
                }
            }
            return null;
        }

        public int Depth
        {
            get { return depth; }
        }

        public string Name
        {
            get { return name; }
        }

        public void AddChild(XmlNode node)
        {
            children.Add(node);
            return;
        }

        public ArrayList Children
        {
            get { return new ArrayList(children); }
        }

        public void AddText(string text)
        {
            this.text += text;
            return;
        }

        public string Text
        {
            get { return text; }
        }

        public string this[string attributeName]
        {
            get
            {
                if (!attributes.ContainsKey(attributeName))
                {
                    return null;
                }
                else
                {
                    return (string)attributes[attributeName];
                }
            }
            set
            {
                attributes[attributeName] = value;
            }
        }

        //
        // Safe access to attributes:
        //      since the kernel is going to use this object, we should
        //      push the error-checking into the object instead of risking
        //      the kernel forgetting to error check in some obscure method
        //

        public string GetAttribute(string attributeName, string defaultValue)
        {
            if (!attributes.ContainsKey(attributeName)) {
                return defaultValue;
            }
            else {
                return (string)attributes[attributeName];
            }
        }

        public bool GetAttribute(string attributeName, bool defaultValue)
        {
            if (!attributes.ContainsKey(attributeName)) {
                return defaultValue;
            }
            else {
                return (string)attributes[attributeName] ==
                    System.Boolean.TrueString;
            }
        }

        public int GetAttribute(string attributeName, int defaultValue)
        {
            if (!attributes.ContainsKey(attributeName)) {
                return defaultValue;
            }
            else {
                string num = (string)attributes[attributeName];
                if (num.StartsWith("0x") || num.StartsWith("0X")) {
                    return System.Int32.Parse(num, NumberStyles.AllowHexSpecifier);
                }
                else {
                    return System.Int32.Parse(num);
                }
            }
        }

        [CLSCompliant(false)]
        public UIntPtr GetAttributeAsUIntPtr(string attributeName, UIntPtr defaultValue)
        {
            if (!attributes.ContainsKey(attributeName)) {
                return defaultValue;
            }
            else {
                string num = (string)attributes[attributeName];
                if (num.StartsWith("0x") || num.StartsWith("0X")) {
                    return System.UIntPtr.Parse(num, NumberStyles.AllowHexSpecifier);
                }
                else {
                    return System.UIntPtr.Parse(num);
                }
            }
        }

        public bool HasAttribute(string attributeName)
        {
            return attributes.ContainsKey(attributeName);
        }

        public string GetAttributes()
        {
            string list = "";

            foreach (DictionaryEntry entry in attributes) {
                list = list + " " + entry.Key.ToString() + "=\"" + entry.Value.ToString() + "\"";
            }
            return list;
        }
    }
}
