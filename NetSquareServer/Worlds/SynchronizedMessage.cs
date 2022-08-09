using NetSquare.Core;
using NetSquare.Core.Messages;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public class SynchronizedMessage
    {
        public ushort HeadID { get; set; }
        private ConcurrentDictionary<uint, NetworkMessage> messagesList = new ConcurrentDictionary<uint, NetworkMessage>(); // clientID => message

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
            if (!messagesList.ContainsKey(message.ClientID))
                while (!messagesList.TryAdd(message.ClientID, message))
                    continue;
            else
                messagesList[message.ClientID] = message;
        }

        /// <summary>
        /// Remove every received message for a given clientID (call it on client Disconnect)
        /// </summary>
        /// <param name="ClientID">ID of the disconnected client</param>
        public void RemoveMessagesFromClient(uint ClientID)
        {
            NetworkMessage removed;
            while (!messagesList.TryRemove(ClientID, out removed))
            {
                if (!messagesList.ContainsKey(ClientID))
                    return;
                else
                    continue;
            }
        }

        /// <summary>
        /// get fully packed message, not spatialized
        /// </summary>
        /// <returns>packed message</returns>
        public NetworkMessage GetPackedMessage()
        {
            NetworkMessage packed = new NetworkMessage(HeadID);
            if (HeadID == (ushort)NetSquareMessageType.ClientSetPosition)
                packed.SetType(MessageType.Default);
            else
                packed.SetType(MessageType.SynchronizeMessageCurrentWorld);
            packed.Pack(messagesList.Values, true);
            messagesList.Clear();
            return packed;
        }

        /// <summary>
        /// get a spatialized packed message for a client
        /// </summary>
        /// <param name="client">client to get message</param>
        /// <returns>packed message</returns>
        public NetworkMessage GetSpatializedPackedMessage(IEnumerable<uint> visivleClientsID, uint clientID)
        {
            List<NetworkMessage> messages = new List<NetworkMessage>();
            foreach (uint visibleID in visivleClientsID)
            {
                if (messagesList.ContainsKey(visibleID))
                    messages.Add(messagesList[visibleID]);
            }
            if (messages.Count == 0)
                return null;

            NetworkMessage packed = new NetworkMessage(HeadID);
            //packed.Client = client.Client;
            if (HeadID == (ushort)NetSquareMessageType.ClientSetPosition)
                packed.SetType(MessageType.Default);
            else
                packed.SetType(MessageType.SynchronizeMessageCurrentWorld);
            packed.ClientID = clientID;
            packed.Pack(messages, true);
            return packed;
        }
    }
}