///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   MonitorTest.cs
//
//  Note:   Some basic tests of the monitor code.
//

using System;
using System.Threading;

using Microsoft.Singularity.UnitTest;

using Microsoft.Singularity.Channels;
using Microsoft.Contracts;
using Microsoft.SingSharp.Reflection;
using Microsoft.Singularity.Applications;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Configuration;
[assembly: Transform(typeof(ApplicationResourceTransform))]

namespace Microsoft.Singularity.Applications {
    [ConsoleCategory(DefaultAction=true)]
    internal class Parameters {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        reflective internal Parameters();

        internal int AppMain() {
            return MonitorTest.AppMain(this);
        }
    }

    public class MonitorTest
    {
        internal static int AppMain(Parameters! config)
        {
            for (int i = 0; i < 2; i++) {
                UnitTest.Add("Many Threads PulseAll",
                             new UnitTest.TestDelegate(PulseAllTest.ManyThreadsTest));

                UnitTest.Add("Few Threads Pulse",
                             new UnitTest.TestDelegate(PulseAllTest.FewThreadsTest));

                UnitTest.Add("Low-density Pulse",
                             new UnitTest.TestDelegate(PulseTest.LowDensityTest));
                UnitTest.Add("Medium-density Pulse",
                             new UnitTest.TestDelegate(PulseTest.MediumDensityTest));
                UnitTest.Add("High-density Pulse",
                             new UnitTest.TestDelegate(PulseTest.HighDensityTest));
            }
            return ((UnitTest.Run(true) == UnitTest.Result.Passed) &&
                    Assert.Failures == 0) ? 0 : 1;
        }
    }
}
