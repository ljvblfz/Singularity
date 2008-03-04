///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;

class NMakeDirs
{
    const string NMakeProgram = "nmake.exe";
    const string RunParallelProgram = "runparallel.exe";
    const string DrainMarker = "+DRAIN";

    static int Main(string[] args)
    {
        try
        {
            ArrayList dirs = new ArrayList();
            int dir_count = 0; // this count excludes drain markers

            bool parallel = false;

            string nmake_args = "/nologo ";

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.Length == 0)
                    continue;

                if (arg.StartsWith("/"))
                {
                    arg = arg.ToLower();
                    if (arg == "/nmake")
                    {
                        // The rest of the arguments are for NMAKE.
                        i++;
                        nmake_args = nmake_args + String.Join(" ", args, i, args.Length - i);
                        break;
                    }
                    else if (arg == "/p")
                    {
                        parallel = true;
                    }
                    else if (arg == "/?")
                    {
                        Usage();
                        return 0;
                    }
                    else
                    {
                        WriteLine("WARNING: Unrecognized switch: " + arg);
                    }
                }
                else
                {
                    dirs.Add(arg);
                    if (String.Compare(arg, DrainMarker, true) != 0)
                        dir_count++;
                }
            }

            if (dir_count == 0)
            {
                Usage();
                return 1;
            }

            if (parallel)
            {
                WriteLine("Building {0} dirs in parallel.", dir_count);

                using (Process process = new Process())
                {
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardInput = true;
                    process.StartInfo.FileName = RunParallelProgram;
                    process.StartInfo.Arguments = "/stdin";

                    process.Start();

                    TextWriter pipe = process.StandardInput;

                    foreach (string dir in dirs)
                    {
                        if (String.Compare(dir, DrainMarker, true) == 0)
                        {
                            pipe.WriteLine(dir);
                            continue;
                        }
                        string line = String.Format("cd \"{0}\" && " + NMakeProgram + " {1}", dir, nmake_args);
                        // WriteLine("    cmd - " + line);
                        pipe.WriteLine(line);
                    }

                    pipe.Close();

                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        WriteLine("RUNPARALLEL failed: " + process.ExitCode);
                        return process.ExitCode;
                    }

                    return 0;
                }
            }
            else
            {
                WriteLine("Building {0} dirs serially.", dir_count);

                string initial_directory = Environment.CurrentDirectory;

                foreach (string dir in dirs)
                {
                    if (String.Compare(dir, DrainMarker, true) == 0)
                        continue;

                    string path = Path.Combine(initial_directory, dir);
                    WriteLine("building dir - " + path);

                    using (Process process = new Process())
                    {
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.FileName = NMakeProgram;
                        process.StartInfo.Arguments = nmake_args;
                        process.StartInfo.WorkingDirectory = dir;

                        process.Start();
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            WriteLine("[{0} NMAKE failed: {1}", dir, process.ExitCode);
                            return process.ExitCode;
                        }
                    }
                }

                return 0;
            }
        }
        catch (Exception ex)
        {
            ShowException(ex);
            return 1;
        }
    }

    static void WriteLine(string line)
    {
        Console.WriteLine(Prefix + line);
    }

    static void WriteLine(string format, params object[] args)
    {
        WriteLine(String.Format(format, args));
    }

    const string Prefix = "NMAKE_DIRS: ";

    static void Usage()
    {
        Console.WriteLine("Usage: nmake_dirs [/p] dir1 dir2 ... dirN [/nmake args to nmake.exe]");
        Console.WriteLine();
        Console.WriteLine("    /p      Enables parallel builds of subdirectories.");
        Console.WriteLine("    /nmake  All args following /nmake are passed to nmake.exe.");
    }

    static void ShowException(Exception chain)
    {
        for (Exception ex = chain; ex != null; ex = ex.InnerException)
        {
            Console.WriteLine(ex.GetType().FullName + ": " + ex.Message);
        }
    }

    static string RemoveQuotes(string str)
    {
        str = str.Trim();
        if (str.StartsWith("\"") && str.EndsWith("\""))
            return str.Substring(1, str.Length - 2);
        return str;
    }

}
