////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   TimeConstraint.cs
//
//  Note:
//

using System;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// A public structure used to pass data between the Kernel and the Application to express a
    /// time constraint.
    /// </summary>
    public struct TimeConstraint
    {
        public DateTime Start;
        public TimeSpan Estimate;
        public DateTime Deadline;
        public TimeSpan RelativeDeadline;
    }
}
