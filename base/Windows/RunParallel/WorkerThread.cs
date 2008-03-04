///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

using System;
using System.IO;
using System.Threading;
using System.Text;
using System.Diagnostics;
using Windows;

#if ENABLE_PROGRESS_FORM
using System.Windows.Forms;
#endif

namespace RunParallel
{
    class WorkerThread
    {
        public Thread Thread;
        public int ThreadIndex;
        public uint AffinityMask;
        public int ErrorCount;
        public string Prefix;
#if ENABLE_PROGRESS_FORM
        public ListViewItem ProgressListItem;
#endif
        public Task CurrentTask;

        public WorkerThread()
        {
            Prefix = "";
        }

        public void ThreadRoutine()
        {
            Prefix = String.Format("{0,4}> ", this.ThreadIndex);

            this.ErrorCount = 0;

            // WriteLine("Starting");

            for (; ; )
            {
                Task task = GetTask();
                if (task == null)
                    break;

                const int command_display_max = 45;

                string command = task.CommandLine;
                if (command.Length > command_display_max)
                    command = command.Substring(0, command_display_max) + "...";

                WriteLine("Running: " + command);

                lock (_lock)
                {
                    this.CurrentTask = task;
                }

                try
                {
                    using (Process process = new Process())
                    {
                        ProcessStartInfo info = new ProcessStartInfo();
                        info.UseShellExecute = false;
                        info.FileName = Program.ShellName;
                        info.Arguments = "/c 2>&1 (" + task.CommandLine + ")";
                        info.RedirectStandardOutput = true;
                        info.RedirectStandardInput = true;
                        info.RedirectStandardError = false;

                        process.StartInfo = info;
                        process.Start();

                        try
                        {
                            process.PriorityClass = ProcessPriorityClass.BelowNormal;
                        }
                        catch
                        {
                            Debug.WriteLine("Failed to set process priority class.");
                        }

                        if (Program._JobObjectHandle != IntPtr.Zero)
                        {
                            if (!Kernel32.AssignProcessToJobObject(Program._JobObjectHandle, process.Handle))
                                WriteLine("FAILED to assign process to job: " + Kernel32.GetLastErrorText());
                        }

                        try
                        {
                            process.ProcessorAffinity = (IntPtr)this.AffinityMask;
                        }
                        catch (Exception ex)
                        {
                            WriteLine("Failed to set processor affinity of child process.");
                            Program.ShowException(ex);
                        }
                        process.StandardInput.Close();

                        for (; ; )
                        {
                            string line = process.StandardOutput.ReadLine();
                            if (line == null)
                                break;

                            WriteLine(line);
                        }

                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            this.ErrorCount++;
                            WriteLine("Process exited with error code {0} {0:x8}.", process.ExitCode);
                            task.Succeeded = false;
                            task.Error = new Exception(String.Format("Process exited with error code {0}.", process.ExitCode));
                        }
                        else
                        {
                            task.Succeeded = true;
                            task.Error = null;
                        }
                    }

                }
                catch (Exception ex)
                {
                    WriteLine(Prefix + "Task FAILED: " + ex.Message);
                    lock (_lock)
                    {
                        this.ErrorCount++;
                    }

                    task.Succeeded = false;
                    task.Error = ex;
                }

                lock (_lock)
                {
                    Program._tasksCompletedCount++;
                    this.CurrentTask = null;
                }

                Program._workerFinishedQueue.Enqueue(task);
            }

            lock (_lock)
            {
                this.CurrentTask = null;
            }

            // WriteLine("Done.");
        }

        Task GetTask()
        {
            Task task;

            if (Program._workerTaskQueue.Dequeue(out task))
                return task;
            else
                return null;
        }

        object _lock
        {
            get { return Program.SyncLock; }
        }

        void WriteLine(string msg)
        {
            Console.WriteLine(Prefix + msg);
        }

        void WriteLine(string format, params object[] args)
        {
            WriteLine(String.Format(format, args));
        }
    }
}
