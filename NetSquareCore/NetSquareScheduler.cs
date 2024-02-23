using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetSquare.Core
{
    /// <summary>
    /// The scheduller allow to to register actions that will be invoked at a fixed frequency
    /// </summary>
    public static class NetSquareScheduler
    {
        private static Dictionary<string, NetSquareScheduledActionRunner> ScheduledActions { get; set; }

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
            if (ScheduledActions.ContainsKey(action.Name))
                return false;
            ScheduledActions.Add(action.Name, new NetSquareScheduledActionRunner(action));
            return true;
        }

        /// <summary>
        /// Remove an action from the scheduler
        /// </summary>
        /// <param name="actionName">name of the action to remove</param>
        /// <returns>true if removed</returns>
        public static bool RemoveAction(string actionName)
        {
            return ScheduledActions.Remove(actionName);
        }

        /// <summary>
        /// Start a scheduled action
        /// </summary>
        /// <param name="actionName">name of the action to start</param>
        /// <returns>true if started</returns>
        public static bool StartAction(string actionName)
        {
            if (!ScheduledActions.ContainsKey(actionName))
                return false;
            ScheduledActions[actionName].StartAction();
            return true;
        }

        /// <summary>
        /// Stop a scheduled action
        /// </summary>
        /// <param name="actionName">name of the action to stop</param>
        /// <returns>true if stoped</returns>
        public static bool StopAction(string actionName)
        {
            if (!ScheduledActions.ContainsKey(actionName))
                return false;
            ScheduledActions[actionName].StopAction();
            return true;
        }

        /// <summary>
        /// Start all registered actions from the scheduler
        /// </summary>
        /// <returns>nb of actions started</returns>
        public static int StartAllActions()
        {
            int nbStarted = 0;
            foreach (NetSquareScheduledActionRunner runner in ScheduledActions.Values)
                if(runner.StartAction())
                    nbStarted++;
            return nbStarted;
        }

        /// <summary>
        /// Stop all actions from the scheduler
        /// </summary>
        /// <returns>nb of actions stopped</returns>
        public static int StopAllActions()
        {
            int nbStopped = 0;
            foreach (NetSquareScheduledActionRunner runner in ScheduledActions.Values)
                if(runner.StopAction())
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
    }

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

    internal class NetSquareScheduledActionRunner
    {
        public NetSquareScheduledAction Action { get; private set; }
        public bool IsRunning { get; private set; }
        public event Action OnDoAction;
        private Thread actionThread;

        public NetSquareScheduledActionRunner(NetSquareScheduledAction action)
        {
            Action = action;
            IsRunning = false;
            actionThread = null;
        }

        /// <summary>
        /// Start the action repetively
        /// </summary>
        public bool StartAction()
        {
            if (IsRunning)
                return false;

            if (Action.SmartFrequencyAdjusting)
                actionThread = new Thread(SmartFrequencyActionLoop);
            else
                actionThread = new Thread(NormalFrequencyActionLoop);
            IsRunning = true;
            actionThread.IsBackground = true;
            actionThread.Start();
            return true;
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
            if (IsRunning)
            {
                actionThread.Abort();
                IsRunning = false;
                return true;
            }
            else
                return false;
        }

        private void SmartFrequencyActionLoop()
        {
            Stopwatch sw = new Stopwatch();
            while (IsRunning)
            {
                sw.Start();
                Action.Callback();
                OnDoAction?.Invoke();
                sw.Stop();
                int ms = (int)(Action.Frequency - sw.ElapsedMilliseconds);
                if (ms < 1)
                    ms = 1;
                sw.Reset();
                Thread.Sleep(ms);
            }
        }

        private void NormalFrequencyActionLoop()
        {
            while (IsRunning)
            {
                Action.Callback();
                OnDoAction?.Invoke();
                Thread.Sleep(Action.Frequency);
            }
        }
    }
}