///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

/*

This program is a simple parallel task runner.  It reads a set of tasks from input files
(or from stdin), builds a queue of tasks, starts N threads, and each thread pulls tasks
from the queue and runs them.  Each worker thread is affinitized to a specific processor.

The input files are flat text files, one command per line.  Blank lines are ignored.
If a line consists of "+DRAIN", then the scheduler will wait for all previous tasks
to finish before continuing.

The program is designed to be run from command-line scripts (automated builds).  It does
support a GUI for displaying status, but the GUI never prompts the user for anything,
never blocks, etc. and so is at worst benign during automated builds.  The GUI can be
enabled with the /gui switch.


Arlie Davis
July 2006

*/

using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using Windows;

#if ENABLE_PROGRESS_FORM
using System.Windows.Forms;
#endif

namespace RunParallel
{
    sealed class Program
    {
        static uint _availableProcessorMask;
        static uint _threadMask;

        /// <summary>
        /// The shell to use when starting child processes; this comes from the COMSPEC
        /// variable in the environment.
        /// </summary>
        static string _shell;

        static int _errorCount;

        /// <summary>
        /// Handle to the NT kernel "job" object, created using the Win32 CreateJobObject()
        /// function.  All processes that this tool starts are put into the job, so that we
        /// can query the total CPU usage at the end.
        /// </summary>
        internal static IntPtr _JobObjectHandle;


        static bool _verbose;

        public static string ShellName
        {
            get { return _shell; }
        }

        public static object SyncLock
        {
            get { return _lock; }
        }

        const string DrainBarrierMarker = "+DRAIN";

#if ENABLE_PROGRESS_FORM
        /// <summary>
        /// If true, the GUI progress form will be displayed.
        /// </summary>
        static bool _enableProgressForm;
#endif

        /// <summary>
        /// This is the queue of work items that the main thread uses.
        /// Only the main thread ever touched this queue.  The main thread
        /// dequeues items from this queue, and moves them to the _workerTaskQueue.
        /// When it moves a job, the main thread also increments _workerActiveTaskCount.
        /// 
        /// This allows the main thread to implement synchronize/drain.  When the main
        /// thread needs to wait for all threads to finish processing, it just dequeues
        /// items from _workerFinishedQueue until _workerActiveTaskCount reaches zero.
        /// </summary>
        static readonly Queue_of_Task _taskQueue = new Queue_of_Task();

        /// <summary>
        /// The synchronized queue of tasks to execute.  Worker threads pull items
        /// from this queue, process them, and then enqueue those items to _workerFinishedQueue.
        /// </summary>
        static internal readonly SyncQueue_of_Task _workerTaskQueue = new SyncQueue_of_Task();

        /// <summary>
        /// The synchronized queue of tasks that have finished.  Worker threads enqueue
        /// items here, and the main thread dequeues items.
        /// </summary>
        static internal readonly SyncQueue_of_Task _workerFinishedQueue = new SyncQueue_of_Task();

        // number of items that the main thread has submitted to the worker threads
        static int _workerActiveTaskCount;

        internal static int _tasksCompletedCount;
        static int _totalTaskCount;

        static IntPtr ThisProcessHandle { get { return (IntPtr)(-1); } }

        static int Main(string[] args)
        {
            try
            {
                _shell = Environment.GetEnvironmentVariable("COMSPEC");
                if (Util.StringIsNullOrEmpty(_shell))
                    _shell = "cmd.exe";

                uint systemAffinityMask;
                if (!GetProcessAffinityMask(ThisProcessHandle, out _availableProcessorMask, out systemAffinityMask))
                {
                    WriteLine("Failed to get process affinity mask?!");
                    _availableProcessorMask = 1;
                    systemAffinityMask = 1;
                }

                _threadMask = _availableProcessorMask;

                if (Kernel32.IsCurrentProcessInJob())
                {
                    WriteLine("This process is already in a job; job accounting is disabled.");
                    _JobObjectHandle = IntPtr.Zero;
                }
                else
                {
                    try
                    {
                        _JobObjectHandle = Kernel32.CreateJobObject();
                    }
                    catch (Exception ex)
                    {
                        WriteLine("WARNING: Failed to create job for processes.");
                        ShowException(ex);
                        _JobObjectHandle = IntPtr.Zero;
                    }
                }

                ParseArgs(args);

                _totalTaskCount = _taskQueue.Count;

                DateTime timeStarted = DateTime.Now;

                RunTasks();


                DateTime timeEnded = DateTime.Now;

                TimeSpan elapsed = timeEnded - timeStarted;

                const string times_format = " {0,-32} : {1}";

                if (_JobObjectHandle != IntPtr.Zero)
                {
                    try
                    {
                        JOBOBJECT_BASIC_ACCOUNTING_INFORMATION job_info;
                        job_info = Kernel32.QueryJobObjectBasicAccountingInformation(_JobObjectHandle);

                        TimeSpan total_job_time = job_info.TotalUserTime + job_info.TotalKernelTime;

                        WriteLine(
                            times_format,
                            "CPU time consumed by jobs",
                            total_job_time);

                        WriteLine(
                            times_format,
                            "    in user mode",
                            job_info.TotalUserTime);

                        WriteLine(
                            times_format,
                            "    in kernel mode",
                            job_info.TotalKernelTime);

                        WriteLine(
                            times_format,
                            "Elapsed time (wall time)",
                            elapsed);

                        WriteLine("");
                        if (elapsed.TotalMilliseconds > 0)
                        {
                            double elapsed_ms = elapsed.TotalMilliseconds;
                            double total_job_ms = total_job_time.TotalMilliseconds;
                            double throughput_ratio = total_job_ms / elapsed_ms;
                            double throughput_percent = Math.Round(throughput_ratio * 100.0);
                            WriteLine("      Parallel throughput ratio: {0}%", throughput_percent);
                        }
                        else
                        {
                            WriteLine("      Too little time elapsed to compute ratio.");
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLine("FAILED to query job info!");
                        ShowException(ex);
                    }
                }
                else
                {
                    WriteLine("Child process times not available.");
                }

                if (_errorCount != 0)
                {
                    WriteLine("ERROR: {0} {1} completed unsuccessfully!",
                        _errorCount, _errorCount != 1 ? "tasks" : "task");
                    return 2;
                }

                return 0;
            }
            catch (Exception ex)
            {
                WriteLine("A top-level exception occurred.");
                ShowException(ex);
                return 1;
            }
            finally
            {
                if (_JobObjectHandle != IntPtr.Zero)
                {
                    Kernel32.CloseHandle(_JobObjectHandle);
                    _JobObjectHandle = IntPtr.Zero;
                }

                StopProgressForm();
            }
        }

        #region Progress Thread Support
#if ENABLE_PROGRESS_FORM

        static AutoResetEvent _progressThreadReady;

        static void CreateProgressThread()
        {
            using (AutoResetEvent ready = new AutoResetEvent(false))
            {
                _progressThreadReady = ready;

                _progressThread = new Thread(new ThreadStart(ProgressThreadRoutine));
                _progressThread.Start();

                if (!ready.WaitOne(30000, false))
                {
                    _progressThread.Abort();
                    _progressThread.Join();
                    throw new Exception("The GUI thread failed to start.");
                }

                _progressThreadReady = null;
            }
        }

        static void StopProgressForm()
        {
            if (_progressForm != null)
            {
                Debug.WriteLine("telling gui thread to quit");
                MethodInvoker close = new MethodInvoker(_progressForm.PublicClose);
                _progressForm.Invoke(close);
                Debug.WriteLine("waiting for GUI thread to quit");
                _progressForm = null;
            }

            if (_progressThread != null)
            {
                if (!_progressThread.Join(TimeSpan.FromSeconds(3)))
                {
                    _progressThread.Abort();
                    _progressThread.Join();
                }
                _progressThread = null;
            }
        }

        static void ProgressThreadRoutine()
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;

                Debug.Assert(_progressThreadReady != null);

                using (ProgressForm form = new ProgressForm())
                {
                    _progressForm = form;

                    lock (_lock)
                    {
                        foreach (WorkerThread worker in _workers)
                        {
                            ListViewItem item = new ListViewItem(worker.ThreadIndex.ToString());
                            item.SubItems.Add("");
                            item.SubItems.Add("");
                            _progressForm.WorkerListView.Items.Add(item);
                            worker.ProgressListItem = item;
                        }
                    }

                    _progressThreadReady.Set();
                    Application.Run(form);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception on GUI thread: " + ex.Message);
            }
        }

        static Thread _progressThread;
#else
        static void StopProgressForm()
        {
        }
#endif

        #endregion

        [DllImport("KERNEL32.DLL")]
        static extern bool GetProcessAffinityMask(IntPtr process, out uint processAffinityMask, out uint systemAffinityMask);

        [DllImport("KERNEL32.DLL")]
        static extern uint SetThreadIdealProcessor(IntPtr thread, uint processor);

        static uint ComputeProcessorAffinity(int maxThreads)
        {
            uint available_mask = _availableProcessorMask;
            uint enabled_mask = 0;
            int bit = 0;
            int enabled_count = 0;

            while (enabled_count < maxThreads)
            {
                for (; ; )
                {
                    if (bit < 32)
                    {
                        uint mask = (1u << bit);
                        if ((available_mask & mask) != 0)
                        {
                            enabled_mask |= mask;
                            bit++;
                            break;
                        }
                        else
                        {
                            bit++;
                            continue;
                        }
                    }
                    else
                    {
                        // No more processors!
                        Print("Max task count specified is higher than number of processors.");
                        Print("Capping at {0} concurrent tasks.", enabled_count);
                        return enabled_mask;
                    }
                }
            }

            return enabled_mask;
        }


        static char[] _commandSeparators = { ':', '=' };

        const string Prefix = "RUNPARALLEL: ";

        static void Print(string text)
        {
            WriteLine(Prefix + text);
        }

        static void Print(string format, params object[] args)
        {
            Print(String.Format(format, args));
        }

        /// <summary>
        /// Actually, this does more than just parse the args.
        /// It also loads the tasks.
        /// </summary>
        /// <param name="args"></param>
        static void ParseArgs(string[] args)
        {
            bool foundInputFiles = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.StartsWith("/") || arg.StartsWith("-"))
                {
                    int index = arg.IndexOfAny(_commandSeparators, 1);
                    string name;
                    string value;
                    if (index >= 0)
                    {
                        name = arg.Substring(1, index - 1);
                        value = arg.Substring(index + 1);
                    }
                    else
                    {
                        name = arg.Substring(1);
                        value = "";
                    }

                    name = name.ToLower();
                    switch (name)
                    {
                        case "help":
                        case "?":
                            Usage();
                            break;

                        case "stdin":
                            ReadTasksText(Console.In);
                            foundInputFiles = true;
                            break;

                        case "max":
                            int maxThreads;
                            try
                            {
                                maxThreads = Int32.Parse(name);
                            }
                            catch
                            {
                                WriteLine("Invalid value for -max");
                                Usage();
                                maxThreads = 0; // satisfy compiler
                            }
                            _threadMask = ComputeProcessorAffinity(maxThreads);
                            break;

                        case "v":
                        case "verbose":
                            _verbose = true;
                            break;

                        case "gui":
#if ENABLE_PROGRESS_FORM
                            _enableProgressForm = true;
#endif
                            break;

                        case "tasks":
                            using (StreamReader reader = File.OpenText(arg))
                            {
                                ReadTasksText(reader);
                                foundInputFiles = true;
                            }
                            break;

                        default:
                            WriteLine("Unrecognized switch: " + name);
                            Usage();
                            break;
                    }
                }
                else
                {
#if false
                    // It appears to be an inline repeat, sort of like xargs.
                    int start = i;
                    i++;
                    while (i < args.Length && args[i] != "--")
                        i++;
                    if (i == args.Length)
                    {
                        WriteLine("No tasks were specified.  Please use -- to separate the fixed portion");
                        WriteLine("of the command line from the list of tasks.");
                        ShortUsage();
                    }

                    string command_base = ArgsToString(args, start, i - start);
                    const string insertion_marker = "$=";
                    bool has_insertion_marker = command_base.IndexOf(insertion_marker) != -1;

                    // For each remaining argument, build a task.
                    i++;
                    bool drain_required = false;
                    for (int j = i; j < args.Length; j++)
                    {
                        string taskarg = args[j];

                        if (Util.StringCompareCaseInsensitive(taskarg, DrainBarrierMarker) == 0)
                        {
                            // A drain marker indicates that all tasks in the current sequence must
                            // finish before any tasks after the marker begin.
                            drain_required = true;
                            continue;
                        }

                        string command = command_base;

                        if (has_insertion_marker)
                            command = command_base.Replace(insertion_marker, taskarg);
                        else
                            command = command_base + " " + taskarg;

                        Task task = new Task();
                        task.Name = taskarg;
                        task.CommandLine = command;
                        task.DrainRequired = drain_required;
                        _taskQueue.Enqueue(task);

                        if (drain_required)
                        {
                            // WriteLine("{0}: task will cause drain", task.CommandLine);
                        }

                        drain_required = false;
                    }
                    foundInputFiles = true;
                    break;
#else
                    ReadTasksText(arg);
                    foundInputFiles = true;
#endif
                }
            }

            if (!foundInputFiles)
                Usage();
        }

        static string ArgsToString(string[] args, int offset, int count)
        {
            StringBuilder buffer = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    buffer.Append(" ");

                string arg = args[offset + i];

                bool quotes = false;
                if (arg.IndexOf(" ") != -1)
                    quotes = true;

                if (quotes)
                    buffer.Append("\"");

                buffer.Append(arg);

                if (quotes)
                    buffer.Append("\"");
            }

            return buffer.ToString();
        }

        static void ShortUsage()
        {
            WriteLine("Use -? or -help for a list of full arguments.");
            Environment.Exit(1);
        }

        static void Usage()
        {
            WriteLine(
@"
RUNPARALLEL: A simple parallel task runner.

Usage: RUNPARALLEL [options] /tasks:foo.txt /tasks:bar.txt [/stdin]

Or:
        RUNPARALLEL [options] foo.exe arg1 ... $= ... -- task1 task2 task3

where [options] can be:

    /max:nn     Maximum of nn active tasks
"
#if ENABLE_PROGRESS_FORM
+ @"    /gui        Show the GUI progress form (non-blocking)\r\n"
#endif
+ @"    /stdin      Read tasks from stdin
    /v          Verbose info about job scheduling

Each input file contains commands to run in parallel.  Each command is on a 
separate line.  The +DRAIN marker will cause all previous commands to complete
before continuing.  You can use /stdin to read tasks from standard input.

If you use the second form, then no input file is necessary.  The first arguments
form the command-line for the program to run.  The ""--"" marker separates the
command-line to run from the tasks to run.  If the command-line to run has a
'$=' marker in it, then that marker will be replaced with each of the task values.
If there is no marker, then the task value will be appended to the end of the
command-line.  For example:

    runparallel cd $= ^&^& nmake /nologo -- app1 app2 app3

will result in these commands being run in parallel:

    cd app1 && nmake /nologo
    cd app2 && nmake /nologo
    cd app3 && nmake /nologo

The special task-name +DRAIN will cause the scheduler to wait for all previous
tasks to complete before scheduling more tasks.

Contact Arlie Davis (arlied) with questions.
");
            Environment.Exit(1);

        }

        /// <summary>
        /// This method reads a text file and creates work items.  Each line in the file
        /// contains a work item.  There is no comment character.  Blank lines are ignored.
        /// </summary>
        /// <param name="reader"></param>
        static void ReadTasksText(TextReader reader)
        {
            bool drain_required = false;

            for (; ; )
            {
                string line = reader.ReadLine();
                if (line == null)
                    break;

                if (Util.IsBlank(line))
                    continue;

                line = line.Trim();
                if (Util.StringCompareCaseInsensitive(line, DrainBarrierMarker) == 0)
                {
                    drain_required = true;
                    continue;
                }

                Task task = new Task();

                task.Name = line;
                task.CommandLine = line;
                task.DrainRequired = drain_required;

                _taskQueue.Enqueue(task);

                drain_required = false;
            }
        }

        static void ReadTasksText(string filename)
        {
            using (StreamReader reader = File.OpenText(filename))
            {
                ReadTasksText(reader);
            }
        }

        static int GetSetBitCount(uint value)
        {
            uint result = ((value & 0xaaaaaaaau) >> 1) + (value & 0x55555555u);
            result = ((result & 0xccccccccu) >> 2) + (result & 0x33333333u);
            result = ((result & 0xf0f0f0f0u) >> 4) + (result & 0x0f0f0f0fu);
            result = ((result & 0xff00ff00u) >> 8) + (result & 0x00ff00ffu);
            result = ((result & 0xffff0000u) >> 16) + (result & 0x0000ffffu);
            return checked((int)result);
        }

        static void RunTasks()
        {
            int threadcount = GetSetBitCount(_threadMask);

            WriteLine("Running {0} tasks on {1} thread(s) in directory: {2}",
                _taskQueue.Count,
                threadcount,
                Environment.CurrentDirectory);

            List_of_WorkerThread slotlist = new List_of_WorkerThread();

            uint runMask = _threadMask;

            for (int i = 0; i < 32; i++)
            {
                uint mask = (1u << i);
                if ((runMask & mask) != 0)
                {
                    WorkerThread worker = new WorkerThread();
                    worker.AffinityMask = mask;
                    worker.Thread = new Thread(new ThreadStart(worker.ThreadRoutine));
                    worker.ThreadIndex = i;
                    slotlist.Add(worker);
                }
            }

#if CLR2
            _workers = slotlist.ToArray();
#else
            _workers = (WorkerThread[])slotlist.ToArray(typeof(WorkerThread));
#endif

#if ENABLE_PROGRESS_FORM
            if (_enableProgressForm)
            {
                CreateProgressThread();
            }
#endif

            foreach (WorkerThread worker in _workers)
            {
                worker.Thread.Start();
            }

            // WriteLine("Dispatching work items...");

            _workerActiveTaskCount = 0;

            for (; ; )
            {
                if (_taskQueue.Count != 0)
                {
                    Task task = (Task)_taskQueue.Dequeue();

                    if (task.DrainRequired)
                    {
                        if (_workerActiveTaskCount != 0)
                            WriteLine("Performing synchronization drain.");
                        WaitActiveTasks();
                    }

                    _workerTaskQueue.Enqueue(task);
                    _workerActiveTaskCount++;
                }
                else
                {
                    // WriteLine("Main thread has reached end of task queue.  Closing worker queue.");
                    break;
                }
            }

            _workerTaskQueue.Close();
            WaitActiveTasks();

            foreach (WorkerThread worker in _workers)
            {
                if (worker.Thread != null)
                {
                    worker.Thread.Join();
                }
            }

            WriteLine("Done.");
        }

        static void WaitActiveTasks()
        {
            Debug.Assert(_workerActiveTaskCount >= 0);

            if (_verbose)
                WriteLine("waiting for {0} active task(s) to complete.", _workerActiveTaskCount);

            if (_workerActiveTaskCount == 0)
            {
                // WriteLine("no need to wait; no active tasks");
                return;
            }


            while (_workerActiveTaskCount > 0)
            {
                Task task;
                if (_workerFinishedQueue.Dequeue(out task))
                {
                    _workerActiveTaskCount--;
                    if (!task.Succeeded)
                    {
                        _errorCount++;
                    }
                }
                else
                {
                    Debug.Fail("_workerFinishedQueue.Dequeue did not return an item!!");
                    WriteLine("Internal error!  _workerFinishedQueue did not return an item!");
                    throw new Exception("Internal error!  _workerFinishedQueue did not return an item!");
                }
            }
        }

        static WorkerThread[] _workers;

        static readonly object _lock = new Object();

#if ENABLE_PROGRESS_FORM
        static ProgressForm _progressForm;

        static void UpdateProgressAnyThread()
        {
            if (_progressForm == null)
                return;

            _progressForm.Invoke(new MethodInvoker(UpdateProgressGuiThread), null);
        }

        internal static void UpdateProgressGuiThread()
        {
            // This runs on the GUI thread.

            lock (_lock)
            {
                if (_totalTaskCount != 0)
                {
                    int percent = (100 * _tasksCompletedCount) / _totalTaskCount;
                    _progressForm.OverallProgressBar.Value = percent;
                }

                _progressForm._TotalTasksLabel.Text = _totalTaskCount.ToString();
                int tasksRemaining = _taskQueue.Count;
                _progressForm._TasksRemainingLabel.Text = tasksRemaining.ToString();


                foreach (WorkerThread slot in _workers)
                {
                    ListViewItem item = slot.ProgressListItem;

                    if (slot.CurrentTask != null)
                    {
                        item.SubItems[1].Text = slot.CurrentTask.Name;
                        item.SubItems[2].Text = slot.CurrentTask.CommandLine;
                    }
                    else
                    {
                        item.SubItems[1].Text = "(none)";
                        item.SubItems[2].Text = "(none)";
                    }
                }
            }
        }
#endif

        static public void ShowException(Exception chain)
        {
            for (Exception current = chain; current != null; current = current.InnerException)
            {
                WriteLine("{0}: {1}", current.GetType().FullName, current.Message);
            }
        }

        static void WriteLine(string line)
        {
            Console.WriteLine("main> " + line);
        }

        static void WriteLine(string format, params object[] args)
        {
            WriteLine(String.Format(format, args));
        }
    }



}
