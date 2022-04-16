using NetSquare.Core;
using System;
using System.Collections.Generic;
using System.Threading;

namespace NetSquareServer.Lobbies
{
    public class Synchronizer
    {
        public Dictionary<ushort, Dictionary<uint, NetworkMessage>> Messages { get; private set; } // => Id Message, Id Client, Message
        public bool Synchronizing { get; private set; }
        public int Frequency { get; private set; }
        private NetSquare_Server server;

        public Synchronizer(NetSquare_Server _server)
        {
            server = _server;
            Synchronizing = false;
            Messages = new Dictionary<ushort, Dictionary<uint, NetworkMessage>>();
        }

        /// <summary>
        /// Start synchronizer
        /// </summary>
        /// <param name="frequency">frequency of the synchronization (Hz => times / s)</param>
        public void StartSynchronizing(int frequency)
        {
            if (frequency <= 0)
                frequency = 1;
            if (frequency > 30)
                frequency = 30;
            Frequency = (int)((1f / (float)frequency) * 1000f);
            Synchronizing = true;
            Thread syncThread = new Thread(SyncronizationLoop);
            syncThread.IsBackground = true;
            syncThread.Start();
        }

        /// <summary>
        /// Remove every received message for a given clientID (call it on client Disconnect)
        /// </summary>
        /// <param name="ClientID">ID of the disconnected client</param>
        public void RemoveMessagesFromClient(uint ClientID)
        {
            lock (Messages)
            {
                foreach (var pair in Messages)
                    if (pair.Value.ContainsKey(ClientID))
                        pair.Value.Remove(ClientID);
            }
        }

        /// <summary>
        /// Stop synchronization
        /// </summary>
        public void Stop()
        {
            Synchronizing = false;
            Messages = new Dictionary<ushort, Dictionary<uint, NetworkMessage>>();
        }

        /// <summary>
        /// Add a message (from a client) to the synchronization queue
        /// </summary>
        /// <param name="message">message to sync</param>
        public void AddMessage(NetworkMessage message)
        {
            lock (Messages)
            {
                // add Head list of not exists
                if (!Messages.ContainsKey(message.HeadID))
                    Messages.Add(message.HeadID, new Dictionary<uint, NetworkMessage>());

                // add client to head list if not exists
                if (!Messages[message.HeadID].ContainsKey(message.ClientID))
                    Messages[message.HeadID].Add(message.ClientID, message);
                else
                    // set message for this client if client already has one
                    Messages[message.HeadID][message.ClientID] = message;
            }
        }

        private void SyncronizationLoop()
        {
            while (Synchronizing)
            {
                foreach (NetSquareWorld lobby in server.Worlds.Worlds.Values)
                {
                    Dictionary<ushort, byte[]> packed = PackMessages(lobby);
                    foreach (byte[] message in packed.Values)
                        lobby.Broadcast(message);
                }
                Thread.Sleep(Frequency);
            }
        }

        private Dictionary<ushort, byte[]> PackMessages(NetSquareWorld lobby)
        {
            Dictionary<ushort, byte[]> messages = new Dictionary<ushort, byte[]>();
            lock (Messages)
            {
                foreach (var msgPair in Messages)
                {
                    NetworkMessage packed = new NetworkMessage(msgPair.Key);
                    packed.SetType(MessageType.SynchronizeMessageCurrentWorld);

                    // get lenght
                    int fullLenght = 12;
                    foreach (var msg in msgPair.Value)
                        fullLenght += msg.Value.Data.Length + 4; // lenght of the message  + clietnID size (uint => 4 bytes)

                    // pack data
                    byte[] msgData = new byte[fullLenght];
                    int index = 12;
                    foreach (var msg in msgPair.Value)
                    {
                        Buffer.BlockCopy(BitConverter.GetBytes(msg.Key), 0, msgData, index, 4);
                        index += 4;
                        Buffer.BlockCopy(msg.Value.Data, 0, msgData, index, msg.Value.Data.Length);
                        index += msg.Value.Data.Length;
                    }
                    packed.SetData(msgData, false);
                    // write header
                    Buffer.BlockCopy(packed.GetHead(0), 0, msgData, 0, 12);

                    // add packed message to packed groups
                    messages.Add(msgPair.Key, msgData);
                }
                Messages.Clear();
            }
            return messages;
        }
    }
}