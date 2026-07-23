using NetSquare.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Periodically broadcasts the latest synchronized state of each client.
    /// </summary>
    public class Synchronizer
    {
        #region Fields
        private readonly object lifecycleLock = new object();
        private readonly NetSquareServer server;
        private CancellationTokenSource stopCancellation;
        private Thread synchronizationThread;
        private int synchronizing;
        #endregion

        #region Properties
        /// <summary>
        /// Gets whether unreliable UDP is used for synchronized state broadcasts.
        /// </summary>
        public bool SynchronizeUsingUDP { get; private set; }

        /// <summary>
        /// Gets synchronized state partitions by message head.
        /// </summary>
        public ConcurrentDictionary<ushort, SynchronizedMessage> Messages { get; private set; }

        /// <summary>
        /// Gets whether the synchronization worker is running.
        /// </summary>
        public bool Synchronizing { get { return Volatile.Read(ref synchronizing) != 0; } }

        /// <summary>
        /// Gets the synchronization interval in milliseconds.
        /// </summary>
        public int Frequency { get; private set; }

        /// <summary>
        /// Gets the synchronized world.
        /// </summary>
        public NetSquareWorld World { get; private set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a world state synchronizer.
        /// </summary>
        /// <param name="server">Owning server.</param>
        /// <param name="world">World whose state is synchronized.</param>
        /// <param name="synchronizeUsingUDP">Whether synchronization uses unreliable UDP.</param>
        public Synchronizer(NetSquareServer server, NetSquareWorld world, bool synchronizeUsingUDP)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            World = world;
            SynchronizeUsingUDP = synchronizeUsingUDP;
            this.server = server;
            Messages = new ConcurrentDictionary<ushort, SynchronizedMessage>();
        }
        #endregion

        #region Lifecycle
        /// <summary>
        /// Starts periodic synchronization at the requested frequency.
        /// </summary>
        /// <param name="frequency">Synchronization frequency in hertz.</param>
        public void StartSynchronizing(int frequency)
        {
            lock (lifecycleLock)
            {
                if (Synchronizing)
                    return;

                int clampedFrequency = Math.Max(1, Math.Min(60, frequency));
                Frequency = Math.Max(1, 1000 / clampedFrequency);
                stopCancellation = new CancellationTokenSource();
                CancellationToken workerToken = stopCancellation.Token;
                synchronizationThread = new Thread(() => SynchronizationLoop(workerToken));
                synchronizationThread.IsBackground = true;
                synchronizationThread.Name = "NetSquare world synchronization " + World.ID;
                Volatile.Write(ref synchronizing, 1);
                synchronizationThread.Start();
            }
        }

        /// <summary>
        /// Stops synchronization and waits for the active iteration to finish.
        /// </summary>
        public void Stop()
        {
            Thread threadToJoin;
            CancellationTokenSource cancellation;
            lock (lifecycleLock)
            {
                if (!Synchronizing && synchronizationThread == null)
                {
                    Messages = new ConcurrentDictionary<ushort, SynchronizedMessage>();
                    return;
                }

                Volatile.Write(ref synchronizing, 0);
                cancellation = stopCancellation;
                threadToJoin = synchronizationThread;
                cancellation?.Cancel();
            }

            if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
            {
                int configuredTimeout = NetSquareConfigurationManager
                    .Get<NetSquareConfiguration>().WorkerStopTimeoutMilliseconds;
                int timeout = configuredTimeout > 0 ? configuredTimeout : 5000;
                if (!threadToJoin.Join(timeout))
                {
                    throw new TimeoutException(
                        "The world synchronization worker did not stop within the configured timeout.");
                }
            }

            lock (lifecycleLock)
            {
                if (synchronizationThread == threadToJoin)
                {
                    synchronizationThread = null;
                    stopCancellation = null;
                }
                Messages = new ConcurrentDictionary<ushort, SynchronizedMessage>();
            }
        }
        #endregion

        #region Message state
        /// <summary>
        /// Removes every retained message for one disconnected client.
        /// </summary>
        /// <param name="clientID">Disconnected client identifier.</param>
        public void RemoveMessagesFromClient(uint clientID)
        {
            foreach (KeyValuePair<ushort, SynchronizedMessage> pair in Messages)
                pair.Value.RemoveMessagesFromClient(clientID);
        }

        /// <summary>
        /// Replaces the latest synchronized state for one client and message head.
        /// </summary>
        /// <param name="message">Latest client state.</param>
        public void AddMessage(NetworkMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            SynchronizedMessage synchronizedMessage = Messages.GetOrAdd(
                message.HeadID,
                headID => new SynchronizedMessage(headID));
            synchronizedMessage.AddMessage(message);
        }
        #endregion

        #region Synchronization loop
        /// <summary>
        /// Sends snapshots at a stable frequency until cancellation is requested.
        /// </summary>
        /// <param name="cancellationToken">Stop token.</param>
        private void SynchronizationLoop(CancellationToken cancellationToken)
        {
            Stopwatch syncWatch = new Stopwatch();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    syncWatch.Restart();
                    SynchronizeCurrentSnapshots();
                    syncWatch.Stop();

                    int delayMilliseconds = Frequency - (int)syncWatch.ElapsedMilliseconds;
                    if (delayMilliseconds < 1)
                        delayMilliseconds = 1;
                    if (cancellationToken.WaitHandle.WaitOne(delayMilliseconds))
                        return;
                }
            }
            finally
            {
                Volatile.Write(ref synchronizing, 0);
            }
        }

        /// <summary>
        /// Captures and broadcasts every pending synchronized message partition.
        /// </summary>
        private void SynchronizeCurrentSnapshots()
        {
            if (World.UseSpatializer)
            {
                SynchronizeSpatializedSnapshots();
                return;
            }

            foreach (SynchronizedMessage message in Messages.Values)
            {
                Dictionary<uint, NetworkMessage> snapshot = message.GetSnapshot();
                if (snapshot.Count == 0)
                    continue;

                foreach (uint clientID in World.Clients.Keys)
                {
                    NetworkMessage packed = message.GetPackedMessage(snapshot, clientID);
                    SendPackedMessage(packed, clientID);
                }
                message.RemoveSnapshot(snapshot);
            }
        }

        /// <summary>
        /// Builds and broadcasts one tailored synchronized payload per visible client set.
        /// </summary>
        private void SynchronizeSpatializedSnapshots()
        {
            foreach (SynchronizedMessage message in Messages.Values)
            {
                Dictionary<uint, NetworkMessage> snapshot = message.GetSnapshot();
                if (snapshot.Count == 0)
                    continue;

                World.Spatializer.ForEach((clientID, visibleIDs) =>
                {
                    NetworkMessage packed = message.GetSpatializedPackedMessage(
                        visibleIDs,
                        clientID,
                        snapshot);
                    SendPackedMessage(packed, clientID);
                });
                message.RemoveSnapshot(snapshot);
            }
        }

        /// <summary>
        /// Sends one packed synchronization payload over the configured transport.
        /// </summary>
        /// <param name="message">Targeted packed message, or null when no state is visible.</param>
        /// <param name="clientID">Authenticated destination client identifier.</param>
        private void SendPackedMessage(NetworkMessage message, uint clientID)
        {
            if (message == null)
                return;

            ConnectedClient client = server.SafeGetClient(clientID);
            if (SynchronizeUsingUDP)
                client?.AddUnreliableMessage(message);
            else
                client?.AddTCPMessage(message);
        }
        #endregion
    }
}
#endregion
