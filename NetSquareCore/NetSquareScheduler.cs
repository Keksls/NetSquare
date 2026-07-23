using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// The scheduller allow to to register actions that will be invoked at a fixed frequency
    /// </summary>
    public static class NetSquareScheduler
    {
        /// <summary>
        /// Gets or sets the scheduled actions value.
        /// </summary>
        private static Dictionary<string, NetSquareScheduledActionRunner> ScheduledActions { get; set; }
        /// <summary>
        /// Coordinates concurrent access to the global scheduled-action registry.
        /// </summary>
        private static readonly object ScheduledActionsLock = new object();

        /// <summary>
        /// Instantiatre a new Scheduler
        /// </summary>
        static NetSquareScheduler()
        {
            ScheduledActions = new Dictionary<string, NetSquareScheduledActionRunner>();
        }

        /// <summary>
        /// Check if an action is already registered with this name
        /// </summary>
        /// <param name="actionName">name of the action to check</param>
        /// <returns>true if exists</returns>
        public static bool HasAction(string actionName)
        {
            lock (ScheduledActionsLock)
                return ScheduledActions.ContainsKey(actionName);
        }

        /// <summary>
        /// Register an action to the scheduler
        /// </summary>
        /// <param name="name">name of the action</param>
        /// <param name="frequency">frequency in ms</param>
        /// <param name="enableSmartFrequencyAdjusting">if enabled, the action will be raised according to the duration of the last loop turn</param>
        /// <param name="callback">callback to invoke each loop turn</param>
        /// <returns>true if success</returns>
        public static bool AddAction(string name, int frequency, bool enableSmartFrequencyAdjusting, Action callback)
        {
            return AddAction(new NetSquareScheduledAction(name, frequency, enableSmartFrequencyAdjusting, callback));
        }

        /// <summary>
        /// Register an action to the scheduler
        /// </summary>
        /// <param name="name">name of the action</param>
        /// <param name="frequency">frequency in Hz</param>
        /// <param name="enableSmartFrequencyAdjusting">if enabled, the action will be raised according to the duration of the last loop turn</param>
        /// <param name="callback">callback to invoke each loop turn</param>
        /// <returns>true if success</returns>
        public static bool AddAction(string name, float frequency, bool enableSmartFrequencyAdjusting, Action callback)
        {
            return AddAction(new NetSquareScheduledAction(name, GetMsFrequencyFromHz(frequency), enableSmartFrequencyAdjusting, callback));
        }

        /// <summary>
        /// Register an action to the scheduler
        /// </summary>
        /// <param name="action">action to register</param>
        /// <returns>true if success</returns>
        public static bool AddAction(NetSquareScheduledAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            lock (ScheduledActionsLock)
            {
                if (ScheduledActions.ContainsKey(action.Name))
                    return false;
                ScheduledActions.Add(action.Name, new NetSquareScheduledActionRunner(action));
                return true;
            }
        }

        /// <summary>
        /// Remove an action from the scheduler
        /// </summary>
        /// <param name="actionName">name of the action to remove</param>
        /// <returns>true if removed</returns>
        public static bool RemoveAction(string actionName)
        {
            NetSquareScheduledActionRunner runner;
            lock (ScheduledActionsLock)
            {
                if (!ScheduledActions.TryGetValue(actionName, out runner))
                    return false;
                ScheduledActions.Remove(actionName);
            }

            if (runner.IsRunning)
                runner.StopAction();
            runner.Detach();
            return true;
        }

        /// <summary>
        /// Start a scheduled action
        /// </summary>
        /// <param name="actionName">name of the action to start</param>
        /// <returns>true if started</returns>
        public static bool StartAction(string actionName)
        {
            NetSquareScheduledActionRunner runner;
            lock (ScheduledActionsLock)
            {
                if (!ScheduledActions.TryGetValue(actionName, out runner))
                    return false;
            }
            return runner.StartAction();
        }

        /// <summary>
        /// Stop a scheduled action
        /// </summary>
        /// <param name="actionName">name of the action to stop</param>
        /// <returns>true if stoped</returns>
        public static bool StopAction(string actionName)
        {
            NetSquareScheduledActionRunner runner;
            lock (ScheduledActionsLock)
            {
                if (!ScheduledActions.TryGetValue(actionName, out runner))
                    return false;
            }
            return runner.StopAction();
        }

        /// <summary>
        /// Start all registered actions from the scheduler
        /// </summary>
        /// <returns>nb of actions started</returns>
        public static int StartAllActions()
        {
            List<NetSquareScheduledActionRunner> runners;
            lock (ScheduledActionsLock)
                runners = new List<NetSquareScheduledActionRunner>(ScheduledActions.Values);

            int nbStarted = 0;
            foreach (NetSquareScheduledActionRunner runner in runners)
                if (runner.StartAction())
                    nbStarted++;
            return nbStarted;
        }

        /// <summary>
        /// Stop all actions from the scheduler
        /// </summary>
        /// <returns>nb of actions stopped</returns>
        public static int StopAllActions()
        {
            List<NetSquareScheduledActionRunner> runners;
            lock (ScheduledActionsLock)
                runners = new List<NetSquareScheduledActionRunner>(ScheduledActions.Values);

            int nbStopped = 0;
            foreach (NetSquareScheduledActionRunner runner in runners)
                if (runner.StopAction())
                    nbStopped++;
            return nbStopped;
        }

        /// <summary>
        /// Get the frequency in ms from a frequency in Hz
        /// </summary>
        /// <param name="frequency"> frequency in Hz</param>
        /// <returns> frequency in ms</returns>
        public static int GetMsFrequencyFromHz(float frequency)
        {
            // clamp frequency
            if (frequency <= 0f)
                frequency = 0.1f;
            if (frequency > 30f)
                frequency = 30f;
            // set frequency
            return (int)((1f / frequency) * 1000f);
        }

        /// <summary>
        /// Set the frequency of an action
        /// </summary>
        /// <param name="actionName"> name of the action</param>
        /// <param name="frequencyHz"> frequency in Hz</param>
        public static void SetSchedulerFrequency(string actionName, float frequencyHz)
        {
            NetSquareScheduledActionRunner runner;
            lock (ScheduledActionsLock)
            {
                if (!ScheduledActions.TryGetValue(actionName, out runner))
                    return;
            }
            runner.UpdateFrequency(GetMsFrequencyFromHz(frequencyHz));
        }

        /// <summary>
        /// Set the frequency of an action
        /// </summary>
        /// <param name="actionName"> name of the action</param>
        /// <param name="frequencyMs"> frequency in Ms</param>
        public static void SetSchedulerFrequency(string actionName, int frequencyMs)
        {
            NetSquareScheduledActionRunner runner;
            lock (ScheduledActionsLock)
            {
                if (!ScheduledActions.TryGetValue(actionName, out runner))
                    return;
            }
            runner.UpdateFrequency(frequencyMs);
        }

    }

    /// <summary>
    /// Represents one callback and its scheduler timing settings.
    /// </summary>
    public class NetSquareScheduledAction
    {
        /// <summary>
        /// Name of the scheduled action
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Frequency of the action
        /// </summary>
        public int Frequency { get; set; }
        /// <summary>
        /// Enable frequency adjusting for being more regular if there is slow operations in the loop
        /// </summary>
        public bool SmartFrequencyAdjusting { get; set; }
        /// <summary>
        /// the action to schedule
        /// </summary>
        public Action Callback { get; set; }

        /// <summary>
        /// Instantiate a new Scheduled Action
        /// </summary>
        /// <param name="name">name of the action</param>
        /// <param name="frequency">frequency in ms</param>
        /// <param name="enableSmartFrequencyAdjusting">if enabled, the action will be raised according to the duration of the last loop turn</param>
        /// <param name="callback">callback to invoke each loop turn</param>
        public NetSquareScheduledAction(string name, int frequency, bool enableSmartFrequencyAdjusting, Action callback)
        {
            Name = name;
            Frequency = frequency;
            SmartFrequencyAdjusting = enableSmartFrequencyAdjusting;
            Callback = callback;
        }
    }

    /// <summary>
    /// Represents the net square scheduled action runner component.
    /// </summary>
    internal class NetSquareScheduledActionRunner
    {
        #region Fields
        [ThreadStatic]
        private static NetSquareScheduledActionRunner executingRunner;
        private readonly object lifecycleLock = new object();
        private readonly ManualResetEventSlim idleSignal = new ManualResetEventSlim(true);
        private long nextDueTimestamp;
        private int queuedOrExecuting;
        private int running;
        #endregion

        #region Properties and events
        /// <summary>
        /// Gets the scheduled action configuration.
        /// </summary>
        public NetSquareScheduledAction Action { get; private set; }

        /// <summary>
        /// Gets whether this action accepts future executions.
        /// </summary>
        public bool IsRunning { get { return Volatile.Read(ref running) != 0; } }

        /// <summary>
        /// Occurs after the scheduled callback executes successfully.
        /// </summary>
        public event Action OnDoAction;
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a runner managed by the shared scheduler engine.
        /// </summary>
        /// <param name="action">Scheduled action configuration.</param>
        public NetSquareScheduledActionRunner(NetSquareScheduledAction action)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }
        #endregion

        #region Lifecycle
        /// <summary>
        /// Starts the action and makes its first execution immediately due.
        /// </summary>
        /// <returns>True when the action transitioned to running.</returns>
        public bool StartAction()
        {
            lock (lifecycleLock)
            {
                if (IsRunning)
                    return false;

                Interlocked.Exchange(ref nextDueTimestamp, Stopwatch.GetTimestamp());
                Volatile.Write(ref running, 1);
            }

            NetSquareSchedulerEngine.Register(this);
            return true;
        }

        /// <summary>
        /// Stops future executions and waits for an active callback to finish.
        /// </summary>
        /// <returns>True when the action transitioned to stopped.</returns>
        public bool StopAction()
        {
            lock (lifecycleLock)
            {
                if (!IsRunning)
                    return false;
                Volatile.Write(ref running, 0);
            }

            NetSquareSchedulerEngine.SignalScheduleChanged();
            if (ReferenceEquals(executingRunner, this))
                return true;

            Stopwatch stopWatch = Stopwatch.StartNew();
            while (Volatile.Read(ref queuedOrExecuting) != 0)
            {
                int remainingMilliseconds = 5000 - (int)stopWatch.ElapsedMilliseconds;
                if (remainingMilliseconds <= 0 || !idleSignal.Wait(remainingMilliseconds))
                {
                    throw new TimeoutException(
                        "The scheduled action '" + Action.Name + "' did not stop within 5000 ms.");
                }
                Thread.Yield();
            }
            return true;
        }

        /// <summary>
        /// Removes this stopped runner from the shared timing coordinator.
        /// </summary>
        public void Detach()
        {
            NetSquareSchedulerEngine.Unregister(this);
        }

        /// <summary>
        /// Updates the interval and applies it to the next execution without restarting the runner.
        /// </summary>
        /// <param name="frequencyMilliseconds">New interval in milliseconds.</param>
        public void UpdateFrequency(int frequencyMilliseconds)
        {
            Action.Frequency = Math.Max(1, frequencyMilliseconds);
            Interlocked.Exchange(
                ref nextDueTimestamp,
                Stopwatch.GetTimestamp() + MillisecondsToTimestampTicks(Action.Frequency));
            NetSquareSchedulerEngine.SignalScheduleChanged();
        }
        #endregion

        #region Execution
        /// <summary>
        /// Executes the callback immediately on the caller thread.
        /// </summary>
        public void DoActionImmediate()
        {
            Action.Callback();
            OnDoAction?.Invoke();
        }

        /// <summary>
        /// Atomically reserves one due execution for the shared ready queue.
        /// </summary>
        /// <param name="nowTimestamp">Current stopwatch timestamp.</param>
        /// <returns>True when this runner must be enqueued.</returns>
        internal bool TryQueue(long nowTimestamp)
        {
            if (!IsRunning || nowTimestamp < Interlocked.Read(ref nextDueTimestamp))
                return false;
            if (Interlocked.CompareExchange(ref queuedOrExecuting, 1, 0) != 0)
                return false;
            if (!IsRunning)
            {
                Interlocked.Exchange(ref queuedOrExecuting, 0);
                return false;
            }

            idleSignal.Reset();
            return true;
        }

        /// <summary>
        /// Returns the remaining wait before this runner becomes due.
        /// </summary>
        /// <param name="nowTimestamp">Current stopwatch timestamp.</param>
        /// <returns>Remaining delay in milliseconds.</returns>
        internal int GetDelayMilliseconds(long nowTimestamp)
        {
            if (!IsRunning || Volatile.Read(ref queuedOrExecuting) != 0)
                return int.MaxValue;

            long remainingTicks = Interlocked.Read(ref nextDueTimestamp) - nowTimestamp;
            if (remainingTicks <= 0)
                return 0;

            long milliseconds = (remainingTicks * 1000L + Stopwatch.Frequency - 1L) / Stopwatch.Frequency;
            return milliseconds > int.MaxValue ? int.MaxValue : (int)milliseconds;
        }

        /// <summary>
        /// Executes one reserved callback and releases the runner for its next due time.
        /// </summary>
        internal void ExecuteQueued()
        {
            long startedTimestamp = Stopwatch.GetTimestamp();
            executingRunner = this;
            try
            {
                if (!IsRunning)
                    return;

                Action.Callback();
                OnDoAction?.Invoke();
            }
            catch (Exception exception)
            {
                // One failing callback stops only its own schedule and never kills a shared worker.
                Volatile.Write(ref running, 0);
                Trace.TraceError("Scheduled action '" + Action.Name + "' failed: " + exception);
            }
            finally
            {
                executingRunner = null;
                if (IsRunning)
                {
                    long completedTimestamp = Stopwatch.GetTimestamp();
                    long baseTimestamp = Action.SmartFrequencyAdjusting
                        ? startedTimestamp
                        : completedTimestamp;
                    Interlocked.Exchange(
                        ref nextDueTimestamp,
                        baseTimestamp + MillisecondsToTimestampTicks(Action.Frequency));
                }

                Interlocked.Exchange(ref queuedOrExecuting, 0);
                idleSignal.Set();
                NetSquareSchedulerEngine.SignalScheduleChanged();
            }
        }

        /// <summary>
        /// Converts a positive millisecond interval into stopwatch timestamp ticks.
        /// </summary>
        /// <param name="milliseconds">Interval in milliseconds.</param>
        /// <returns>Equivalent stopwatch timestamp ticks.</returns>
        private static long MillisecondsToTimestampTicks(int milliseconds)
        {
            return Math.Max(1L, (long)Math.Max(1, milliseconds) * Stopwatch.Frequency / 1000L);
        }
        #endregion
    }
}
#endregion
