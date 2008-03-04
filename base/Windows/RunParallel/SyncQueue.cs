///////////////////////////////////////////////////////////////////////////////
//
//  Microsoft Research Singularity
//
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//
///////////////////////////////////////////////////////////////////////////////

using System;
using System.Threading;

// Until we can safely assume that everyone who builds Singularity has CLR2,
// we'll use non-generic collections.

namespace RunParallel
{
#if CLR2
    using System.Collections.Generic;

    public class SyncQueue<T>
    {
        internal readonly object _lock = new object();

        internal readonly Queue<T> _queue = new Queue<T>();

        internal bool _is_closed;

        internal int _waiter_count;

        public void Enqueue(T item)
        {
            lock (_lock)
            {
                _queue.Enqueue(item);
                if (_waiter_count > 0)
                {
                    _waiter_count--;
                    Monitor.Pulse(_lock);
                }
            }
        }

        public bool Dequeue(out T item)
        {
            lock (_lock)
            {
                for (; ; )
                {
                    if (_queue.Count == 0)
                    {
                        if (_is_closed)
                        {
                            item = default(T);
                            return false;
                        }

                        _waiter_count++;
                        Monitor.Wait(_lock);
                    }
                    else
                    {
                        item = _queue.Dequeue();
                        return true;
                    }
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                _is_closed = true;
                Monitor.PulseAll(_lock);
            }
        }
    }

#else
    using System.Collections;

    class SyncQueue_of_Task
    {
        internal readonly object _lock = new object();

        internal readonly Queue _queue = new Queue();

        internal bool _is_closed;

        internal int _waiter_count;

        public void Enqueue(Task item)
        {
            lock (_lock)
            {
                _queue.Enqueue(item);
                if (_waiter_count > 0)
                {
                    _waiter_count--;
                    Monitor.Pulse(_lock);
                }
            }
        }

        public bool Dequeue(out Task item)
        {
            lock (_lock)
            {
                for (; ; )
                {
                    if (_queue.Count == 0)
                    {
                        if (_is_closed)
                        {
                            item = null;
                            return false;
                        }

                        _waiter_count++;
                        Monitor.Wait(_lock);
                    }
                    else
                    {
                        item = (Task)_queue.Dequeue();
                        return true;
                    }
                }
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                _is_closed = true;
                Monitor.PulseAll(_lock);
            }
        }
    }

#endif
}

