using NetSquare.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

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
        private static long nextSchedulerInstanceID;
        private readonly object lifecycleLock = new object();
        private readonly string synchName;
        /// <summary>
        /// Stores the spatialization name value.
        /// </summary>
        private readonly string spatializationName;
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
        /// Stores reusable drained-frame dictionaries for synchronization passes.
        /// </summary>
        private readonly Stack<Dictionary<uint, List<INetSquareSynchFrame>>> drainedFrameSnapshots =
            new Stack<Dictionary<uint, List<INetSquareSynchFrame>>>(1);
        /// <summary>
        /// Stores reusable per-client frame lists to reduce synchronization allocations.
        /// </summary>
        private readonly Stack<List<INetSquareSynchFrame>> drainedFrameLists =
            new Stack<List<INetSquareSynchFrame>>();
        /// <summary>
        /// Defines the maximum number of per-client frame lists retained by the local pool.
        /// </summary>
        private const int MaxPooledFrameLists = 4096;

        /// <summary>
        /// Instantiate a new spatializer
        /// </summary>
        /// <param name="world"> world to spatialize</param>
        public Spatializer(NetSquareWorld world, float spatializationFreq, float synchFreq)
        {
            World = world;
            ClientsTransformFrames = new ConcurrentDictionary<uint, List<INetSquareSynchFrame>>();
            long schedulerInstanceID = Interlocked.Increment(ref nextSchedulerInstanceID);
            synchName = "Spatializer_Sync_World_" + World.ID + "-" + schedulerInstanceID;
            spatializationName = "Spatializer_Spatialization_World_" + World.ID + "-" + schedulerInstanceID;
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
        /// <param name="chunkHysteresis">Extra distance retained around the current chunk.</param>
        /// <returns> a chunked spatializer</returns>
        public static ChunkedSpatializer GetChunkedSpatializer(
            NetSquareWorld world,
            float spatializationFreq,
            float synchFreq,
            float chunkSize,
            float xStart,
            float yStart,
            float xEnd,
            float yEnd,
            float chunkHysteresis = 0f)
        {
            // Preserve the established API while applying the safe default allocation ceiling.
            return GetChunkedSpatializer(
                world,
                spatializationFreq,
                synchFreq,
                chunkSize,
                xStart,
                yStart,
                xEnd,
                yEnd,
                chunkHysteresis,
                ChunkedSpatializer.DefaultMaximumChunkCount);
        }

        /// <summary>
        /// Gets a chunked spatializer with an explicit total allocation ceiling.
        /// </summary>
        /// <param name="world">World to spatialize.</param>
        /// <param name="spatializationFreq">Spatialization frequency.</param>
        /// <param name="synchFreq">Synchronization frequency.</param>
        /// <param name="chunkSize">Size of one chunk.</param>
        /// <param name="xStart">Minimum X coordinate.</param>
        /// <param name="yStart">Minimum Y coordinate.</param>
        /// <param name="xEnd">Maximum X coordinate.</param>
        /// <param name="yEnd">Maximum Y coordinate.</param>
        /// <param name="chunkHysteresis">Extra distance retained around the current chunk.</param>
        /// <param name="maximumChunkCount">Maximum total chunks allocated by the spatializer.</param>
        /// <returns>A bounded chunked spatializer.</returns>
        public static ChunkedSpatializer GetChunkedSpatializer(
            NetSquareWorld world,
            float spatializationFreq,
            float synchFreq,
            float chunkSize,
            float xStart,
            float yStart,
            float xEnd,
            float yEnd,
            float chunkHysteresis,
            int maximumChunkCount)
        {
            // Forward the explicit allocation ceiling to the concrete spatializer.
            return new ChunkedSpatializer(
                world,
                spatializationFreq,
                synchFreq,
                chunkSize,
                xStart,
                yStart,
                xEnd,
                yEnd,
                chunkHysteresis,
                maximumChunkCount);
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
            Dictionary<uint, List<INetSquareSynchFrame>> snapshot =
                drainedFrameSnapshots.Count > 0
                    ? drainedFrameSnapshots.Pop()
                    : new Dictionary<uint, List<INetSquareSynchFrame>>();

            foreach (KeyValuePair<uint, List<INetSquareSynchFrame>> pair in ClientsTransformFrames)
            {
                List<INetSquareSynchFrame> frames = pair.Value;
                lock (frames)
                {
                    if (frames.Count == 0)
                        continue;

                    List<INetSquareSynchFrame> drainedFrames =
                        drainedFrameLists.Count > 0
                            ? drainedFrameLists.Pop()
                            : new List<INetSquareSynchFrame>(frames.Count);
                    if (drainedFrames.Capacity < frames.Count)
                        drainedFrames.Capacity = frames.Count;
                    drainedFrames.AddRange(frames);
                    frames.Clear();
                    snapshot[pair.Key] = drainedFrames;
                }
            }
            return snapshot;
        }

        /// <summary>
        /// Returns drained synchronization containers to their bounded local pools.
        /// </summary>
        /// <param name="snapshot">Completed synchronization snapshot.</param>
        protected void ReturnDrainedFrames(Dictionary<uint, List<INetSquareSynchFrame>> snapshot)
        {
            if (snapshot == null)
                return;

            foreach (List<INetSquareSynchFrame> frames in snapshot.Values)
            {
                frames.Clear();
                if (drainedFrameLists.Count < MaxPooledFrameLists)
                    drainedFrameLists.Push(frames);
            }

            snapshot.Clear();
            if (drainedFrameSnapshots.Count == 0)
                drainedFrameSnapshots.Push(snapshot);
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
        /// Executes an internal callback with immutable visibility snapshots and no per-client copy.
        /// </summary>
        /// <param name="callback">Internal synchronization callback.</param>
        internal virtual void ForEachVisibleSnapshot(Action<uint, uint[]> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            // Custom spatializers keep working through the public API; built-ins override this allocation path.
            ForEach(delegate (uint clientID, IEnumerable<uint> visibleClients)
            {
                if (visibleClients == null)
                {
                    callback(clientID, Array.Empty<uint>());
                    return;
                }

                uint[] visibleArray = visibleClients as uint[];
                callback(
                    clientID,
                    visibleArray ?? new List<uint>(visibleClients).ToArray());
            });
        }

        /// <summary>
        /// Creates a debug snapshot of this spatializer.
        /// </summary>
        /// <returns>Spatializer debug snapshot.</returns>
        public virtual NetSquareSpatializerSnapshot CreateSnapshot()
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

            ForEachVisibleSnapshot(delegate (uint clientID, uint[] visibleClients)
            {
                snapshot.VisibleClientsByClientID[clientID] = visibleClients != null ? new List<uint>(visibleClients) : new List<uint>();
            });

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
            lock (lifecycleLock)
            {
                if (started)
                    return;

                if (!NetSquareScheduler.AddAction(synchName, SynchFrequency, true, SynchLoop))
                    throw new InvalidOperationException("The spatial synchronization action is already registered.");
                if (!NetSquareScheduler.AddAction(
                    spatializationName,
                    SpatializationFrequency,
                    true,
                    SpatializationLoop))
                {
                    NetSquareScheduler.RemoveAction(synchName);
                    throw new InvalidOperationException("The spatialization action is already registered.");
                }

                started = true;
                if (!NetSquareScheduler.StartAction(synchName) ||
                    !NetSquareScheduler.StartAction(spatializationName))
                {
                    started = false;
                    NetSquareScheduler.StopAction(synchName);
                    NetSquareScheduler.StopAction(spatializationName);
                    NetSquareScheduler.RemoveAction(synchName);
                    NetSquareScheduler.RemoveAction(spatializationName);
                    throw new InvalidOperationException("The spatializer actions could not be started.");
                }
            }
        }

        /// <summary>
        /// Stops both spatializer actions and removes them from the shared scheduler.
        /// </summary>
        public void Stop()
        {
            lock (lifecycleLock)
            {
                if (!started)
                {
                    NetSquareScheduler.RemoveAction(synchName);
                    NetSquareScheduler.RemoveAction(spatializationName);
                    return;
                }

                started = false;
                NetSquareScheduler.StopAction(synchName);
                NetSquareScheduler.StopAction(spatializationName);
                NetSquareScheduler.RemoveAction(synchName);
                NetSquareScheduler.RemoveAction(spatializationName);
            }
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
