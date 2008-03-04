////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ServiceThread.cs
//
//  Note:
//

using System.Threading;

namespace Microsoft.Singularity
{
    public class ServiceThread
    {
        private static ServiceRequestQueue queue;

        internal static void Initialize()
        {
            queue = new ServiceRequestQueue();
            Thread.CreateThread(Thread.CurrentProcess, new ThreadStart(ServiceLoop)).Start();
        }

        internal static void Request(ServiceRequest req)
        {
            queue.Enqueue(req);
        }

        private static void ServiceLoop()
        {
            while (true) {
                ServiceRequest req = queue.Dequeue();
                req.Service();
            }
        }
    }
}
