//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//

public abstract class ThreadStart
{
    public abstract void Run();
}

public class Thread
{
    internal uint id;
    internal ThreadStart start;
    internal bool alive = true;
    internal Thread nextInQueue;

    internal Thread(uint id, ThreadStart start)
    {
        this.id = id;
        this.start = start;
    }
}

public class Semaphore
{
    internal ThreadQueue waiters;
    internal volatile int capacity;

    internal Semaphore(ThreadQueue waiters, int capacity)
    {
        this.waiters = waiters;
        this.capacity = capacity;
    }

    public void Wait()
    {
        CompilerIntrinsics.Cli();
        capacity--;
        if (capacity < 0)
        {
            Kernel.kernel.EnqueueAndYield(waiters);
        }
        CompilerIntrinsics.Sti();
    }

    public void Signal()
    {
        CompilerIntrinsics.Cli();
        capacity++;
        Thread t = waiters.Dequeue();
        if (t != null)
        {
            ThreadQueue ready = Kernel.kernel.readyQueue;
            ready.Enqueue(t);
            Kernel.kernel.EnqueueAndYield(ready);
        }
        CompilerIntrinsics.Sti();
    }
}

internal class ThreadQueue
{
    // circular list based on Thread.nextInQueue
    // tail.nextInQueue points to the head
    internal volatile Thread tail;

    internal void Enqueue(Thread t)
    {
        Thread tl = tail;
        if (tl == null)
        {
            t.nextInQueue = t;
            tail = t;
        }
        else
        {
            Thread hd = tl.nextInQueue;
            tl.nextInQueue = t;
            t.nextInQueue = hd;
            tail = t;
        }
    }

    internal Thread Dequeue()
    {
        Thread tl = tail;
        if (tl == null)
        {
            return null;
        }
        else
        {
            Thread hd = tl.nextInQueue;
            if (hd == tl)
            {
                tail = null;
                return hd;
            }
            else
            {
                tl.nextInQueue = hd.nextInQueue;
                return hd;
            }
        }
    }
}

public class Shell: ThreadStart
{
    Semaphore keyboardWaiter;
    public uint offset;
    public Shell(Semaphore keyboardWaiter, uint offset)
    {
        this.keyboardWaiter = keyboardWaiter;
        this.offset = offset;
    }

    public override void Run()
    {
        uint b = 0;
        while (true)
        {
            CompilerIntrinsics.Sti();
            NucleusCalls.DebugPrintHex(offset, b);
            (new int[1000])[0]++;
            b++;
            if (b == 100000)
            {
                b = 0;
                keyboardWaiter.Wait();
            }
        }
    }
}

public class KeyboardDriver: ThreadStart
{
    Kernel kernel;
    Semaphore[] listeners;

    public KeyboardDriver(Kernel kernel, Semaphore[] listeners)
    {
        this.kernel = kernel;
        this.listeners = listeners;
    }

    static BinaryTree t;
    ThreadStart b1;
    ThreadStart b2;

    public override void Run()
    {
        uint x = 80;
        uint listener = 0;
        Semaphore done = kernel.NewSemaphore(0);
        b1 = new BenchmarkAlloc(done);
        b2 = new BenchmarkAlloc2(done);

        while (true)
        {
            Kernel.kernel.Yield();
            uint key = NucleusCalls.TryReadKeyboard();
            if (key == 2)
            {
                // '1'
                kernel.NewThread(
                    new Kernel.BenchmarkYieldTo(kernel, done, 0));
                done.Wait();
            }
            else if (key == 3)
            {
                // '2'
                kernel.NewThread(
                    new BenchmarkSemaphore(kernel, done, 0));
                done.Wait();
            }
            else if (key == 4)
            {
                // '3'
                kernel.NewThread(b1);
                done.Wait();
            }
            else if (key == 5)
            {
                // '4'
                kernel.NewThread(b2);
                done.Wait();
            }
            else if (key == 6)
            {
                // '5'
                t = new BinaryTree(23); // 16 * 2^(23+1) = 16 * 16MB = 256MB
            }
            else if (key == 7)
            {
                // '6'
                //if (t != null) t.Flip();
                //t = null;
            }
            else if (key < 128)
            {
                //NucleusCalls.DebugPrintHex(60, key);
                NucleusCalls.VgaTextWrite(x, 0x2d00 + key);
                x++;
                listeners[listener].Signal();
                listener++;
                if (listener == listeners.Length)
                {
                    listener = 0;
                }
            }
        }
    }
}

public class BenchmarkSemaphore: ThreadStart
{
    Kernel kernel;
    Semaphore mySemaphore;
    Semaphore doneSemaphore; // only set for me == 0
    int me;
    BenchmarkSemaphore other;

    public BenchmarkSemaphore(Kernel kernel, Semaphore doneSemaphore, int me)
    {
        this.kernel = kernel;
        this.doneSemaphore = doneSemaphore;
        this.me = me;
        if (me == 0)
        {
            other = new BenchmarkSemaphore(kernel, null, 1);
            other.other = this;
            mySemaphore = kernel.NewSemaphore(0);
            other.mySemaphore = kernel.NewSemaphore(0);
        }
    }

    public override void Run()
    {
        int nIter = 1048576;
        if (me == 0)
        {
            kernel.NewThread(other);
            Semaphore s0 = mySemaphore;
            Semaphore s1 = other.mySemaphore;
            kernel.Yield();
            NucleusCalls.DebugPrintHex(50, 0);
            long t1 = NucleusCalls.Rdtsc();
            for (int i = 0; i < nIter; i++)
            {
                s1.Signal();
                s0.Wait();
            }
            long t2 = NucleusCalls.Rdtsc();
            uint diff = (uint)((t2 - t1) >> 20);
            NucleusCalls.DebugPrintHex(50, diff);
            doneSemaphore.Signal();
        }
        else
        {
            Semaphore s1 = mySemaphore;
            Semaphore s0 = other.mySemaphore;
            kernel.Yield();
            for (int i = 0; i < nIter; i++)
            {
                s1.Wait();
                s0.Signal();
            }
        }
    }
}

public class BenchmarkAlloc: ThreadStart
{
    Semaphore doneSemaphore;
    public BenchmarkAlloc(Semaphore doneSemaphore)
    {
        this.doneSemaphore = doneSemaphore;
    }

    public override void Run()
    {
        int nIter = 1048576;
        NucleusCalls.DebugPrintHex(50, 0);
        long t1 = NucleusCalls.Rdtsc();
        for (int i = 0; i < nIter; i++)
        {
            new BinaryTree(0);
        }
        long t2 = NucleusCalls.Rdtsc();
        uint diff = (uint)((t2 - t1) >> 20);
        NucleusCalls.DebugPrintHex(50, diff);
        doneSemaphore.Signal();
    }
}

public class BenchmarkAlloc2: ThreadStart
{
    Semaphore doneSemaphore;
    public BenchmarkAlloc2(Semaphore doneSemaphore)
    {
        this.doneSemaphore = doneSemaphore;
    }

    public override void Run()
    {
        int nIter = 65536;
        NucleusCalls.DebugPrintHex(50, 0);
        long t1 = NucleusCalls.Rdtsc();
        for (int i = 0; i < nIter; i++)
        {
            (new byte[1000])[0]++;
        }
        long t2 = NucleusCalls.Rdtsc();
        uint diff = (uint)((t2 - t1) >> 16);
        NucleusCalls.DebugPrintHex(50, diff);
        doneSemaphore.Signal();
    }
}

public class BinaryTree
{
    public BinaryTree left, right;
    public BinaryTree(int depth)
    {
        if (depth != 0)
        {
            left = new BinaryTree(depth - 1);
            right = new BinaryTree(depth - 1);
        }
    }
    public void Flip()
    {
        BinaryTree t = left;
        left = right;
        right = t;
        if (left != null) left.Flip();
        if (right != null) right.Flip();
    }
}

public class Kernel
{
    internal static Kernel kernel;
    internal static uint CurrentThread;

    // Thread 0 is the kernel private thread
    // Threads 1...NUM_THREADS-1 are user threads
    internal const int NUM_THREADS = 64;
    internal Thread[] threadTable = new Thread[Kernel.NUM_THREADS];
    // Threads ready to run:
    internal ThreadQueue readyQueue = new ThreadQueue();
    // Threads waiting to be garbage collected:
    internal ThreadQueue collectionQueue = new ThreadQueue();
    internal volatile bool collectionRequested;

    internal const int STACK_EMPTY = 0;
    internal const int STACK_RUNNING = 1;
    internal const int STACK_YIELDED = 2;
    internal const int STACK_INTERRUPTED = 3;

    // Entry point for initial threads and new threads.
    // The TAL checker verifies that Main has type ()->void and that Main never returns.
    private static void Main()
    {
        uint id = CurrentThread;
        if (id == 0)
        {
            kernel = new Kernel();
            kernel.KernelMain();
        }
        else
        {
            kernel.ThreadMain(id);
        }
        NucleusCalls.DebugPrintHex(0, 0xdead0001);
        while(true) {}
    }

    private void KernelMain()
    {
        Semaphore[] semaphores = new Semaphore[4];
        for (int i = 0; i < semaphores.Length; i++) {
            Semaphore s = new Semaphore(new ThreadQueue(), 0);
            semaphores[i] = s;
            _NewThread(new Shell(s, (uint)(10 + 10 * i)));
        }
        _NewThread(new KeyboardDriver(this, semaphores));
        uint x = 0xe100;
        while (true)
        {
            NucleusCalls.VgaTextWrite(479, x);
            x++;

            // Schedule thread, wait for exception or interrupt
            ScheduleNextThread();

            // CurrentThread received exception, interrupt, or exited voluntarily
            uint cid = CurrentThread;
            Thread t = threadTable[cid];

            uint cState = NucleusCalls.GetStackState(cid);
            if (cState == STACK_EMPTY)
            {
                // Thread received an exception.  Kill it.
                t.alive = false;
            }

            if (t.alive)
            {
                // Thread was interrupted.  It's still ready to run.
                NucleusCalls.SendEoi();
                NucleusCalls.StartTimer(0);
                readyQueue.Enqueue(t);
            }
            else
            {
                // Thread is dead.  Dead threads always jump back to stack 0.
                threadTable[cid] = null;
                NucleusCalls.ResetStack(cid);
            }
        }
    }

    internal void EnqueueAndYield(ThreadQueue queue)
    {
        uint cid = CurrentThread;
        Thread current = threadTable[cid];
        queue.Enqueue(current);
        ScheduleNextThread();

        // We're back.  Somebody yielded to us.
    }

    private static uint gcCount;

    // Switch to the next ready thread.
    // (This may be called from stack 0 or from any thread.)
    internal void ScheduleNextThread()
    {
        Thread t = readyQueue.Dequeue();
        if (t == null)
        {
            NucleusCalls.DebugPrintHex(70, ++gcCount);
            NucleusCalls.DebugPrintHex(60, 0);
            long t1 = NucleusCalls.Rdtsc();

            // No ready threads.
            // Make anyone waiting for GC ready:
            while (true)
            {
                t = collectionQueue.Dequeue();
                if (t == null)
                {
                    break;
                }
                readyQueue.Enqueue(t);
            }

            t = readyQueue.Dequeue();
            if (t == null)
            {
                // No threads to run.  The system is finished.
                NucleusCalls.DebugPrintHex(0, 0x76543210);
                while (true) {}
            }

            // Garbage collect, then we're ready to go.
            NucleusCalls.GarbageCollect();
            collectionRequested = false;

            long t2 = NucleusCalls.Rdtsc();
            uint diff = (uint)((t2 - t1) >> 10);
            NucleusCalls.DebugPrintHex(60, diff);
        }
        // Go to t.
        RunThread(t);

        // We're back.  Somebody (not necessarily t) yielded to us.
    }

    // Run a thread in a new scheduling quantum.
    private bool RunThread(Thread t)
    {
        uint id = t.id;
        CurrentThread = id;
        NucleusCalls.YieldTo(id);
        return true;
    }

    private void ThreadMain(uint id)
    {
        Thread t = threadTable[id];
        CompilerIntrinsics.Sti();
        t.start.Run();
        CompilerIntrinsics.Cli();
        t.alive = false;
        NucleusCalls.YieldTo(0);

        // Should never be reached:
        NucleusCalls.DebugPrintHex(0, 0xdead0002);
        while(true) {}
    }

    private Thread _NewThread(ThreadStart start)
    {
        for (uint i = 1; i < threadTable.Length; i++)
        {
            if (threadTable[i] == null)
            {
                Thread t = new Thread(i, start);
                threadTable[i] = t;
                readyQueue.Enqueue(t);
                return t;
            }
        }
        return null;
    }

    public Thread NewThread(ThreadStart start)
    {
        CompilerIntrinsics.Cli();
        Thread t = _NewThread(start);
        CompilerIntrinsics.Sti();
        return t;
    }

    public Semaphore NewSemaphore(int capacity)
    {
        CompilerIntrinsics.Cli();
        Semaphore s = new Semaphore(new ThreadQueue(), capacity);
        CompilerIntrinsics.Sti();
        return s;
    }

    public void Yield()
    {
        CompilerIntrinsics.Cli();
        if (collectionRequested)
        {
            EnqueueAndYield(collectionQueue);
        }
        else
        {
            EnqueueAndYield(readyQueue);
        }
        CompilerIntrinsics.Sti();
    }

    public static void Collect()
    {
        CompilerIntrinsics.Cli();
        kernel.collectionRequested = true;
        // Wait in the collectionQueue for the next GC.
        kernel.EnqueueAndYield(kernel.collectionQueue);
        // no sti needed here; we're returning to do another allocation anyway
    }

    public class BenchmarkYieldTo: ThreadStart
    {
        Kernel kernel;
        Semaphore doneSemaphore; // only set for me == 0
        int me;
        uint myId; // only set for me == 0
        BenchmarkYieldTo other;

        public BenchmarkYieldTo(Kernel kernel, Semaphore doneSemaphore, int me)
        {
            this.kernel = kernel;
            this.doneSemaphore = doneSemaphore;
            this.me = me;
            if (me == 0)
            {
                other = new BenchmarkYieldTo(kernel, null, 1);
                other.other = this;
            }
        }

        public override void Run()
        {
            CompilerIntrinsics.Cli();
            int nIter = 1048576;
            if (me == 0)
            {
                myId = Kernel.CurrentThread;
                Thread otherT = kernel._NewThread(other);
                uint otherId = otherT.id;
                kernel.Yield();
                CompilerIntrinsics.Cli();
                NucleusCalls.DebugPrintHex(50, 0);
                long t1 = NucleusCalls.Rdtsc();
                for (int i = 0; i < nIter; i++)
                {
                    NucleusCalls.YieldTo(otherId);
                }
                long t2 = NucleusCalls.Rdtsc();
                uint diff = (uint)((t2 - t1) >> 20);
                NucleusCalls.DebugPrintHex(50, diff);
                doneSemaphore.Signal();
            }
            else
            {
                uint otherId = other.myId;
                kernel.Yield();
                CompilerIntrinsics.Cli();
                for (int i = 0; i < nIter; i++)
                {
                    NucleusCalls.YieldTo(otherId);
                }
            }
        }
    }

    private static void Demo()
    {
        (new int[10])[5]++;
        (new Kernel[7])[3] = null;
        (new Kernel[7])[6] = new Kernel();

        if (CurrentThread == 0)
        {
            uint a = 0;
            while (true)
            {
                NucleusCalls.DebugPrintHex(10, 0xbababeef);

                CurrentThread = 1;
                NucleusCalls.StartTimer(0);
                NucleusCalls.YieldTo(CurrentThread);
                NucleusCalls.SendEoi();

                CurrentThread = 2;
                NucleusCalls.StartTimer(0);
                NucleusCalls.YieldTo(CurrentThread);
                NucleusCalls.SendEoi();

                NucleusCalls.DebugPrintHex(20, 0xbababeee);
                NucleusCalls.DebugPrintHex(30, a);
                a++;
            }
        }
        else if (CurrentThread == 1)
        {
            uint b = 0;
            while (true)
            {
                CompilerIntrinsics.Sti();
                NucleusCalls.DebugPrintHex(40, 0xfeedfade);
                NucleusCalls.DebugPrintHex(50, b);
                b++;
            }
        }
        else if(CurrentThread == 2)
        {
            uint x = 80;
            while (true)
            {
                CompilerIntrinsics.Sti();
                CompilerIntrinsics.Sti();
                CompilerIntrinsics.Sti();
                uint key = NucleusCalls.TryReadKeyboard();
                if (key < 256)
                {
                    NucleusCalls.DebugPrintHex(60, key);
                    NucleusCalls.VgaTextWrite(x, 0x2d00 + key);
                    x++;
                }
            }
        }
        else
        {
            throw null;
        }
    }
}

class NucleusCalls
{
    // These declarations are double-checked by the TAL checker
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static uint GetStackState(uint stackId);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void ResetStack(uint stackId);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void YieldTo(uint stackId);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void GarbageCollect();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void VgaTextWrite(uint position, uint data);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static uint TryReadKeyboard();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void StartTimer(uint freq);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void SendEoi();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static long Rdtsc();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void DebugPrintHex(uint screenOffset, uint hexMessage);
}

class CompilerIntrinsics
{
    // These declarations are used by the (untrusted) C# compiler
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void Cli();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
    internal extern static void Sti();
}
