///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Substitute.cs
//
//  Note:
//

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Microsoft.Singularity.Tools
{
    class Substitute
    {
        private static void Apply(TextReader input,
                                  TextWriter output,
                                  string     inPattern,
                                  string     outPattern)
        {
            string line;
            while (null != (line = input.ReadLine())) {
                line = Regex.Replace(line, inPattern, outPattern);
                output.WriteLine(line);
            }
        }

        public static int Main(string[] args)
        {
            switch (args.Length) {
                case 2:
                    Apply(Console.In, Console.Out, args[0], args[1]);
                    return 0;

                case 3:
                    using (StreamReader sr = new StreamReader(args[2])) {
                        Apply(sr, Console.Out, args[0], args[1]);
                    }
                    return 0;

                case 4:
                    using (StreamReader sr = new StreamReader(args[2])) {
                        if (sr.Peek() >= 0) {
                            using (StreamWriter sw = new StreamWriter(args[3], false, sr.CurrentEncoding)) {
                                Apply(sr, sw, args[0], args[1]);
                            }
                        }
                    }
                    return 0;

                default:
                    Console.WriteLine("Usage: replace <string1> <string2> [<Input file> [<OutputFile>]]");
                    return -1;
            }
        }
    }
}
