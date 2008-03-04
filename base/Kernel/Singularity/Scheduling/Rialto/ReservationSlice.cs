////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ReservationSlice.cs
//
//  Note:
//

using System;
using Microsoft.Singularity.Scheduling;

namespace Microsoft.Singularity.Scheduling.Rialto
{
    /// <summary>
    /// A reservation slice is a slice of time assigned to the reservation.  It includes a
    /// reference to it associated reservation, is stored with the node, and contains a
    /// flag to say if it’s stolen.  Note that there is no reference from a reservation to
    /// its slices, only the other way around.
    /// </summary>
    public class ReservationSlice : ICloneable
    {

        public DateTime     Start;      // between [i].Start and [i+1].Start, node is reserved for AssociatedReservation
        public DateTime     End;
        public TimeSpan     Available;  // how much RT-time this entry represents
        public OneShotReservation  AssociatedReservation;

        public ReservationSlice()
        {

        }

#region ICloneable Members

        public object Clone()
        {
            ReservationSlice newObj = new ReservationSlice();
            newObj.Available = Available;
            newObj.End = End;
            newObj.AssociatedReservation = AssociatedReservation; //NOTE: A copy of the reference.  But OK since originally was a pointer.
            newObj.Start = Start;
            return newObj;
        }

#endregion
    }
}
