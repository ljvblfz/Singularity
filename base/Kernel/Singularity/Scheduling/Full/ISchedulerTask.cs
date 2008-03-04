////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\ISchedulerTask.cs
//
//  Note:
//

using System.Collections;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// Summary description for ISchedulerTask.
    /// </summary>
    public abstract class ISchedulerTask
    {
        public abstract Task EnclosingTask { set; get; }

        //This is temporary in the one resource world.  It needs
        // to change for the multi-resource world.
        //XXX
        public abstract Hashtable ResourcesGranted { get; }
    }
}
