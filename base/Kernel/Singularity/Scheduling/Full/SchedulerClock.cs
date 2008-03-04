////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   SchedulerClock.cs
//
//  Note:
//

using System;
using System.Diagnostics;
using Microsoft.Singularity;

namespace Microsoft.Singularity.Scheduling
{
    /// <summary>
    /// Interface for the system clock device.
    /// </summary>
    public class SchedulerClock
    {
        public static readonly DateTime BootTime = new DateTime(0);
        public static DateTime LastTime = new DateTime(0);

        public static DateTime GetUpTime()
        {
            LastTime = SystemClock.GetKernelTime();
            return LastTime;
        }

        //Simulator Use
        //Increments the time
        public static void SimulateTime(TimeSpan diff)
        {
            return;
        }

        //Simulator Use
        //Returns the time between now and the nextTimerInterrupt
        public static TimeSpan TimeToInterrupt()
        {
            return Processor.CurrentProcessor.NextTimerInterrupt - GetUpTime();
        }

        //Simulator Use
        //Sets the nextTimerInterrupt
        public static bool SetNextInterrupt(DateTime time)
        {
            long span = (time - GetUpTime()).Ticks;
            if (span > Processor.CurrentProcessor.Timer.MaxInterruptInterval) {
                span = Processor.CurrentProcessor.Timer.MaxInterruptInterval;
            }

            if (span < Processor.CurrentProcessor.Timer.MinInterruptInterval) {
#if false
                DebugStub.Print("SetNextInterrupt warning: requested span {0} " +
                                " < MinInterruptInterval {1}" +
                                " at cycle count {2}\n",
                                __arglist(
                                    span,
                                    Processor.CurrentProcessor.Timer.MinInterruptInterval,
                                    Kernel.GetCpuCycleCount()));
#endif
                span = Processor.CurrentProcessor.Timer.MinInterruptInterval;
            }

            bool success = Processor.CurrentProcessor.Timer.SetNextInterrupt(span);
            //Debug.Print("SetNextInterrupt -- Span: ");
            //Debug.Print(span);
            //Debug.Print(" End: ");
            //Debug.Print(time.Ticks);
            //Debug.Print(" Success: ");
            //Debug.Print((success?1:0));
            //Debug.Print("\n");
            if (success) {
                Processor.CurrentProcessor.NextTimerInterrupt = time;
                Scheduler.TimerInterruptedFlag = false;
            }
            return success;
        }

        //Allow timer periods longer than timer.MaxInterruptInterval
        public static void CheckInterrupt()
        {
            long span = (Processor.CurrentProcessor.NextTimerInterrupt - GetUpTime()).Ticks;
            if (span < Processor.CurrentProcessor.Timer.MinInterruptInterval) {
                return;
            }
            SetNextInterrupt(Processor.CurrentProcessor.NextTimerInterrupt);
            return;
        }

        // need to tell other processors an important scheduling change
        //       has been made.
        // OPEN QUESTION: Do we need to tell all processors, or just the ones
        //       which are idle?
        public static void SignalOtherProcessors()
        {
            // needs implementation.
        }
    }
}
