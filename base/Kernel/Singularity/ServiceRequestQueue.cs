////////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
//  File:   ServiceRequestQueue.cs
//
//  Note:
//

using System.Threading;

namespace Microsoft.Singularity
{
    public class ServiceRequestQueue
    {
        // Invariant: head == null <==> tail == null
        // tail.next is undefined.
        private ServiceRequest head = null;
        private ServiceRequest tail = null;
        private SpinLock spinLock;
        private AutoResetEvent enqueueEvent = new AutoResetEvent(false);

        // Insert an element at the tail of the queue.
        public void Enqueue(ServiceRequest req)
        {
            bool iflag = Processor.DisableInterrupts();
            spinLock.Acquire();
            try {
                if (head == null) {
                    head = req;
                    tail = req;
                }
                else {
                    tail.next = req;
                    tail = req;
                }
                enqueueEvent.Set();
            }
            finally {
                spinLock.Release();
                Processor.RestoreInterrupts(iflag);
            }
        }

        // Block while the queue is empty, then return the head of the queue.
        public ServiceRequest Dequeue()
        {
            while (true) {
                bool iflag = Processor.DisableInterrupts();
                spinLock.Acquire();
                try {
                    if (tail != null)
                    {
                        ServiceRequest req = head;
                        if (req != tail) {
                            head = req.next;
                        }
                        else {
                            head = null;
                            tail = null;
                        }
                        return req;
                    }
                }
                finally {
                    spinLock.Release();
                    Processor.RestoreInterrupts(iflag);
                }
                enqueueEvent.WaitOne();
            }
        }
    }
}
