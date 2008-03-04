///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

using System;

namespace RunParallel
{
    class Task
    {
        public string CommandLine;
        public string Name;
        public bool DrainRequired;
        public bool Succeeded;
        public Exception Error;
    }
}
