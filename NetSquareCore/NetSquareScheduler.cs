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
            runner.Action.Frequency = GetMsFrequencyFromHz(frequencyHz);
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
            runner.Action.Frequency = frequencyMs;
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
        /// <summary>
        /// Gets or sets the action value.
        /// </summary>
        public NetSquareScheduledAction Action { get; private set; }
        /// <summary>
        /// Gets or sets the is running value.
        /// </summary>
        public bool IsRunning { get { return Volatile.Read(ref running) != 0; } }
        /// <summary>
        /// Occurs when do action is raised.
        /// </summary>
        public event Action OnDoAction;
        /// <summary>
        /// Stores the action thread value.
        /// </summary>
        private Thread actionThread;
        private readonly object lifecycleLock = new object();
        private readonly ManualResetEventSlim stopSignal = new ManualResetEventSlim(false);
        private int running;

        /// <summary>
        /// Executes the net square scheduled action runner operation.
        /// </summary>
        public NetSquareScheduledActionRunner(NetSquareScheduledAction action)
        {
            Action = action;
            Volatile.Write(ref running, 0);
            actionThread = null;
        }

        /// <summary>
        /// Start the action repetively
        /// </summary>
        public bool StartAction()
        {
            lock (lifecycleLock)
            {
                if (IsRunning)
                    return false;

                stopSignal.Reset();
                actionThread = Action.SmartFrequencyAdjusting
                    ? new Thread(SmartFrequencyActionLoop)
                    : new Thread(NormalFrequencyActionLoop);
                actionThread.IsBackground = true;
                actionThread.Name = "NetSquare scheduler " + Action.Name;
                Volatile.Write(ref running, 1);
                actionThread.Start();
                return true;
            }
        }

        /// <summary>
        /// Do the action now for once
        /// </summary>
        public void DoActionImmediate()
        {
            Action.Callback();
            OnDoAction?.Invoke();
        }

        /// <summary>
        /// stop the scheduled action
        /// </summary>
        public bool StopAction()
        {
            Thread threadToJoin;
            lock (lifecycleLock)
            {
                if (!IsRunning)
                    return false;

                Volatile.Write(ref running, 0);
                stopSignal.Set();
                threadToJoin = actionThread;
            }

            if (threadToJoin != null && threadToJoin != Thread.CurrentThread && !threadToJoin.Join(5000))
            {
                throw new TimeoutException(
                    "The scheduled action '" + Action.Name + "' did not stop within 5000 ms.");
            }
            return true;
        }

        /// <summary>
        /// Executes the smart frequency action loop operation.
        /// </summary>
        private void SmartFrequencyActionLoop()
        {
            Stopwatch stopwatch = new Stopwatch();
            try
            {
                while (IsRunning)
                {
                    stopwatch.Restart();
                    Action.Callback();
                    OnDoAction?.Invoke();
                    stopwatch.Stop();
                    int waitMilliseconds = (int)(Action.Frequency - stopwatch.ElapsedMilliseconds);
                    if (waitMilliseconds < 1)
                        waitMilliseconds = 1;
                    if (stopSignal.Wait(waitMilliseconds))
                        return;
                }
            }
            finally
            {
                Volatile.Write(ref running, 0);
            }
        }

        /// <summary>
        /// Executes the normal frequency action loop operation.
        /// </summary>
        private void NormalFrequencyActionLoop()
        {
            try
            {
                while (IsRunning)
                {
                    Action.Callback();
                    OnDoAction?.Invoke();
                    if (stopSignal.Wait(Math.Max(1, Action.Frequency)))
                        return;
                }
            }
            finally
            {
                Volatile.Write(ref running, 0);
            }
        }
    }
}
#endregion
