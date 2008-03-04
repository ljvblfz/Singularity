////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Lisp.cs
//
//  Note:   Simple Singularity test program.
//
using System;
using ProtoLisp;

using Microsoft.Singularity.Channels;
using Microsoft.Contracts;
using Microsoft.SingSharp.Reflection;
using Microsoft.Singularity.Applications;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Configuration;
[assembly: Transform(typeof(ApplicationResourceTransform))]

namespace Microsoft.Singularity.Applications {
    [ConsoleCategory(DefaultAction=true)]
    internal class Parameters {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        [StringArrayParameter( "args", HelpMessage="arg bucket")]
        internal string[] args;
        
        reflective internal Parameters();

        internal int AppMain() {
            return Lisp.AppMain(this);
        }
    }

    public class Lisp
    {
        // Returns true if the arguments were valid, false for illegal args
        private static bool GetLispArgs(string[]! args, out bool helpArg, out bool traceArg, out string exprArg)
        {
            traceArg = false;
            helpArg = false;
            exprArg = "";

            for (int i = 0; i < args.Length; ++i)
            {
                string! args_i = (!)args[i];

                if ((args_i.Length > 0) && (args_i[0] == '/'))
                {
                    string arg = args_i.Substring(1);

                    if (arg.Equals("trace"))
                    {
                        traceArg = true;
                        continue;
                    }
                    else if (arg.Equals("help") || arg.Equals("?"))
                    {
                        helpArg = true;
                        continue;
                    }
                }
                else
                {
                    // A non-switch must be the last argument
                    if (i == (args.Length - 1))
                    {
                        exprArg = args_i;
                        continue;
                    }
                }

                // If we fall out, we have illegal arguments.
                return false;
            }

            // If we didn't abort, everything was OK
            return true;
        }

        internal static int AppMain(Parameters! config)
        {
            bool trace, help;
            string expr;
            string[] args = config.args; 

            
            if (args== null || args.Length <= 0)
            {
                Console.WriteLine("ProtoLisp interpreter.");
                Console.WriteLine("Usage: lisp [flags] <lispexpression>");
                Console.WriteLine("Run \"lisp /help\" or \"lisp /?\" for more details");
                return 0;
            }

            bool validUsage = GetLispArgs(args, out help, out trace, out expr);

            if ((!validUsage) || (help))
            {
                Console.WriteLine("ProtoLisp is an extremely basic Lisp-like interpreter.\n");
                Console.WriteLine("Usage: lisp [flags] LISPEXPRESSION\n");
                Console.WriteLine("Flags:");
                Console.WriteLine("  /? or /help : Print this message");
                Console.WriteLine("  /trace : Print verbose traces from the interpreter\n");
                Console.WriteLine("The following classic Lisp primitives are available:\n");
                Console.WriteLine("    atom, eq, car, cdr, cons, cond\n");
                Console.WriteLine("In addition,");
                Console.WriteLine("  - Variables are lexically scoped, not dynamically scoped.");
                Console.WriteLine("  - (define X Y) binds the name \"X\" to the value \"Y\"");
                Console.WriteLine("    in the global context");
                Console.WriteLine("  - (lambda (ARGLIST) (BODYEXPRESSION)) creates a \"closure\" by");
                Console.WriteLine("    capturing the ambient lexical scope.");
                Console.WriteLine("  - (defun NAME (ARGLIST) (BODYEXPR) is sugar for");
                Console.WriteLine("    (define NAME (lambda (ARGLIST) (BODYEXPR)))\n");
                Console.WriteLine("  - The arithmetic operators *, +, -, / are available. Math is");
                Console.WriteLine("    performed with integers.");
            }
            else
            {
                ProtoLisp.Engine engine = new ProtoLisp.Engine();
                System.Console.Write(engine.EvalAll(expr, true, trace));
            }

            return 0;
        }
    }
}
