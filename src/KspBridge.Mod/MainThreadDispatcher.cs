using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace KspBridge
{
    /// <summary>Runs work queued from HTTP threads on the Unity main thread (Pump is called from Update).</summary>
    public sealed class MainThreadDispatcher
    {
        private const int Pending = 0;
        private const int Running = 1;
        private const int Cancelled = 2;

        private sealed class WorkItem
        {
            public Func<object> Fn;
            public object Result;
            public Exception Error;
            public int State = Pending;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private readonly ConcurrentQueue<WorkItem> _queue = new ConcurrentQueue<WorkItem>();

        public object Invoke(Func<object> fn, TimeSpan timeout)
        {
            WorkItem item = new WorkItem { Fn = fn };
            _queue.Enqueue(item);
            if (!item.Done.Wait(timeout))
            {
                // Cancel only if the main thread has not picked it up; otherwise wait for the result.
                if (Interlocked.CompareExchange(ref item.State, Cancelled, Pending) == Pending)
                {
                    item.Done.Dispose();
                    throw new ApiException(504, "timeout", "the game main thread did not respond within " + timeout.TotalSeconds + "s");
                }
                item.Done.Wait();
            }
            item.Done.Dispose();
            if (item.Error != null)
            {
                ExceptionDispatchInfo.Capture(item.Error).Throw();
            }
            return item.Result;
        }

        /// <summary>Executes all queued work. Main thread only.</summary>
        public void Pump()
        {
            while (_queue.TryDequeue(out WorkItem item))
            {
                if (Interlocked.CompareExchange(ref item.State, Running, Pending) != Pending)
                {
                    continue;
                }
                try
                {
                    item.Result = item.Fn();
                }
                catch (Exception e)
                {
                    item.Error = e;
                }
                finally
                {
                    item.Done.Set();
                }
            }
        }

        /// <summary>Fails every waiting request (used on shutdown).</summary>
        public void FailAll(string message)
        {
            while (_queue.TryDequeue(out WorkItem item))
            {
                if (Interlocked.CompareExchange(ref item.State, Running, Pending) != Pending)
                {
                    continue;
                }
                item.Error = new ApiException(500, "internal", message);
                item.Done.Set();
            }
        }
    }
}
