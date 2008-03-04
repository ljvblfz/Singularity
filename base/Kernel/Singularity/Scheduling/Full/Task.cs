////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Task.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using System.Collections;
using System.Threading;
#if SIMULATOR
using Thread = Microsoft.Singularity.Scheduling.Thread;
#endif

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// A Singularity Task object represents work requiring a set of resources to be performed by a deadline.
    /// </summary>
    public class Task
    {
        /// <summary>
        /// True if task was successfully admitted
        /// </summary>
        public bool Admitted
        {
            get { return admitted; }
        }
        internal bool admitted;

        internal Task parentTask;
        private  DateTime deadline;
        internal Activity activity;
        private  Hashtable resourceEstimates;
        internal Hashtable resourcesGranted;
        internal Hashtable resourcesUsed;
        internal ISchedulerTask schedulerTask;

        /// <summary>
        /// To begin a constraint, use Task.BeginConstraint.  The Task constructors
        /// are to be used only within the system, and perform no reservation
        /// activities.
        ///
        /// This constructor makes a task as a subtask of CurrentTask() and
        /// CurrentActivity()
        /// </summary>
        internal Task(Hashtable resourceEstimates,
                      Hashtable resourcesGranted,
                      DateTime deadline,
                      bool admitted,
                      ISchedulerTask schedulerTask)
            : this(resourceEstimates, resourcesGranted, deadline, admitted, schedulerTask,
                    Scheduler.CurrentTask(), Scheduler.CurrentActivity())
        {
        }

        /// <summary>
        /// To begin a constraint, use Task.BeginConstraint.  The Task constructors
        /// are to be used only within the system, and perform no reservation
        /// activities.
        ///
        /// This constructor makes a task as a subtask of parentTask (possibly null)
        /// and activity (likely a new resource container).
        /// </summary>
        internal Task(Hashtable resourceEstimates,
                      Hashtable resourcesGranted,
                      DateTime deadline,
                      bool admitted,
                      ISchedulerTask schedulerTask,
                      Task parentTask,
                      Activity activity)
        {
            this.activity = activity;
            this.parentTask = parentTask;
            this.resourceEstimates = resourceEstimates;
            this.resourcesGranted = (resourcesGranted == null) ? new Hashtable() : resourcesGranted;
            this.deadline = deadline;
            this.resourcesUsed = new Hashtable();
            this.admitted = admitted;
            this.schedulerTask = schedulerTask;
        }

        public void ClearSchedulerTask()
        {
            schedulerTask = null;
        }

        public void UpdateSchedulingState(bool admitted,
                                          DateTime deadline,
                                          Hashtable resourcesGranted)
        {
            this.admitted = admitted;
            this.deadline = deadline;
            this.resourcesGranted = resourcesGranted;
        }

        //Because there are no timed-wait-and-begin-constraint (i.e. future constraints),
        //      I had to modify some of the simfiles to keep threads from exiting early.
        //      Currently start-times later than now are treated as starting now.
        public static Task BeginTask()
        {
            Hashtable foo;
            return BeginTask(null, DateTime.MaxValue, null, out foo);
        }

        public static Task BeginTask(Task taskToEnd, out Hashtable used)
        {
            return BeginTask(null, DateTime.MaxValue, taskToEnd, out used);
        }

        public static Task BeginTask(Hashtable resourceEstimates, DateTime deadline)
        {
            Hashtable foo;
            return BeginTask(resourceEstimates, deadline, null, out foo);
        }

        public static Task BeginTask(Hashtable resourceEstimates,
                                     DateTime deadline,
                                     Task taskToEnd,
                                     out Hashtable actualUsed)
        {
            //In a single resource world, it is sufficient here to
            //  ask the CpuProvider to begin the constraint.  However,
            //  this code will be the beginning of the starting point
            //  for multi-resource scheduling in this capacity.
            ISchedulerTask schedulerTask;
            Task task;
            if ((resourceEstimates != null && resourceEstimates.Count > 0) ||
                (Scheduler.CurrentTask().resourceEstimates != null && Scheduler.CurrentTask().resourceEstimates.Count > 0)) {
                bool admitted = CpuResource.Provider().BeginConstraint(resourceEstimates,
                                                                       deadline,
                                                                       taskToEnd,
                                                                       out schedulerTask);
                if (taskToEnd == null) {
                    task = new Task(resourceEstimates,
                                    schedulerTask.ResourcesGranted,
                                    deadline,
                                    admitted,
                                    schedulerTask);
                }
                else {
                    task = new Task(resourceEstimates,
                                    schedulerTask.ResourcesGranted,
                                    deadline,
                                    admitted,
                                    schedulerTask,
                                    taskToEnd.parentTask,
                                    taskToEnd.activity);
                }
                schedulerTask.EnclosingTask = task;
            }
            else {
                if (taskToEnd != null) {
                    taskToEnd.End();
                }
                task = new Task(null, null, DateTime.MaxValue, true, null);
            }
            if (taskToEnd != null) {
                actualUsed = taskToEnd.resourcesUsed;
            }
            else {
                actualUsed = null;
            }
            Thread.CurrentThread.currentTask = task;
            Debug.Assert(task.resourcesUsed.Count == 0);
            return task;
        }

        public static Task BeginDelayedTask(Hashtable resourceEstimates,
                                            TimeSpan relativeDeadline)
        {
            Hashtable foo;
            return BeginDelayedTask(resourceEstimates, relativeDeadline, null, out foo);
        }

        /// <summary>
        /// Delayed tasks are used for such things as atomic wait and begin constraints.
        /// The interpretation is that when a thread is woken up, the scheduler promises
        /// to resolve any pending constraints atomically with the wakeup.  After calling
        /// BeginDelayedTask, the task won't actually begin until the thread performs
        /// some action that causes a sleep/wait and is being woken up from it.
        ///
        /// Example uses:
        ///
        /// while (doingSomeStuff) {
        ///     Task t = BeginDelayedTask(resourceEstimates, relativeDeadline);
        ///     autoResetEvent.Wait();
        ///     if (t.Admitted) {
        ///         //do full work
        ///     } else {
        ///         //do less work is possible
        ///     }
        ///     t.End();
        /// }
        ///
        /// while (doingSomeStuff) {
        ///     Task t = BeginDelayedTask(resourceEstimates, relativeDeadline);
        ///     Thread.Sleep(500);
        ///     if (t.Admitted) {
        ///         //do full work
        ///     } else {
        ///         //do less work is possible
        ///     }
        ///     t.End();
        /// }
        ///
        /// while (doingSomeStuff) {
        ///     Task t = BeginDelayedTask(resourceEstimates, relativeDeadline);
        ///     Thread.Sleep(500);
        ///     if (t.Admitted) {
        ///         //do full work
        ///     } else {
        ///         //do less work is possible
        ///     }
        ///     t.End();
        /// }
        ///
        /// Mutex m;
        /// while (doingSomeStuff) {
        ///     Task t = BeginDelayedTask(resourceEstimates, relativeDeadline);
        ///     m.Acquire();
        ///     if (t.Admitted) {
        ///         //do full work
        ///     } else {
        ///         //do less work is possible
        ///     }
        ///     t.End();
        /// }
        ///
        /// </summary>
        /// <param name="resourceEstimates"></param>
        /// <param name="relativeDeadline"></param>
        /// <param name="taskToEnd"></param>
        /// <param name="actualUsed"></param>
        /// <returns></returns>
        public static Task BeginDelayedTask(Hashtable resourceEstimates,
                                            TimeSpan relativeDeadline,
                                            Task taskToEnd,
                                            out Hashtable actualUsed)
        {
            //In a single resource world, it is sufficient here to
            //  ask the CpuProvider to begin the constraint.  However,
            //  this code will be the beginning of the starting point
            //  for multi-resource scheduling in this capacity.
            ISchedulerTask schedulerTask;
            Task task;

            Debug.Assert((resourceEstimates != null && resourceEstimates.Count > 0) ||
                (Scheduler.CurrentTask().resourceEstimates != null && Scheduler.CurrentTask().resourceEstimates.Count > 0));
            CpuResource.Provider().BeginDelayedConstraint(resourceEstimates, relativeDeadline, taskToEnd, out schedulerTask);
            Debug.Assert(schedulerTask != null);
            if (taskToEnd == null) {
                task = new Task(resourceEstimates, schedulerTask.ResourcesGranted, DateTime.MaxValue, false, schedulerTask);
            }
            else {
                task = new Task(resourceEstimates, schedulerTask.ResourcesGranted, DateTime.MaxValue, false, schedulerTask, taskToEnd.parentTask, taskToEnd.activity);
            }
            schedulerTask.EnclosingTask = task;
            if (taskToEnd != null) {
                actualUsed = taskToEnd.resourcesUsed;
            }
            else {
                actualUsed = null;
            }
            Thread.CurrentThread.currentTask = task;
            Debug.Assert(task.resourcesUsed.Count == 0);
            return task;
        }

        /// <summary>
        /// The common case method for indicating task completion
        /// </summary>
        /// <returns>Returns the actual amounts of resources consumed</returns>
        public Hashtable End()
        {
            //Do we want this static -- i.e. to prevent the thread from trying
            // to end a task it's not presently working on?
            Debug.Assert(this == Scheduler.CurrentTask(), "Cannot end a task other than the current task!");
            Debug.Assert(this != Activity().DefaultTask(), "Cannot end the default task for the resource container!");
            CpuResource.Provider().EndConstraint(this);
            Thread.CurrentThread.currentTask = parentTask;
            return resourcesUsed; // XXX TBD
        }

        /// <summary>
        /// Return the Activity object that this task is part of
        /// </summary>
        public Activity Activity()
        {
            return activity;
        }

        public void AddResourceAmountUsed(string resourceString, IResourceAmount amount)
        {
            IResourceAmount current = (IResourceAmount)resourcesUsed[resourceString];
            if (current == null) {
                resourcesUsed[resourceString] = amount;
            }
            else {
                ((IResourceAmount)resourcesUsed[resourceString]).AddTo(amount);
            }
            if (parentTask != null) {
                parentTask.AddResourceAmountUsed(resourceString, amount);
            }
        }
    }
}
