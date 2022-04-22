using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetSquareServer.Worlds
{
    public class Synchronizer
    {
        public bool SynchronizeUsingUDP { get; private set; }
        public Dictionary<ushort, Dictionary<UInt24, NetworkMessage>> Messages { get; private set; } // => Id Message, Id Client, Message
        public bool Synchronizing { get; private set; }
        public int Frequency { get; private set; }
        private NetSquare_Server server;

        public Synchronizer(NetSquare_Server _server, bool synchronizeUsingUDP)
        {
            SynchronizeUsingUDP = synchronizeUsingUDP;
            server = _server;
            Synchronizing = false;
            Messages = new Dictionary<ushort, Dictionary<UInt24, NetworkMessage>>();
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
        public void RemoveMessagesFromClient(UInt24 ClientID)
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
            Messages = new Dictionary<ushort, Dictionary<UInt24, NetworkMessage>>();
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
                    Messages.Add(message.HeadID, new Dictionary<UInt24, NetworkMessage>());

                // add client to head list if not exists
                if (!Messages[message.HeadID].ContainsKey(message.ClientID))
                    Messages[message.HeadID].Add(message.ClientID, message);
                else
                    // set message for this client if client already has one
                    Messages[message.HeadID][message.ClientID] = message;
            }
        }

        Stopwatch syncWatch = new Stopwatch(); 
        private void SyncronizationLoop()
        {
            while (Synchronizing)
            {
                syncWatch.Reset();
                syncWatch.Start();
                foreach (NetSquareWorld world in server.Worlds.Worlds.Values)
                    if (world.UseSpatializer)
                        SynchronizeWorldSpatilized(world);
                    else
                        SynchronizeWorld(world);
                syncWatch.Stop();
                int freq = Frequency - (int)syncWatch.ElapsedMilliseconds;
                if (freq <= 0)
                    freq = 1;
                Thread.Sleep(freq);
            }
        }

        private void SynchronizeWorld(NetSquareWorld world)
        {
            Dictionary<ushort, byte[]> packed = PackMessages(world);
            foreach (var message in packed)
            {
                if (SynchronizeUsingUDP)
                    world.BroadcastUDP(message.Key, message.Value);
                else
                    world.Broadcast(message.Value);
            }
        }

        private void SynchronizeWorldSpatilized(NetSquareWorld world)
        {
            ConcurrentDictionary<ushort, ConcurrentDictionary<uint, byte[]>> packed = PackMessagesSpatialized(world);
            foreach (var message in packed)
            {
                foreach (var spatialized in message.Value)
                    if (spatialized.Value == null)
                        Utils.Writer.Write("null message");
                if (SynchronizeUsingUDP)
                    foreach (var spatialized in message.Value)
                        world.SendToClientUDP(spatialized.Key, message.Key, spatialized.Value);
                else
                    foreach (var spatialized in message.Value)
                        world.SendToClient(spatialized.Key, spatialized.Value);
            }
        }

        private Dictionary<ushort, byte[]> PackMessages(NetSquareWorld world)
        {
            Dictionary<ushort, byte[]> messages = new Dictionary<ushort, byte[]>();
            lock (Messages)
            {
                // add packed message to packed groups
                foreach (var msgPair in Messages)
                    messages.Add(msgPair.Key, PackMessage(world, msgPair.Key));
                Messages.Clear();
            }
            return messages;
        }

        private ConcurrentDictionary<ushort, ConcurrentDictionary<uint, byte[]>> PackMessagesSpatialized(NetSquareWorld world)
        {
            ConcurrentDictionary<ushort, ConcurrentDictionary<uint, byte[]>> messages = new ConcurrentDictionary<ushort, ConcurrentDictionary<uint, byte[]>>();
            lock (Messages)
            {
                // add packed message to packed groups
                foreach (var msgPair in Messages)
                {
                    ConcurrentDictionary<uint, byte[]> packeds = new ConcurrentDictionary<uint, byte[]>();
                    HashSet<uint> clients = new HashSet<uint>(world.Clients);
                    foreach(uint clientID in clients)
                    {
                        byte[] spatializedMessage = PackSpatializedMessage(world, msgPair.Key, clientID);
                        if (spatializedMessage != null)
                            while (!packeds.TryAdd(clientID, spatializedMessage))
                                continue;
                    }
                    while(!messages.TryAdd(msgPair.Key, packeds))
                        continue;
                }
                Messages.Clear();
            }
            return messages;
        }

        private byte[] PackMessage(NetSquareWorld lobby, ushort headID)
        {
            NetworkMessage packed = new NetworkMessage(headID);
            packed.SetType(MessageType.SynchronizeMessageCurrentWorld);
            // set type as default for setPosition message, because it will be handled by dispatcher for invoke event on client side
            if (packed.HeadID == (ushort)NetSquareMessageType.ClientSetPosition)
                packed.SetType(MessageType.Default);

            // get lenght
            int fullLenght = 10;
            foreach (var msg in Messages[headID])
                fullLenght += msg.Value.Data.Length - 7; // lenght of the message  + clietnID size (uint => 4 bytes) - lenght of message head

            // pack data
            byte[] msgData = new byte[fullLenght];
            int index = 10;
            foreach (var msg in Messages[headID])
            {
                msgData[index] = msg.Key.b0;
                msgData[index + 1] = msg.Key.b1;
                msgData[index + 2] = msg.Key.b2;
                index += 3;
                Buffer.BlockCopy(msg.Value.Data, 10, msgData, index, msg.Value.Data.Length - 10);
                index += msg.Value.Data.Length - 10;
            }
            packed.SetSerializedData(msgData);

            // add packed message to packed groups
            return packed.Data;
        }

        private byte[] PackSpatializedMessage(NetSquareWorld world, ushort headID, uint clientID)
        {
            NetworkMessage packed = new NetworkMessage(headID);
            packed.SetType(MessageType.SynchronizeMessageCurrentWorld);
            // set type as default for setPosition message, because it will be handled by dispatcher for invoke event on client side
            if (packed.HeadID == (ushort)NetSquareMessageType.ClientSetPosition)
                packed.SetType(MessageType.Default);

            HashSet<uint> VisibleClients = new HashSet<uint>(world.Spatializer.GetVisibleClients(clientID)); // sometimes when unity client set pos, this line fire exception out of range index

            // get lenght
            int fullLenght = 10;
            foreach (var msg in Messages[headID])
                if (VisibleClients.Contains(msg.Key.UInt32))
                    fullLenght += msg.Value.Data.Length - 7; // lenght of the message  + clientID size (uint => 4 bytes) - lenght of message head

            if (fullLenght <= 10)
                return null;

            // pack data
            byte[] msgData = new byte[fullLenght];
            int index = 10;
            foreach (var msg in Messages[headID])
            {
                if (VisibleClients.Contains(msg.Key.UInt32))
                {
                    msgData[index] = msg.Key.b0;
                    msgData[index + 1] = msg.Key.b1;
                    msgData[index + 2] = msg.Key.b2;
                    index += 3;
                    Buffer.BlockCopy(msg.Value.Data, 10, msgData, index, msg.Value.Data.Length - 10);
                    index += msg.Value.Data.Length - 10;
                }
            }
            packed.SetSerializedData(msgData);

            // add packed message to packed groups
            return packed.Data;
        }
    }
}