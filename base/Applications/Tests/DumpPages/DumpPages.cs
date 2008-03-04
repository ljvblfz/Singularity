////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   DumpPages.cs
//
//  Note:   Simple Singularity test program.
//
using System;

namespace Microsoft.Singularity.Applications
{
    public class DumpPages
    {
        //[ShellCommand("dump", "Dump page table")]
        public static int Main(String[] args)
        {
            AppRuntime.DumpPageTable();
            return 0;
        }
    }
}
