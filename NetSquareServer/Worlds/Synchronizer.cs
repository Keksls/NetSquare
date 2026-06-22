using NetSquare.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the synchronizer component.
    /// </summary>
    public class Synchronizer
    {
        /// <summary>
        /// Gets or sets the synchronize using udp value.
        /// </summary>
        public bool SynchronizeUsingUDP { get; private set; }
        /// <summary>
        /// Gets or sets the messages value.
        /// </summary>
        public ConcurrentDictionary<ushort, SynchronizedMessage> Messages { get; private set; }
        /// <summary>
        /// Gets or sets the synchronizing value.
        /// </summary>
        public bool Synchronizing { get; private set; }
        /// <summary>
        /// Gets or sets the frequency value.
        /// </summary>
        public int Frequency { get; private set; }
        /// <summary>
        /// Gets or sets the world value.
        /// </summary>
        public NetSquareWorld World { get; private set; }
        /// <summary>
        /// Stores the server value.
        /// </summary>
        private NetSquareServer server;

        /// <summary>
        /// Initializes a new instance of the synchronizer class.
        /// </summary>
        public Synchronizer(NetSquareServer _server, NetSquareWorld world, bool synchronizeUsingUDP)
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
            SynchronizedMessage synchronizedMessage = Messages.GetOrAdd(message.HeadID, headID => new SynchronizedMessage(headID));
            synchronizedMessage.AddMessage(message);
        }

        Stopwatch syncWatch = new Stopwatch();
        /// <summary>
        /// Executes the syncronization loop operation.
        /// </summary>
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
                        Dictionary<uint, NetworkMessage> snapshot = message.GetSnapshot();
                        if (snapshot.Count == 0)
                            continue;
                        // get spatialized messages
                        List<NetworkMessage> packedMessages = new List<NetworkMessage>();
                        World.Spatializer.ForEach((clientID, visibleIDs) =>
                        {
                            NetworkMessage msg = message.GetSpatializedPackedMessage(visibleIDs, clientID, snapshot);
                            if (msg != null)
                                packedMessages.Add(msg);
                        });

                        if (SynchronizeUsingUDP)
                        {
                            foreach (var packed in packedMessages)
                                server.SafeGetClient(packed.ClientID)?.AddUnreliableMessage(packed);
                        }
                        else
                        {
                            foreach (var packed in packedMessages)
                                server.SafeGetClient(packed.ClientID)?.AddTCPMessage(packed);
                        }
                        message.RemoveSnapshot(snapshot);
                    }
                }
                else // don't use spatializer
                {
                    foreach (SynchronizedMessage message in Messages.Values)
                    {
                        Dictionary<uint, NetworkMessage> snapshot = message.GetSnapshot();
                        if (snapshot.Count == 0)
                            continue;

                        if (SynchronizeUsingUDP)
                        {
                            foreach (uint clientID in World.Clients.Keys)
                            {
                                NetworkMessage packed = message.GetPackedMessage(snapshot, clientID);
                                if (packed != null)
                                    server.SafeGetClient(clientID)?.AddUnreliableMessage(packed);
                            }
                        }
                        else
                        {
                            foreach (uint clientID in World.Clients.Keys)
                            {
                                NetworkMessage packed = message.GetPackedMessage(snapshot, clientID);
                                if (packed != null)
                                    server.SafeGetClient(clientID)?.AddTCPMessage(packed);
                            }
                        }
                        message.RemoveSnapshot(snapshot);
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
#endregion
