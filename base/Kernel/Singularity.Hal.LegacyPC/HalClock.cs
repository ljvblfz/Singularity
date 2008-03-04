///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File: HalClock.cs
//
//  Note:
//
//  This file is an implementation of Interfaces/Hal/HalClock.csi
//
//  This is nothing more than a wrapper for the real-time clock.
//

using System;
using System.Runtime.CompilerServices;

namespace Microsoft.Singularity.Hal
{
    public class HalClock
    {
        private RTClock rtc;

        internal HalClock(RTClock rtc)
        {
            this.rtc = rtc;
        }

        public HalClock()
        {
            DebugStub.Assert(false);
        }

        public void Finalize()
        {
            rtc.Finalize();
        }

        [NoHeapAllocation]
        public void ClearInterrupt()
        {
            rtc.ClearInterrupt();
        }

        [NoHeapAllocation]
        public long GetKernelTicks()
        {
            return rtc.GetKernelTicks();
        }

        [NoHeapAllocation]
        public void CpuResumeFromHaltEvent()
        {
            rtc.CpuResumeFromHaltEvent();
        }

        [NoHeapAllocation]
        public long GetRtcTime()
        {
            return rtc.GetRtcTime();
        }

        public void SetRtcTime(long rtcTicks)
        {
            rtc.SetRtcTime(rtcTicks);
        }
    }
}
