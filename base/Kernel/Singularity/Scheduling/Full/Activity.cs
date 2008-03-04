////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Activity.cs
//
//  Note:
//

using System;
using System.Collections;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// This class is the base of the resource class.  To reduce code management cost
    /// between the simulator and kernel, both kernel and simulator resource container
    /// classes derive from this base class, which contains most of the functionality.
    /// </summary>
    public class Activity
    {
        Task defaultTask;
        //IResourceReservation[] reservations;
        Hashtable reservations;
        internal ISchedulerActivity schedulerActivity;

        public  Activity()
        {
            defaultTask = new Task(null, null, DateTime.MaxValue, true, null, null, this); //The default task
            reservations = new Hashtable();
            schedulerActivity = CpuResource.CreateSchedulerActivity();
            schedulerActivity.EnclosingActivity = this;
        }

        /// <summary>
        /// Return the default task object for this Activity.
        /// Any code not inside an explicit task is working on behalf of the default "Task".
        /// Would you not prefer this be a property?
        /// </summary>
        public Task DefaultTask()
        {
            return defaultTask;
        }

        /// <summary>
        /// Returns the ongoing resource reservations for this resource container.
        /// </summary>
        public ICollection Reservations()
        {
            return reservations.Values;
        }

        internal IResourceReservation GetResourceReservation(string resourceString)
        {
            return (IResourceReservation)reservations[resourceString];
        }

        internal void SetResourceReservation(string resourceString, IResourceReservation reservation)
        {
            if (reservation == null) {
                reservations.Remove(resourceString);
            }
            else {
                reservations[resourceString] = reservation;
            }
        }

        /// <summary>
        /// This only need be called by the thread which creates the resource container,
        /// and should do so once the reference won't be used outside the resource
        /// container (i.e. to add threads to it).
        /// </summary>
        public void ReleaseReference()
        {
            schedulerActivity.ReleaseReference();
        }
    }
}
