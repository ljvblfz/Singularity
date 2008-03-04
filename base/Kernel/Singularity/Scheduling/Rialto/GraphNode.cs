////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   GraphNode.cs
//
//  Note:
//

using System;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Rialto
{
    /// <summary>
    /// Nodes represent blocks of the Rialto pre-computed schedule.  A node has a
    /// period and slice, accounting for its default activity, time to origin in
    /// the schedule, next execution time, and a set of variables which form the
    /// tree.  A node tree has strictly increasing periods at each level in the
    /// tree (it doubles), and a node can have a `next' node (at the same level),
    /// can branch into left and right nodes (at one level below), or can account
    /// free time (and have neither).  The type field stores those cases as well
    /// as the direction of the next branch to follow (Used, Free, LeftBranch,
    /// RightBranch).  When traversing the tree, the next node can be determined
    /// by the following steps: (1) If the current node has a next node, select it
    /// next.  (2) Otherwise, if it has type Left or Right branch, follow the
    /// branch in that direction, and toggle which branch is stored.  (3) If it is
    /// a Free node, start back at the root of the tree.
    /// </summary>
    public class GraphNode
    {
#region Constants and Enumerations

        public enum NodeType
        {
            Free = 0,
            Used = 1,
            LeftBranch = 2,
            RightBranch = 3
        };

        public static readonly int MaxNumReservations = 250;

        public static readonly int MaxProxySplits = 10;
        public static readonly int MaxMarcelTries = 2000;

#endregion

        public NodeType     Type;       // Free, Used, BRANCH, (Includes mark of what next branch to take is) etc....
        public TimeSpan     Slice;      // node's slice (time units)
        public TimeSpan     Period;     // node's period (time units)
        public DateTime     NextExec;   // start of Next or current CPU slice
        public TimeSpan     TimeToOrigin;// time to the first node of the tree
        public RialtoActivity      DefaultActivity;   // default activity for Used nodes

        public int          ReservCount;             // valid entries in the array above

        public GraphNode    Next;       // Next sibling
        public GraphNode    Left;       // left and right children of a BRANCH node
        public GraphNode    Right;      // leaf nodes have left == right == Next == null
        public GraphNode    SameActivityNext; // Next node of the same activity
        public ReservationSlice[]   ReservationArray; // array of pointers to CPU reservations

        public GraphNode()
        {
            ReservationArray = new ReservationSlice[MaxNumReservations];
        }

        public static GraphNode InitTree()
        {
            GraphNode node = new GraphNode();

            node.Type = NodeType.Free;
            node.Slice = CpuResource.MaxPeriod;
            node.Period = CpuResource.MaxPeriod;
            node.TimeToOrigin = CpuResource.MaxPeriod;
            node.NextExec = new DateTime(0);
            node.ReservCount = 0;
            node.Next = null;
            node.Left = null;
            node.Right = null;
            node.SameActivityNext = null;
            return node;
        }
    }
}
