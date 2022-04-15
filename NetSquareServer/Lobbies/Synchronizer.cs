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

        public void Stop()
        {
            Synchronizing = false;
            Messages = new Dictionary<ushort, Dictionary<uint, NetworkMessage>>();
        }

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
                    Dictionary<ushort, NetworkMessage> packed = PackMessages(lobby);
                    foreach (NetworkMessage message in packed.Values)
                        lobby.Broadcast(message);
                }
                Thread.Sleep(Frequency);
            }
        }

        private Dictionary<ushort, NetworkMessage> PackMessages(NetSquareWorld lobby)
        {
            Dictionary<ushort, NetworkMessage> messages = new Dictionary<ushort, NetworkMessage>();
            lock (Messages)
            {
                foreach (var msgPair in Messages)
                {
                    NetworkMessage packed = new NetworkMessage(msgPair.Key);
                    packed.SetType(MessageType.SynchronizeMessageCurrentWorld);
                    // foreach messages by clients, grouped by HeadID
                    foreach (var msg in msgPair.Value)
                        packed.Set(packed.ConcatArrays(BitConverter.GetBytes(msg.Key), msg.Value.Data));
                    // add packed message to packed groups
                    messages.Add(msgPair.Key, packed);
                }
                Messages.Clear();
            }
            return messages;
        }
    }
}