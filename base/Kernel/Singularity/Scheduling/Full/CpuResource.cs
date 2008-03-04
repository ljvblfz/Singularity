////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Kernel\Singularity\Scheduling\CpuResource.cs
//
//  Note:
//

using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using Microsoft.Singularity;
#if SIMULATOR
using Thread = Microsoft.Singularity.Scheduling.Thread;
using Processor = Microsoft.Singularity.Scheduling.Processor;
#endif

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// The CPU resource provider object.
    /// </summary>
    [CLSCompliant(false)]
    public class CpuResource : IResource
    {
        /// <summary>
        /// Return a reference to the CPU resource provider object.
        /// </summary>
        static public CpuResource Provider()
        {
            return cpuResourceProvider;
        }

        public override string ResourceString
        {
            get { return "Microsoft.Singularity.Scheduling.CpuResource"; }
        }

        /// <summary>
        /// Constructor for the static CPU Resource Provider object
        /// </summary>
        private CpuResource()
        {
        }

        /// <summary>
        /// The static CPU Resource Provider object
        /// </summary>
        private static CpuResource cpuResourceProvider = new CpuResource();

        //Establishes the system scheduler -- should be called by system initialization, and should
        //throw an error when called a 2nd time.
        public static void RegisterSystemScheduler(ICpuScheduler scheduler)
        {
            if(cpuResourceScheduler == null) {
                cpuResourceScheduler = scheduler;
            }
            else {
                //TODO: Throw Exception Perhaps!
            }
        }

        /// <summary>
        /// This is the actual system CPU scheduler.  It is used for CPU reservations,
        /// constraints, to interact with the timer, and to allow the CPU scheduler
        /// to be swapped out while maintaining a consistent interface.
        /// </summary>
        private static ICpuScheduler cpuResourceScheduler;

        public static ISchedulerActivity CreateSchedulerActivity()
        {
            return cpuResourceScheduler.CreateSchedulerActivity();
        }

        public static ISchedulerThread CreateSchedulerThread(Thread thread)
        {
            return cpuResourceScheduler.CreateSchedulerThread(thread);
        }

        public static ISchedulerProcessor CreateSchedulerProcessor(Processor processor)
        {
            return cpuResourceScheduler.CreateSchedulerProcessor(processor);
        }

        public static readonly TimeSpan MaxPeriod = new TimeSpan(1 * TimeSpan.TicksPerSecond);

        /// <summary>
        /// Return amount of CPU available for reservation for the specified Activity and period
        /// </summary>
        public CpuResourceAmount AvailableCpu(Activity activity, TimeSpan period)
        {
            // XXX TBD
            return new CpuResourceAmount((cyclesPerSecond * period.Ticks)
                                         / TimeSpan.TicksPerSecond);
        }

        /// <summary>
        /// Reserve CPU on an ongoing basis for a resource container.
        /// Replaces any existing reservation for that resource container.
        /// Specify 0 cycles to end the CPU reservation for the resource container.
        /// Returns null if reservation is denied.  XXX Should this throw an exception instead?
        /// </summary>
        public CpuResourceReservation ReserveCpu(Activity activity,
            CpuResourceAmount amount, TimeSpan period)
        {
            object tempOldReservation = activity.GetResourceReservation(CpuResource.Provider().ResourceString);
            Debug.Assert(tempOldReservation == null ||
                         tempOldReservation is CpuResourceReservation);
            CpuResourceReservation oldReservation = (CpuResourceReservation)tempOldReservation;

            ISchedulerCpuReservation schedulerReservation =
                cpuResourceScheduler.ReserveCpu(activity.schedulerActivity, amount, period);

            CpuResourceReservation reservation = null;
            if(schedulerReservation != null) {
                reservation = new CpuResourceReservation(amount, period, schedulerReservation);
                schedulerReservation.EnclosingCpuReservation = reservation;
            }

            activity.SetResourceReservation(CpuResource.Provider().ResourceString, reservation);
            return reservation;
        }

        /// <summary>
        /// Convert a CPU amount to the minimum amount of time required for that amount.
        /// This conversion assumes that the CPU is running at its maximum rate.
        /// </summary>
        public CpuResourceAmount TimeToCpu(TimeSpan time)
        {
            return new CpuResourceAmount((cyclesPerSecond * time.Ticks)
                                         / TimeSpan.TicksPerSecond);
        }

        /// <summary>
        /// Convert a timespan to the maximum amount of CPU that can be used in that timespan.
        /// This conversion assumes that the CPU is running at its maximum rate.
        /// </summary>
        public TimeSpan CpuToTime(CpuResourceAmount amount)
        {
            if(amount == null) {
                return new TimeSpan(0);
            }
            return new TimeSpan((TimeSpan.TicksPerSecond * amount.Cycles)
                                / cyclesPerSecond);
        }

        private long cyclesPerSecond;

        volatile static int do_something = 0;
        static void Spinner()
        {
            do_something++;
        }

        public bool NextThread(out Thread nextThread)
        {
            return cpuResourceScheduler.NextThread(out nextThread);
        }

        public bool ShouldReschedule()
        {
            return cpuResourceScheduler.ShouldReschedule();
        }

        //XXX: One resource world only.
        /// <summary>
        /// Note that as tasks now contain resource accounting, there is
        /// no need to pass out the resource usage from the scheduler.
        /// For consistency we'll still pass it out of the Task API at least
        /// until instantaneous usage is fixed.
        /// </summary>
        /// <param name="?"></param>
        /// <param name="?"></param>
        /// <param name="?"></param>
        /// <param name="?"></param>
        /// <returns>Returns true if admitted.</returns>
        internal bool BeginConstraint(Hashtable resourceEstimates,
                                      DateTime deadline,
                                      Task taskToEnd,
                                      out ISchedulerTask schedulerTask)
        {
            return cpuResourceScheduler.BeginConstraint(resourceEstimates,
                                                        deadline, ((taskToEnd==null)?null:taskToEnd.schedulerTask), out schedulerTask);
        }

        internal void BeginDelayedConstraint(Hashtable resourceEstimates, TimeSpan relativeDeadline, Task taskToEnd, out ISchedulerTask schedulerTask)
        {
            cpuResourceScheduler.BeginDelayedConstraint(resourceEstimates, relativeDeadline, ((taskToEnd==null)?null:taskToEnd.schedulerTask), out schedulerTask);
        }

        internal Hashtable EndConstraint(Task taskToEnd)
        {
            cpuResourceScheduler.EndConstraint(((taskToEnd==null)?null:taskToEnd.schedulerTask));
            return taskToEnd.resourcesUsed;
        }

        public void Initialize()
        {
            Debug.Assert(cpuResourceScheduler != null, "No registered scheduler!");
            cyclesPerSecond = (long)Processor.CyclesPerSecond;

            Debug.Assert(Processor.processorTable != null, "Processor table is null!");
            for (int i = 0; i < Processor.processorTable.Length; i++) {
                Debug.Assert(Processor.processorTable[i] != null, "Processor is null!");
                Processor.processorTable[i].InitializeSchedulerProcessor();
            }

            cpuResourceScheduler.Initialize();
        }
    }
}
