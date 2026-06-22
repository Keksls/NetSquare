using NetSquare.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the synchronized message component.
    /// </summary>
    public class SynchronizedMessage
    {
        /// <summary>
        /// Gets or sets the empty value.
        /// </summary>
        public bool Empty { get { return messagesList.Count == 0; } }
        /// <summary>
        /// Gets or sets the head id value.
        /// </summary>
        public ushort HeadID { get; set; }
        /// <summary>
        /// Gets or sets the messages list value.
        /// </summary>
        private ConcurrentDictionary<uint, NetworkMessage> messagesList = new ConcurrentDictionary<uint, NetworkMessage>(); // clientID => message

        /// <summary>
        /// Initializes a new instance of the synchronized message class.
        /// </summary>
        public SynchronizedMessage(ushort headID)
        {
            HeadID = headID;
        }

        /// <summary>
        /// Add a message to the synchronizer
        /// </summary>
        /// <param name="message">message to synchronize</param>
        public void AddMessage(NetworkMessage message)
        {
            messagesList[message.ClientID] = message;
        }

        /// <summary>
        /// Remove every received message for a given clientID (call it on client Disconnect)
        /// </summary>
        /// <param name="ClientID">ID of the disconnected client</param>
        public void RemoveMessagesFromClient(uint ClientID)
        {
            NetworkMessage removed;
            messagesList.TryRemove(ClientID, out removed);
        }

        /// <summary>
        /// Executes the get snapshot operation.
        /// </summary>
        public Dictionary<uint, NetworkMessage> GetSnapshot()
        {
            Dictionary<uint, NetworkMessage> snapshot = new Dictionary<uint, NetworkMessage>();
            foreach (var pair in messagesList)
                snapshot[pair.Key] = pair.Value;
            return snapshot;
        }

        /// <summary>
        /// Executes the remove snapshot operation.
        /// </summary>
        public void RemoveSnapshot(Dictionary<uint, NetworkMessage> snapshot)
        {
            foreach (uint clientID in snapshot.Keys)
            {
                NetworkMessage removed;
                messagesList.TryRemove(clientID, out removed);
            }
        }

        /// <summary>
        /// get fully packed message, not spatialized
        /// </summary>
        /// <returns>packed message</returns>
        public NetworkMessage GetPackedMessage()
        {
            Dictionary<uint, NetworkMessage> snapshot = GetSnapshot();
            NetworkMessage packed = GetPackedMessage(snapshot);
            RemoveSnapshot(snapshot);
            return packed;
        }

        /// <summary>
        /// Executes the get packed message operation.
        /// </summary>
        public NetworkMessage GetPackedMessage(Dictionary<uint, NetworkMessage> snapshot)
        {
            return GetPackedMessage(snapshot.Values);
        }

        /// <summary>
        /// Executes the get packed message operation while excluding one sender.
        /// </summary>
        public NetworkMessage GetPackedMessage(Dictionary<uint, NetworkMessage> snapshot, uint excludedClientID)
        {
            List<NetworkMessage> messages = new List<NetworkMessage>();
            foreach (var pair in snapshot)
                if (pair.Key != excludedClientID)
                    messages.Add(pair.Value);

            if (messages.Count == 0)
                return null;

            return GetPackedMessage(messages);
        }

        /// <summary>
        /// Executes the get packed message operation.
        /// </summary>
        private NetworkMessage GetPackedMessage(IEnumerable<NetworkMessage> messages)
        {
            NetworkMessage packed = new NetworkMessage(HeadID);
            packed.HeadID = HeadID;
            packed.Pack(messages, true);
            return packed;
        }

        /// <summary>
        /// get a spatialized packed message for a client
        /// </summary>
        /// <param name="client">client to get message</param>
        /// <returns>packed message</returns>
        public NetworkMessage GetSpatializedPackedMessage(IEnumerable<uint> visivleClientsID, uint clientID)
        {
            return GetSpatializedPackedMessage(visivleClientsID, clientID, GetSnapshot());
        }

        /// <summary>
        /// Executes the get spatialized packed message operation.
        /// </summary>
        public NetworkMessage GetSpatializedPackedMessage(IEnumerable<uint> visivleClientsID, uint clientID, Dictionary<uint, NetworkMessage> snapshot)
        {
            List<NetworkMessage> messages = new List<NetworkMessage>();
            foreach (uint visibleID in visivleClientsID)
            {
                if (visibleID == clientID)
                    continue;

                NetworkMessage message;
                if (snapshot.TryGetValue(visibleID, out message))
                    messages.Add(message);
            }
            if (messages.Count == 0)
                return null;

            NetworkMessage packed = new NetworkMessage(HeadID);
            packed.HeadID = HeadID;
            packed.ClientID = clientID;
            packed.Pack(messages, true);
            return packed;
        }

        /// <summary>
        /// Clear the synchronizer
        /// </summary>
        public void Clear()
        {
            messagesList.Clear();
        }
    }
}
#endregion
