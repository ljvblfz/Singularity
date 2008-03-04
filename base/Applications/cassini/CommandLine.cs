//------------------------------------------------------------------------------
// <copyright company='Microsoft Corporation'>
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//   Information Contained Herein is Proprietary and Confidential.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.VisualStudio.WebServer {
    using System;
    using System.Collections;
    using System.Globalization;
    using Microsoft.Contracts;

    /// <summary>
    /// </summary>
    public sealed class CommandLine {

        private string[] _arguments;
        private IDictionary _options;
        private bool _showHelp;

        [NotDelayed]
        public CommandLine(string[]! args) {
            ArrayList argList = new ArrayList();

            for (int i = 0; i < args.Length; i++) {
                string! args_i = (!)args[i];
                char c = args_i[0];
                if ((c != '/') && (c != '-')) {
                    argList.Add(args_i);
                }
                else {
                    int index = args_i.IndexOf(':');
                    if (index == -1) {
                        string option = args_i.Substring(1);
                        if ((String.Compare(option, "help", true) == 0) ||
                            option.Equals("?")) {
                            _showHelp = true;
                        }
                        else {
                            Options[option] = String.Empty;
                        }
                    }
                    else {
                        Options[args_i.Substring(1, index - 1)] = args_i.Substring(index + 1);
                    }
                }
            }
            _arguments = (string[])argList.ToArray(typeof(string));
        }

        public string[] Arguments {
            get {
                return _arguments;
            }
        }

        public IDictionary/*!*/ Options {
            get {
                if (_options == null) {
                    _options = new Hashtable();
                }
                return _options;
            }
        }

        public bool ShowHelp {
            get {
                return _showHelp;
            }
        }
    }
}
