using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NetSquare.Core
{
    public class NetSquareDispatcher
    {
        private Dictionary<ushort, NetSquareHeadAction> HeadActions;
        public int Count { get { return HeadActions.Count; } }
        private Action<NetSquareAction, NetworkMessage> executeInMainThreadCallback;

        public NetSquareDispatcher()
        {
            HeadActions = new Dictionary<ushort, NetSquareHeadAction>();
        }

        /// <summary>
        /// Get a list of all registered head actions
        /// </summary>
        /// <returns></returns>
        public List<NetSquareHeadAction> GetRegisteredActionsList()
        {
            return HeadActions.Values.ToList();
        }

        /// <summary>
        /// Get a nice string list of all registered head actions
        /// </summary>
        /// <returns></returns>
        public string GetRegisteredActionsString()
        {
            StringBuilder sb = new StringBuilder();
            foreach( var pair in HeadActions.OrderBy(a => a.Value.HeadID))
                sb.AppendLine(" - [" + pair.Key + "] : " + pair.Value.HeadName + " (" + pair.Value.HeadAction.Method.Name + ")");
            return sb.ToString();
        }

        /// <summary>
        /// Set a callback to invoke Actions on for non thread safe applications
        /// </summary>
        /// <param name="MainThreadCallback">Callback that invoke NetSquareAction into main thread</param>
        public void SetMainThreadCallback(Action<NetSquareAction, NetworkMessage> MainThreadCallback)
        {
            executeInMainThreadCallback = MainThreadCallback;
        }

        /// <summary>
        /// Manualy add a Head Action
        /// </summary>
        /// <param name="HeadID">ID of the NetworkMessage</param>
        /// <param name="HeadName">Name of the Network message (for debug only, you can let it null)</param>
        /// <param name="HeadAction">NetSquareAction to call on network message received with correspondig HeadID</param>
        /// <exception cref="Exception">Already Exist exception</exception>
        public void AddHeadAction(ushort HeadID, string HeadName, NetSquareAction HeadAction)
        {
            if (HeadActions.ContainsKey(HeadID))
                throw new Exception("Head " + HeadID + " Already exists in Dispatcher");
            HeadActions.Add(HeadID, new NetSquareHeadAction(HeadID, HeadName, HeadAction));
        }

        /// <summary>
        /// Manualy add a Head Action
        /// </summary>
        /// <param name="HeadID">ID of the NetworkMessage</param>
        /// <param name="HeadName">Name of the Network message (for debug only, you can let it null)</param>
        /// <param name="HeadType">NetSquareAction to call on network message received with correspondig HeadID</param>
        /// <exception cref="Exception">Already Exist exception</exception>
        public void AddHeadAction(Enum HeadType, string HeadName, NetSquareAction HeadAction)
        {
            ushort HeadID = Convert.ToUInt16(HeadType);
            if (HeadActions.ContainsKey(HeadID))
                throw new Exception("Head " + HeadID + " Already exists in Dispatcher");
            HeadActions.Add(HeadID, new NetSquareHeadAction(HeadID, HeadName, HeadAction));
        }

        /// <summary>
        /// Manualy remove an action
        /// </summary>
        /// <param name="HeadID">ID of the action to remove</param>
        /// <returns>true if success</returns>
        public bool RemoveHeadAction(ushort HeadID)
        {
            return HeadActions.Remove(HeadID);
        }

        /// <summary>
        /// Did the dispatcher has an action related to the given Head ID
        /// </summary>
        /// <param name="HeadID">Head ID</param>
        /// <returns>true if exists</returns>
        public bool HasHeadAction(ushort HeadID)
        {
            return HeadActions.ContainsKey(HeadID);
        }

        /// <summary>
        /// Bin every NetSquareActions methods on your project that has NetSquareAction Attribute
        /// </summary>
        public void AutoBindHeadActionsFromAttributes()
        {
            // Get all methods in the loaded assembly that have NetSquareAction Attribute
            IEnumerable<MethodInfo> methods = from assembly in AppDomain.CurrentDomain.GetAssemblies()
                                              from type in assembly.GetTypes()
                                              from method in type.GetMethods()
                                              where method.IsDefined(typeof(NetSquareActionAttribute), false)
                                              select method;
            foreach (MethodInfo method in methods)
            {
                AddHeadAction(method.GetCustomAttribute<NetSquareActionAttribute>().HeadID,
                    method.Name,
                    (NetSquareAction)method.CreateDelegate(typeof(NetSquareAction)));
            }
        }

        /// <summary>
        /// Invoke HeadAction of the given message according to it HeadID
        /// </summary>
        /// <param name="message">Network Message to raise</param>
        /// <returns>true if success</returns>
        public bool DispatchMessage(NetworkMessage message)
        {
            if (!HasHeadAction(message.HeadID))
                return false;
            ExecuteinMainThread(HeadActions[message.HeadID].HeadAction, message);
            return true;
        }

        /// <summary>
        /// Execute a NetSquareAction into main thread.
        /// This need to register a SetMainThreadCallback call once to work
        /// </summary>
        /// <param name="action">Callback action to invoke in main thread</param>
        /// <param name="message">Message to give to the callback action</param>
        public void ExecuteinMainThread(NetSquareAction action, NetworkMessage message)
        {
            if (executeInMainThreadCallback != null)
                executeInMainThreadCallback?.Invoke(action, message);
            else
                action?.Invoke(message);
        }

        /// <summary>
        /// Get the name of an action by HeadID
        /// </summary>
        /// <param name="ID">ID of the action (HeadID)</param>
        /// <returns>name of the action</returns>
        public string GetHeadName(ushort ID)
        {
            if (HasHeadAction(ID))
                return HeadActions[ID].HeadName;
            else
                return "No Action registered with ID '" + ID + "'";
        }

        /// <summary>
        /// Get NetSquareHeadAction by Head ID
        /// </summary>
        /// <param name="ID">ID of the action (HeadID)</param>
        /// <returns>null of don't exists</returns>
        public NetSquareHeadAction? GetHeadAction(ushort ID)
        {
            if (HasHeadAction(ID))
                return HeadActions[ID];
            else
                return null;
        }
    }
}