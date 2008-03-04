///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

using System;

namespace RunParallel
{
#if CLR2
    using System.Collections.Generic;

    class Queue_of_Task : Queue<Task>
    {
    }

    class SyncQueue_of_Task : SyncQueue<Task>
    {
    }

    class List_of_WorkerThread : List<WorkerThread>
    {
    }

#else
    using System.Collections;

    class Queue_of_Task
    {
        readonly Queue _queue = new Queue();

        public void Enqueue(Task item)
        {
            _queue.Enqueue(item);
        }

        public Task Dequeue()
        {
            return (Task)_queue.Dequeue();
        }

        public int Count
        {
            get { return _queue.Count; }
        }
    }

    class List_of_WorkerThread : System.Collections.ArrayList
    {
        public new WorkerThread[] ToArray()
        {
            return (WorkerThread[])base.ToArray(typeof(WorkerThread));
        }
    }

#endif
}

