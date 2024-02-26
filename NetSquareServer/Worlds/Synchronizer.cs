using NetSquare.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetSquareServer.Worlds
{
    public class Synchronizer
    {
        public bool SynchronizeUsingUDP { get; private set; }
        public ConcurrentDictionary<ushort, SynchronizedMessage> Messages { get; private set; }
        public bool Synchronizing { get; private set; }
        public int Frequency { get; private set; }
        public NetSquareWorld World { get; private set; }
        private NetSquare_Server server;

        public Synchronizer(NetSquare_Server _server, NetSquareWorld world, bool synchronizeUsingUDP)
        {
            World = world;
            SynchronizeUsingUDP = synchronizeUsingUDP;
            server = _server;
            Synchronizing = false;
            Messages = new ConcurrentDictionary<ushort, SynchronizedMessage>();
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
            foreach (var pair in Messages)
                pair.Value.RemoveMessagesFromClient(ClientID);
        }

        /// <summary>
        /// Stop synchronization
        /// </summary>
        public void Stop()
        {
            Synchronizing = false;
            Messages = new ConcurrentDictionary<ushort, SynchronizedMessage>();
        }

        /// <summary>
        /// Add a message (from a client) to the synchronization queue
        /// </summary>
        /// <param name="message">message to sync</param>
        public void AddMessage(NetworkMessage message)
        {
            // add Head list of not exists
            if (!Messages.ContainsKey(message.HeadID))
                while (!Messages.TryAdd(message.HeadID, new SynchronizedMessage(message.HeadID)))
                    continue;
            // add client to head list if not exists
            Messages[message.HeadID].AddMessage(message);
        }

        Stopwatch syncWatch = new Stopwatch();
        private void SyncronizationLoop()
        {
            while (Synchronizing)
            {
                syncWatch.Reset();
                syncWatch.Start();
                // we use spatializer, let's send spatialized packed messages
                if (World.UseSpatializer)
                {
                    foreach (SynchronizedMessage message in Messages.Values)
                    {
                        if (message.Empty)
                            continue;
                        // get spatialized messages
                        List<NetworkMessage> packedMessages = new List<NetworkMessage>();
                        World.Spatializer.ForEach((clientID, visibleIDs) =>
                        {
                            NetworkMessage msg = message.GetSpatializedPackedMessage(visibleIDs, clientID);
                            if (msg != null)
                                packedMessages.Add(msg);
                        });

                        if (SynchronizeUsingUDP)
                        {
                            foreach (var packed in packedMessages)
                                server.SafeGetClient(packed.ClientID)?.AddUDPMessage(packed);
                        }
                        else
                        {
                            foreach (var packed in packedMessages)
                                server.SafeGetClient(packed.ClientID)?.AddTCPMessage(packed);
                        }
                        message.Clear();
                    }
                }
                else // don't use spatializer
                {
                    foreach (SynchronizedMessage message in Messages.Values)
                    {
                        if (message.Empty)
                            continue;
                        lock (World.Clients)
                        {
                            if (SynchronizeUsingUDP)
                            {
                                NetworkMessage packed = message.GetPackedMessage();
                                foreach (uint clientID in World.Clients.Keys)
                                    server.SafeGetClient(clientID)?.AddUDPMessage(packed);
                            }
                            else
                            {
                                NetworkMessage packed = message.GetPackedMessage();
                                foreach (uint clientID in World.Clients.Keys)
                                    server.SafeGetClient(clientID)?.AddTCPMessage(packed);
                            }
                        }
                        message.Clear();
                    }
                }
                syncWatch.Stop();
                int freq = Frequency - (int)syncWatch.ElapsedMilliseconds;
                if (freq <= 0)
                    freq = 1;
                Thread.Sleep(freq);
            }
        }
    }
}