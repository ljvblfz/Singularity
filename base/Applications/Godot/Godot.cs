///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   Godot.cs
//
//  Note:   Singularity wait primitives test program.
//
using Microsoft.Singularity.V1.Services;
using Microsoft.Singularity.V1.Threads;
using System;
using System.Runtime.CompilerServices;
using System.Threading;


using Microsoft.Contracts;
using Microsoft.SingSharp.Reflection;
using Microsoft.Singularity.Applications;
using Microsoft.Singularity.Channels;
using Microsoft.Singularity.Io;
using Microsoft.Singularity.Configuration;
[assembly: Transform(typeof(ApplicationResourceTransform))]

namespace Microsoft.Singularity.Applications {
    [ConsoleCategory(HelpMessage="Show attributes associated with a file", DefaultAction=true)]
    internal class Parameters {
        [InputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Exp:READY> Stdin;

        [OutputEndpoint("data")]
        public readonly TRef<UnicodePipeContract.Imp:READY> Stdout;

        [LongParameter( "t", Default=2, HelpMessage="Number of Waiters (threads)")]
        internal long numberOfWaiters;

        reflective internal Parameters();

        internal int AppMain() {
            return Godot.AppMain(this);
        }
    }
    
    public class Godot
    {
        private static Mutex! mutex;
        private static WaitHandle[]! waiters;
        private static WaitHandle[]! others;
        private static String[]! names;
        private static WaiterThread[] threadDetails;
        private static Thread[] threads;

        private class WaiterThread {
            private uint myIndex;

            public WaiterThread(uint index) {
                myIndex = index;
            }

            public void Go()
            {
                String myName = names[myIndex];

                Console.Write("Enter Thread {0}\n", myName);

                Console.Write("  {0}: signaling auto-reset event\n", myName);
                ((AutoResetEvent!)waiters[myIndex]).Set();

                for (uint Loop = 0; Loop < 5; Loop++) {
                    Console.Write("  {0}: waiting for mutex\n", myName);
                    mutex.WaitOne();
                    Console.Write("  {0}: got mutex\n", myName);

                    Thread.Sleep(1000);

                    Console.Write("  {0}: releasing mutex\n", myName);
                    mutex.ReleaseMutex();
                    Thread.Yield();
                }

                //
                // Tell other waiters we're done.
                //
                Console.Write("  {0}: signaling manual-reset event\n", myName);
                ((ManualResetEvent!)others[myIndex]).Set();

                //
                // Wait for other waiter.
                //
                Console.Write("  {0}: waiting for other waiters\n", myName);
                foreach (WaitHandle! other in others) {
                    other.WaitOne();
                }
                Console.Write("  {0}: heard from other waiters\n", myName);

                Console.Write("Exit Thread {0}\n", myName);
            }
        }

        public static void Usage()
        {
            Console.WriteLine("\nUsage: godot [NumberOfWaitThreads]\n\n");
        }

        internal static int AppMain(Parameters! config)
        {
            uint numberOfWaiters = (uint) config.numberOfWaiters;

            Console.Write("\nStarting wait test with {0} wait threads\n\n",
                          numberOfWaiters);

            names = new String[4];
            names[0] = "Estragon";
            names[1] = "Vladimir";
            names[2] = "Lucky";
            names[3] = "Pozzo";

            //
            // Create some synchronization primitives to test.
            //
            mutex = new Mutex(true);
            waiters = new WaitHandle[numberOfWaiters];
            others = new WaitHandle[numberOfWaiters];
            for (uint Loop = 0; Loop < numberOfWaiters; Loop++) {
                waiters[Loop] = new AutoResetEvent(false);
                others[Loop] = new ManualResetEvent(false);
            }

            Console.Write("Created synchronization primitives\n\n");

            //
            // Fire up the waiters.
            //
            threads = new Thread[numberOfWaiters];
            threadDetails = new WaiterThread[numberOfWaiters];
            for (uint Loop = 0; Loop < numberOfWaiters; Loop++) {
                threadDetails[Loop] = new WaiterThread(Loop);
                threads[Loop] = new Thread(
                    new ThreadStart(threadDetails[Loop].Go));
                ((!)threads[Loop]).Start();
            }

            //
            // Wait for the waiters to tell us they're about to start waiting.
            //
            Console.Write("Waiting for all waiters to start\n");
            foreach (WaitHandle! waiter in waiters) {
                waiter.WaitOne();
            }

            //
            // Release the Mutex to the wolves.
            //
            Console.Write("About to release mutex\n");
            mutex.ReleaseMutex();
            Console.Write("Mutex released\n");

            //
            // Wait for the threads to die.
            //
#if false
#if NOT_YET
            Console.Write("Waiting for all waiters to terminate\n");
            for (uint Loop = 0; Loop < numberOfWaiters; Loop++) {
                threads[Loop].Join();
            }
#else
            Console.Write("Waiting for 30 sec while threads play\n");
            Thread.Sleep(30000);
#endif
#endif

            Console.Write("Goodbye\n");
            return 0;
        }
    }
}
