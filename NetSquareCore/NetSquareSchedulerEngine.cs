using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Coordinates every scheduled action with one timer thread and a bounded set of reusable workers.
    /// </summary>
    internal static class NetSquareSchedulerEngine
    {
        #region Fields
        private const int MaximumWorkerCount = 16;
        private static readonly object InitializationLock = new object();
        private static readonly object RunnersLock = new object();
        private static readonly object ReadyQueueLock = new object();
        private static readonly List<NetSquareScheduledActionRunner> Runners = new List<NetSquareScheduledActionRunner>();
        private static readonly Queue<NetSquareScheduledActionRunner> ReadyQueue = new Queue<NetSquareScheduledActionRunner>();
        private static readonly AutoResetEvent ScheduleChanged = new AutoResetEvent(false);
        private static readonly Semaphore WorkAvailable = new Semaphore(0, int.MaxValue);
        private static int initialized;
        #endregion

        #region Registration
        /// <summary>
        /// Registers a runner with the shared scheduler and starts the fixed worker set on first use.
        /// </summary>
        /// <param name="runner">Runner to schedule.</param>
        public static void Register(NetSquareScheduledActionRunner runner)
        {
            if (runner == null)
                throw new ArgumentNullException(nameof(runner));

            EnsureInitialized();
            lock (RunnersLock)
            {
                if (!Runners.Contains(runner))
                    Runners.Add(runner);
            }
            ScheduleChanged.Set();
        }

        /// <summary>
        /// Removes a stopped runner from future scheduler scans.
        /// </summary>
        /// <param name="runner">Runner to remove.</param>
        public static void Unregister(NetSquareScheduledActionRunner runner)
        {
            if (runner == null)
                return;

            lock (RunnersLock)
                Runners.Remove(runner);
            ScheduleChanged.Set();
        }

        /// <summary>
        /// Wakes the timing coordinator after lifecycle or frequency changes.
        /// </summary>
        public static void SignalScheduleChanged()
        {
            ScheduleChanged.Set();
        }
        #endregion

        #region Workers
        /// <summary>
        /// Starts the coordinator and the bounded worker set exactly once.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (Volatile.Read(ref initialized) != 0)
                return;

            lock (InitializationLock)
            {
                if (initialized != 0)
                    return;

                int workerCount = Math.Max(
                    1,
                    Math.Min((Environment.ProcessorCount + 1) / 2, MaximumWorkerCount));
                for (int index = 0; index < workerCount; index++)
                {
                    Thread worker = new Thread(WorkerLoop);
                    worker.IsBackground = true;
                    worker.Name = "NetSquare scheduler worker " + index;
                    worker.Start();
                }

                Thread coordinator = new Thread(CoordinatorLoop);
                coordinator.IsBackground = true;
                coordinator.Name = "NetSquare scheduler coordinator";
                coordinator.Start();
                Volatile.Write(ref initialized, 1);
            }
        }

        /// <summary>
        /// Detects due actions and places each runner in the ready queue at most once.
        /// </summary>
        private static void CoordinatorLoop()
        {
            while (true)
            {
                int waitMilliseconds = QueueDueActions();
                ScheduleChanged.WaitOne(Math.Max(1, waitMilliseconds));
            }
        }

        /// <summary>
        /// Queues due runners and releases all local runner references before the coordinator waits.
        /// </summary>
        /// <returns>Delay before the next scheduler scan.</returns>
        private static int QueueDueActions()
        {
            int waitMilliseconds = 1000;
            lock (RunnersLock)
            {
                long nowTimestamp = Stopwatch.GetTimestamp();
                for (int index = 0; index < Runners.Count; index++)
                {
                    NetSquareScheduledActionRunner runner = Runners[index];
                    if (runner.TryQueue(nowTimestamp))
                    {
                        lock (ReadyQueueLock)
                            ReadyQueue.Enqueue(runner);
                        WorkAvailable.Release();
                        continue;
                    }

                    int runnerDelay = runner.GetDelayMilliseconds(nowTimestamp);
                    if (runnerDelay < waitMilliseconds)
                        waitMilliseconds = runnerDelay;
                }
            }

            return waitMilliseconds;
        }

        /// <summary>
        /// Executes ready actions without creating per-tick tasks, threads, or delegates.
        /// </summary>
        private static void WorkerLoop()
        {
            while (true)
            {
                WorkAvailable.WaitOne();
                ExecuteNextRunner();
            }
        }

        /// <summary>
        /// Executes one queued runner in a short-lived frame so idle workers retain no world callbacks.
        /// </summary>
        private static void ExecuteNextRunner()
        {
            NetSquareScheduledActionRunner runner = null;
            lock (ReadyQueueLock)
            {
                if (ReadyQueue.Count > 0)
                    runner = ReadyQueue.Dequeue();
            }

            runner?.ExecuteQueued();
        }
        #endregion
    }
}
#endregion