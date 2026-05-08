using NetSquare.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the spatializer component.
    /// </summary>
    public abstract class Spatializer
    {
        /// <summary>
        /// Gets or sets the world value.
        /// </summary>
        public NetSquareWorld World { get; private set; }
        /// <summary>
        /// Gets or sets the static entities count value.
        /// </summary>
        public uint StaticEntitiesCount { get; internal set; }
        /// <summary>
        /// Gets or sets the clients transform frames value.
        /// </summary>
        public ConcurrentDictionary<uint, List<INetSquareSynchFrame>> ClientsTransformFrames { get; internal set; }
        /// <summary>
        /// Gets or sets the synch frequency value.
        /// </summary>
        public int SynchFrequency { get; private set; }
        /// <summary>
        /// Gets or sets the spatialization frequency value.
        /// </summary>
        public int SpatializationFrequency { get; private set; }
        /// <summary>
        /// Gets or sets the maximum stored synchronization frames per client.
        /// </summary>
        public int MaxStoredFramesPerClient { get; set; }
        /// <summary>
        /// Gets or sets the optional trace recorder used to capture synchronization frames.
        /// </summary>
        public NetSquareTraceRecorder TraceRecorder { get; set; }
        /// <summary>
        /// Stores the synch name value.
        /// </summary>
        private string synchName;
        /// <summary>
        /// Stores the spatialization name value.
        /// </summary>
        private string spatializationName;
        /// <summary>
        /// Stores the synch max frequency value.
        /// </summary>
        private int synchMaxFrequency = -1;
        /// <summary>
        /// Stores the synch min frequency value.
        /// </summary>
        private int synchMinFrequency = -1;
        /// <summary>
        /// Stores the synch minimum offset value.
        /// </summary>
        private int synchMinimumOffset = 50;
        /// <summary>
        /// Stores the last frame pending messages value.
        /// </summary>
        private int lastFramePendingMessages = 0;
        /// <summary>
        /// Stores the synch last durations value.
        /// </summary>
        private List<int> synchLastDurations;
        /// <summary>
        /// Stores the sync stop watch value.
        /// </summary>
        protected Stopwatch syncStopWatch;
        /// <summary>
        /// Stores the started value.
        /// </summary>
        private bool started;

        /// <summary>
        /// Instantiate a new spatializer
        /// </summary>
        /// <param name="world"> world to spatialize</param>
        public Spatializer(NetSquareWorld world, float spatializationFreq, float synchFreq)
        {
            World = world;
            ClientsTransformFrames = new ConcurrentDictionary<uint, List<INetSquareSynchFrame>>();
            synchName = "Spatializer_Sync_World_" + World.ID;
            spatializationName = "Spatializer_Spatialization_World_" + World.ID;
            SpatializationFrequency = NetSquareScheduler.GetMsFrequencyFromHz(spatializationFreq);
            SynchFrequency = NetSquareScheduler.GetMsFrequencyFromHz(synchFreq);
            MaxStoredFramesPerClient = 256;
            syncStopWatch = new Stopwatch();
        }

        /// <summary>
        /// Get a chunked spatializer
        /// </summary>
        /// <param name="world"> world to spatialize</param>
        /// <param name="chunkSize"> size of the chunks</param>
        /// <param name="xStart"> start x of the world</param>
        /// <param name="yStart"> start y of the world</param>
        /// <param name="xEnd"> end x of the world</param>
        /// <param name="yEnd"> end y of the world</param>
        /// <returns> a chunked spatializer</returns>
        public static ChunkedSpatializer GetChunkedSpatializer(NetSquareWorld world, float spatializationFreq, float synchFreq, float chunkSize, float xStart, float yStart, float xEnd, float yEnd, float chunkHysteresis = 0f)
        {
            return new ChunkedSpatializer(world, spatializationFreq, synchFreq, chunkSize, xStart, yStart, xEnd, yEnd, chunkHysteresis);
        }

        /// <summary>
        /// Get a simple spatializer
        /// </summary>
        /// <param name="world"> world to spatialize</param>
        /// <param name="maxViewDistance"> maximum view distance of the clients</param>
        /// <returns> a simple spatializer</returns>
        public static SimpleSpatializer GetSimpleSpatializer(NetSquareWorld world, float spatializationFreq, float synchFreq, float maxViewDistance, float visibilityHysteresis = 0f)
        {
            return new SimpleSpatializer(world, spatializationFreq, synchFreq, maxViewDistance, visibilityHysteresis);
        }

        #region Adaptive Synch Frequency
        /// <summary>
        /// Set the adaptive synch frequency
        /// </summary>
        /// <param name="min"> minimum frequency (-1 to disable)</param>
        /// <param name="max"> maximum frequency (-1 to disable)</param>
        /// <param name="maxKeepingLastFrequencies"> number of last frequencies to keep for the average</param>
        /// <param name="synchMinimumOffset"> minimum offset to change the frequency</param>
        public void SetAdaptiveSynchFrequency(int min, int max, int maxKeepingLastFrequencies, int synchMinimumOffset)
        {
            synchMaxFrequency = max;
            synchMinFrequency = min;
            synchLastDurations = new List<int>(maxKeepingLastFrequencies);
            this.synchMinimumOffset = synchMinimumOffset;

            // start server statistics if not already started
            if (!World.server.Statistics.Running && synchMinFrequency != -1 && synchMaxFrequency != -1)
            {
                World.server.Statistics.StartReceivingStatistics(World.server);
            }
        }

        /// <summary>
        /// Update the adaptive synch frequency
        /// </summary>
        protected void UpdateSynchFrequency(int lastSyncDurationMs)
        {
            if (synchMaxFrequency != -1 && synchMinFrequency != -1)
            {
                // add the last duration to the list
                if (synchLastDurations.Count == synchLastDurations.Capacity)
                {
                    synchLastDurations.RemoveAt(0);
                }
                synchLastDurations.Add(lastSyncDurationMs);

                // calculate the average of the last durations
                int average = 0;
                foreach (var duration in synchLastDurations)
                {
                    average += duration;
                }
                average /= synchLastDurations.Count;
                // clamp the new frequency
                if (average > synchMaxFrequency)
                {
                    average = synchMaxFrequency + synchMinimumOffset;
                }
                else if (average < synchMinFrequency)
                {
                    average = synchMinFrequency + synchMinimumOffset;
                }
                else
                {
                    average = average + synchMinimumOffset;
                }

                // check is server has too much pending messages to send, and low the frequency if it's the case
                if (World.server.Statistics.CurrentStatistics.NbMessagesToSend > 100000 && lastFramePendingMessages > 100000)
                {
                    average += World.server.Statistics.CurrentStatistics.NbMessagesToSend / 10; // give 10ms more for each 100 messages, the time to empty the queue
                }

                // check is server has too much pending messages to send, and low the frequency if it's the case
                if (World.server.Statistics.CurrentStatistics.NbMessagesToSend > 10000 && lastFramePendingMessages > 10000)
                {
                    average += World.server.Statistics.CurrentStatistics.NbMessagesToSend / 100; // give 10ms more for each 100 messages, the time to empty the queue
                }

                // check is server has too much pending messages to send, and low the frequency if it's the case
                if (World.server.Statistics.CurrentStatistics.NbMessagesToSend > 1000 && lastFramePendingMessages > 1000)
                {
                    average += World.server.Statistics.CurrentStatistics.NbMessagesToSend / 200; // give 10ms more for each 100 messages, the time to empty the queue
                }
                lastFramePendingMessages = World.server.Statistics.CurrentStatistics.NbMessagesToSend;

                // set the new frequency
                SynchFrequency = average;
                NetSquareScheduler.SetSchedulerFrequency(synchName, SynchFrequency);
            }
        }
        #endregion

        /// <summary>
        /// Add a client to this spatializer
        /// </summary>
        /// <param name="client">ID of the client to add</param>
        public abstract void AddClient(ConnectedClient client);

        /// <summary>
        /// Remove a client from the spatializer
        /// </summary>
        /// <param name="clientID">ID of the client to remove</param>
        public abstract void RemoveClient(uint clientID);

        /// <summary>
        /// Store a list of synch frames for a client
        /// </summary>
        /// <param name="clientID"> id of the client to store frames</param>
        /// <param name="synchFrames"> list of frames to store</param>
        public virtual void StoreSynchFrames(uint clientID, INetSquareSynchFrame[] synchFrames)
        {
            if (synchFrames == null || synchFrames.Length == 0)
                return;

            if (TraceRecorder != null)
                TraceRecorder.Record(clientID, synchFrames);

            List<INetSquareSynchFrame> frames = ClientsTransformFrames.GetOrAdd(clientID, _ => new List<INetSquareSynchFrame>());
            lock (frames)
            {
                frames.AddRange(synchFrames);
                TrimStoredFrames(frames);
            }

            // set client pos as last frame
            if (NetSquareSynchFramesUtils.TryGetMostRecentTransformFrame(synchFrames, out NetsquareTransformFrame mostRecentTransformFrame))
            {
                World.SetClientTransform(clientID, mostRecentTransformFrame);
            }
        }

        /// <summary>
        /// Store a synch frame for a client
        /// </summary>
        /// <param name="clientID"> id of the client to store frame</param>
        /// <param name="synchFrame"> frame to store</param>
        public virtual void StoreSynchFrame(uint clientID, INetSquareSynchFrame synchFrame)
        {
            if (synchFrame == null)
                return;

            if (TraceRecorder != null)
                TraceRecorder.Record(clientID, new INetSquareSynchFrame[] { synchFrame });

            List<INetSquareSynchFrame> frames = ClientsTransformFrames.GetOrAdd(clientID, _ => new List<INetSquareSynchFrame>());
            lock (frames)
            {
                frames.Add(synchFrame);
                TrimStoredFrames(frames);
            }

            // set client pos as last frame if it's a transform frame
            switch (synchFrame.SynchFrameType)
            {
                case 0:
                    World.SetClientTransform(clientID, (NetsquareTransformFrame)synchFrame);
                    break;
            }
        }

        /// <summary>
        /// Executes the drain stored frames operation.
        /// </summary>
        protected Dictionary<uint, List<INetSquareSynchFrame>> DrainStoredFrames()
        {
            Dictionary<uint, List<INetSquareSynchFrame>> snapshot = new Dictionary<uint, List<INetSquareSynchFrame>>();
            foreach (var pair in ClientsTransformFrames)
            {
                List<INetSquareSynchFrame> frames = pair.Value;
                lock (frames)
                {
                    if (frames.Count == 0)
                        continue;

                    snapshot[pair.Key] = new List<INetSquareSynchFrame>(frames);
                    frames.Clear();
                }
            }
            return snapshot;
        }

        /// <summary>
        /// Trims a stored frame list to the configured per-client cap.
        /// </summary>
        /// <param name="frames">Frame list to trim.</param>
        private void TrimStoredFrames(List<INetSquareSynchFrame> frames)
        {
            if (MaxStoredFramesPerClient <= 0 || frames.Count <= MaxStoredFramesPerClient)
                return;

            int removeCount = frames.Count - MaxStoredFramesPerClient;
            frames.RemoveRange(0, removeCount);
        }

        /// <summary>
        /// Executes the remove stored frames operation.
        /// </summary>
        protected void RemoveStoredFrames(uint clientID)
        {
            List<INetSquareSynchFrame> removed;
            ClientsTransformFrames.TryRemove(clientID, out removed);
        }

        /// <summary>
        /// get all visible clients for a given client, according to a maximum view distance
        /// </summary>
        /// <param name="clientID">ID of the client to get visibles</param>
        /// <param name="maxDistance">maximum view distance of  the client</param>
        /// <returns></returns>
        public abstract HashSet<uint> GetVisibleClients(uint clientID);

        /// <summary>
        /// Execute a callback for each client in the spatializer
        /// </summary>
        /// <param name="callback"></param>
        public abstract void ForEach(Action<uint, IEnumerable<uint>> callback);

        /// <summary>
        /// Creates a debug snapshot of this spatializer.
        /// </summary>
        /// <returns>Spatializer debug snapshot.</returns>
        public virtual NetSquareSpatializerSnapshot CreateSnapshot()
        {
            return CreateSnapshot(true);
        }

        /// <summary>
        /// Creates a debug snapshot of this spatializer.
        /// </summary>
        /// <param name="includeDetails">Whether to include per-client visibility details.</param>
        /// <returns>Spatializer debug snapshot.</returns>
        public virtual NetSquareSpatializerSnapshot CreateSnapshot(bool includeDetails)
        {
            NetSquareSpatializerSnapshot snapshot = new NetSquareSpatializerSnapshot
            {
                Type = GetType().Name,
                SynchFrequency = SynchFrequency,
                SpatializationFrequency = SpatializationFrequency,
                StaticEntitiesCount = StaticEntitiesCount,
                MaxStoredFramesPerClient = MaxStoredFramesPerClient
            };

            foreach (var pair in ClientsTransformFrames)
            {
                int pendingFrames;
                lock (pair.Value)
                    pendingFrames = pair.Value.Count;

                snapshot.PendingFramesByClientID[pair.Key] = pendingFrames;
                snapshot.PendingFrameCount += pendingFrames;
            }

            if (includeDetails)
            {
                ForEach(delegate (uint clientID, IEnumerable<uint> visibleClients)
                {
                    snapshot.VisibleClientsByClientID[clientID] = visibleClients != null ? new List<uint>(visibleClients) : new List<uint>();
                });
            }

            return snapshot;
        }

        /// <summary>
        /// Add a static entity to the spatializer
        /// </summary>
        /// <param name="type"> type of the entity</param>
        /// <param name="id"> id of the entity</param>
        /// <param name="transform"> transform of the entity</param>
        public abstract void AddStaticEntity(short type, uint id, NetsquareTransformFrame transform);

        /// <summary>
        /// Send to spatialized clients the frames of the other clients
        /// Typicaly for chuncked spatializer, we pack frames of clients in the same chunk and send it to the clients in the same chunk
        /// </summary>
        protected abstract unsafe void SynchLoop();

        /// <summary>
        /// synchronization loop will send frames to clients at a fixed frequency
        /// </summary>
        protected abstract void SpatializationLoop();

        /// <summary>
        /// Start synchronization loop, this will send frames to clients at a fixed frequency
        /// Start spatialization loop, this will handle clients spawn and unspawn at a fixed frequency
        /// </summary>
        public void Start()
        {
            if (started)
                return;

            started = true;
            // start synchronization loop
            NetSquareScheduler.AddAction(synchName, SynchFrequency, true, SynchLoop);
            NetSquareScheduler.StartAction(synchName);
            // start spatialization loop
            NetSquareScheduler.AddAction(spatializationName, SpatializationFrequency, true, SpatializationLoop);
            NetSquareScheduler.StartAction(spatializationName);
        }

        /// <summary>
        /// Stop synchronization loop
        /// Stop spatialization loop
        /// </summary>
        public void Stop()
        {
            if (!started)
                return;

            NetSquareScheduler.StopAction(synchName);
            NetSquareScheduler.StopAction(spatializationName);
            started = false;
        }
    }

    /// <summary>
    /// Defines the available spatializer type values.
    /// </summary>
    public enum SpatializerType
    {
        None = 0,
        SimpleSpatializer = 1,
        ChunkedSpatializer = 2
    }
}
