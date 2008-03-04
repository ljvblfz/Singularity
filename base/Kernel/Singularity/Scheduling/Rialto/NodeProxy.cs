////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   NodeProxy.cs
//
//  Note:
//

using System;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Rialto
{
    /// <summary>
    /// NodeProxies are created as temporary activity proxies for use in building a new
    /// scheduler plan.  The slice, period, and associated activity are relatively easy
    /// to digest, but there are two variables TreeLevel and FreeLevel which are a bit
    /// more.  FreeLevel refers to the row in the SchedulerPlan which this node should be
    /// in based on its period, and TreeLevel represents the exact position in a full
    /// binary tree of the schedule.  It is computed by 2^row + position in row, and
    /// corresponds to the position in the FreeSlots[][] (see below).  (For that matter,
    /// FreeLevel refers to which FreeSlots top level array its time is subtracted from).
    /// </summary>
    public class NodeProxy : ICloneable
    {

        public int          TreeLevel; //TODO: BAD NAME
        public int          FreeLevel;
        public TimeSpan     Slice, Period;
        public RialtoActivity      AssociatedActivity;

        public NodeProxy()
        {
        }

#region ICloneable Members

        public object Clone()
        {
            NodeProxy newObj = new NodeProxy();
            newObj.TreeLevel = TreeLevel;
            newObj.FreeLevel = FreeLevel;
            newObj.Slice = Slice; //NOTE: A copy of the reference.  But OK since originally was a pointer.
            newObj.Period = Period;
            newObj.AssociatedActivity = AssociatedActivity;
            return newObj;
        }

#endregion
    }
}
