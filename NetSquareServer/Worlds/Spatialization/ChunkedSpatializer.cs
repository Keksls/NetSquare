using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquare.Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the chunked spatializer component.
    /// </summary>
    public class ChunkedSpatializer : Spatializer
    {
        /// <summary>
        /// Stores the default maximum number of chunks allocated by one spatializer.
        /// </summary>
        public const int DefaultMaximumChunkCount = 65536;

        /// <summary>
        /// Gets or sets the bounds value.
        /// </summary>
        public SpatialBounds Bounds { get; private set; }
        /// <summary>
        /// Gets or sets the chunk size value.
        /// </summary>
        public float ChunkSize { get; private set; }
        /// <summary>
        /// Gets the maximum number of chunks this spatializer may allocate.
        /// </summary>
        public int MaximumChunkCount { get; private set; }
        /// <summary>
        /// Gets or sets the extra distance kept around the current chunk before moving to another chunk.
        /// </summary>
        public float ChunkHysteresis { get; set; }
        /// <summary>
        /// Stores the chunks value.
        /// </summary>
        private SpatialChunk[,] Chunks;
        /// <summary>
        /// Stores the width value.
        /// </summary>
        private short Width;
        /// <summary>
        /// Stores the height value.
        /// </summary>
        private short Height;
        /// <summary>
        /// Stores the clients value.
        /// </summary>
        private ConcurrentDictionary<uint, ChunkedClient> Clients;
        /// <summary>
        /// Stores the reusable clients snapshot for spatialization ticks.
        /// </summary>
        private readonly List<ChunkedClient> spatializationClients = new List<ChunkedClient>();
        /// <summary>
        /// Caches one serialized synchronization payload per source chunk for the current tick.
        /// </summary>
        private readonly Dictionary<int, byte[]> synchronizationPayloadsByChunk = new Dictionary<int, byte[]>();

        /// <summary>
        /// Initializes a new instance of the chunked spatializer class.
        /// </summary>
        public ChunkedSpatializer(
            NetSquareWorld world,
            float spatializationFreq,
            float synchFreq,
            float chunkSize,
            float xStart,
            float yStart,
            float xEnd,
            float yEnd,
            float chunkHysteresis = 0f)
            : this(
                world,
                spatializationFreq,
                synchFreq,
                chunkSize,
                xStart,
                yStart,
                xEnd,
                yEnd,
                chunkHysteresis,
                DefaultMaximumChunkCount)
        {
            // Preserve the established API while applying the safe default allocation ceiling.
        }

        /// <summary>
        /// Initializes a chunked spatializer with an explicit total allocation ceiling.
        /// </summary>
        public ChunkedSpatializer(
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
            : base(world, spatializationFreq, synchFreq)
        {
            // Validate every allocation input before constructing any spatialization state.
            if (float.IsNaN(chunkSize) || float.IsInfinity(chunkSize) || chunkSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be finite and greater than zero.");
            if (!AreFiniteBounds(xStart, yStart, xEnd, yEnd) || xEnd < xStart || yEnd < yStart)
                throw new ArgumentException("Chunked spatializer bounds must be finite and ordered.");
            if (maximumChunkCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumChunkCount), "Maximum chunk count must be greater than zero.");
            if (float.IsNaN(chunkHysteresis) || float.IsInfinity(chunkHysteresis))
                throw new ArgumentOutOfRangeException(nameof(chunkHysteresis), "Chunk hysteresis must be finite.");

            Clients = new ConcurrentDictionary<uint, ChunkedClient>();
            ChunkSize = chunkSize;
            MaximumChunkCount = maximumChunkCount;
            ChunkHysteresis = chunkHysteresis < 0f ? 0f : chunkHysteresis;
            Bounds = new SpatialBounds(xStart, yStart, xEnd, yEnd);
            CreateChunks(xStart, yStart, xEnd, yEnd);
            Start();
        }

        /// <summary>
        /// Add a client to this spatializer
        /// </summary>
        /// <param name="client">ID of the client to add</param>
        public override void AddClient(ConnectedClient client)
        {
            if (client == null)
                return;

            NetsquareTransformFrame transform;
            if (!World.Clients.TryGetValue(client.ID, out transform))
                return;

            SpatialChunk chunk = GetChunkForPosition(transform);
            short chunkX = chunk != null ? chunk.x : (short)-1;
            short chunkY = chunk != null ? chunk.y : (short)-1;
            ChunkedClient chunkClient = new ChunkedClient(client.ID, chunkX, chunkY, transform);
            if (Clients.TryAdd(client.ID, chunkClient))
            {
                if (chunk != null)
                    chunk.AddClient(chunkClient);
            }
        }

        /// <summary>
        /// Remove a client from the spatializer
        /// </summary>
        /// <param name="clientID">ID of the client to remove</param>
        public override void RemoveClient(uint clientID)
        {
            ChunkedClient client;
            if (Clients.TryRemove(clientID, out client))
            {
                SpatialChunk chunk = GetChunk(client.ChunkX, client.ChunkY);
                if (chunk != null)
                    chunk.RemoveClient(clientID);
            }

            RemoveStoredFrames(clientID);
        }

        /// <summary>
        /// get all visible clients for a given client, according to a maximum view distance
        /// </summary>
        /// <param name="clientID">ID of the client to get visibles</param>
        /// <param name="maxDistance">maximum view distance of  the client</param>
        /// <returns></returns>
        public override HashSet<uint> GetVisibleClients(uint clientID)
        {
            ChunkedClient client;
            if (!Clients.TryGetValue(clientID, out client))
                return new HashSet<uint>();

            lock (client.SyncRoot)
                return new HashSet<uint>(client.VisibleIDs);
        }

        /// <summary>
        /// Refresh the spatialization of clients in this world
        /// Process visible clients
        /// </summary>
        protected override void SpatializationLoop()
        {
            if (Clients == null)
                return;

            GetClientsSnapshot(spatializationClients);
            foreach (ChunkedClient client in spatializationClients)
                RefreshClientChunk(client);

            foreach (ChunkedClient client in spatializationClients)
                ProcessVisible(client);
        }

        /// <summary>
        /// Synch clients transforms, pack them into messages and send them to clients
        /// </summary>
        protected override unsafe void SynchLoop()
        {
            if (Clients == null)
                return;

            Dictionary<uint, List<INetSquareSynchFrame>> frameSnapshot = DrainStoredFrames();
            try
            {
                if (frameSnapshot.Count == 0)
                    return;

                syncStopWatch.Restart();
                // Every client in one source chunk sees the same neighbourhood, so serialize that payload once.
                synchronizationPayloadsByChunk.Clear();
                foreach (KeyValuePair<uint, ChunkedClient> clientPair in Clients)
                {
                    ChunkedClient client = clientPair.Value;
                    int chunkKey = GetChunkKey(client.ChunkX, client.ChunkY);
                    byte[] serializedPayload;
                    if (!synchronizationPayloadsByChunk.TryGetValue(chunkKey, out serializedPayload))
                    {
                        SpatialChunk chunk = GetChunk(client.ChunkX, client.ChunkY);
                        NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageID.SetSynchFramesPacked);
                        if (chunk != null)
                        {
                            foreach (uint visibleClientID in chunk.Clients.Keys)
                            {
                                List<INetSquareSynchFrame> frames;
                                if (frameSnapshot.TryGetValue(visibleClientID, out frames) && frames.Count > 0)
                                    NetSquareSynchFramesUtils.SerializePackedFrames(synchMessage, visibleClientID, frames);
                            }
                        }

                        serializedPayload = synchMessage.HasWriteData ? synchMessage.Serialize() : null;
                        synchronizationPayloadsByChunk[chunkKey] = serializedPayload;
                    }

                    if (serializedPayload != null)
                        World.server.SendToClient(serializedPayload, client.ClientID);
                }
                syncStopWatch.Stop();
                UpdateSynchFrequency((int)syncStopWatch.ElapsedMilliseconds);
            }
            finally
            {
                if (syncStopWatch.IsRunning)
                    syncStopWatch.Stop();
                synchronizationPayloadsByChunk.Clear();
                ReturnDrainedFrames(frameSnapshot);
            }
        }
        /// <summary>
        /// Executes the get clients snapshot operation.
        /// </summary>
        private void GetClientsSnapshot(List<ChunkedClient> snapshot)
        {
            snapshot.Clear();
            if (snapshot.Capacity < Clients.Count)
                snapshot.Capacity = Clients.Count;
            foreach (KeyValuePair<uint, ChunkedClient> pair in Clients)
                snapshot.Add(pair.Value);
        }
        /// <summary>
        /// Executes the refresh client chunk operation.
        /// </summary>
        private void RefreshClientChunk(ChunkedClient client)
        {
            NetsquareTransformFrame clientTransform;
            if (!World.Clients.TryGetValue(client.ClientID, out clientTransform))
            {
                RemoveClient(client.ClientID);
                return;
            }

            if (IsInsideCurrentChunkWithHysteresis(client, clientTransform))
                return;

            short chunkX;
            short chunkY;
            if (!TryGetChunkPosition(clientTransform, out chunkX, out chunkY))
            {
                MoveClientOutOfBounds(client);
                return;
            }

            if (chunkX == client.ChunkX && chunkY == client.ChunkY)
                return;

            SpatialChunk oldChunk = GetChunk(client.ChunkX, client.ChunkY);
            SpatialChunk newChunk = GetChunk(chunkX, chunkY);

            if (oldChunk != null)
            {
                oldChunk.RemoveClient(client.ClientID);
                List<StaticEntity> oldStaticEntities = oldChunk.GetStaticEntitiesSnapshot();
                if (oldStaticEntities.Count > 0)
                    World.Fire_OnHideStaticEntities(client.ClientID, oldStaticEntities);
            }

            if (newChunk != null)
                newChunk.AddClient(client);

            client.SetChunk(chunkX, chunkY);

            if (newChunk != null)
            {
                List<StaticEntity> newStaticEntities = newChunk.GetStaticEntitiesSnapshot();
                if (newStaticEntities.Count > 0)
                    World.Fire_OnShowStaticEntities(client.ClientID, newStaticEntities);
            }
        }

        /// <summary>
        /// Executes the move client out of bounds operation.
        /// </summary>
        private void MoveClientOutOfBounds(ChunkedClient client)
        {
            SpatialChunk oldChunk = GetChunk(client.ChunkX, client.ChunkY);
            if (oldChunk != null)
            {
                oldChunk.RemoveClient(client.ClientID);
                List<StaticEntity> oldStaticEntities = oldChunk.GetStaticEntitiesSnapshot();
                if (oldStaticEntities.Count > 0)
                    World.Fire_OnHideStaticEntities(client.ClientID, oldStaticEntities);
            }

            NotifyAllVisibleLeaving(client);
            client.SetChunk(-1, -1);
        }

        /// <summary>
        /// Executes the notify all visible leaving operation.
        /// </summary>
        private void NotifyAllVisibleLeaving(ChunkedClient client)
        {
            HashSet<uint> leaving;
            lock (client.SyncRoot)
            {
                if (client.VisibleIDs.Count == 0)
                    return;

                leaving = client.VisibleIDs;
                client.NextVisibleIDs.Clear();
                client.VisibleIDs = client.NextVisibleIDs;
                client.NextVisibleIDs = leaving;
                client.PublishVisibleIDs(client.VisibleIDs);
            }

            NetworkMessage leavingMessage = new NetworkMessage(NetSquareMessageID.ClientsLeaveWorld);
            foreach (uint oldVisible in leaving)
                leavingMessage.Set(oldVisible);

            World.server.SendToClient(leavingMessage, client.ClientID);
        }
        /// <summary>
        /// Executes the process visible operation.
        /// </summary>
        private void ProcessVisible(ChunkedClient client)
        {
            SpatialChunk chunk = GetChunk(client.ChunkX, client.ChunkY);
            if (chunk == null)
            {
                NotifyAllVisibleLeaving(client);
                return;
            }

            HashSet<uint> currentVisible = client.NextVisibleIDs;
            currentVisible.Clear();
            foreach (uint visibleClientID in chunk.Clients.Keys)
                currentVisible.Add(visibleClientID);

            HashSet<uint> previousVisible;
            lock (client.SyncRoot)
            {
                previousVisible = client.VisibleIDs;
                bool visibilityChanged = !previousVisible.SetEquals(currentVisible);
                client.VisibleIDs = currentVisible;
                client.NextVisibleIDs = previousVisible;
                if (visibilityChanged)
                    client.PublishVisibleIDs(currentVisible);
            }

            NetworkMessage leavingMessage = null;
            foreach (uint previousVisibleClientID in previousVisible)
            {
                if (currentVisible.Contains(previousVisibleClientID))
                    continue;

                if (leavingMessage == null)
                    leavingMessage = new NetworkMessage(NetSquareMessageID.ClientsLeaveWorld);
                leavingMessage.Set(previousVisibleClientID);
            }

            if (leavingMessage != null)
                World.server.SendToClient(leavingMessage, client.ClientID);

            List<NetworkMessage> joiningClientMessages = null;
            foreach (uint visibleClientID in currentVisible)
            {
                if (previousVisible.Contains(visibleClientID))
                    continue;

                NetsquareTransformFrame transform;
                if (!World.Clients.TryGetValue(visibleClientID, out transform))
                    continue;

                NetworkMessage joiningClientMessage = new NetworkMessage(0, visibleClientID);
                transform.Serialize(joiningClientMessage);
                World.server.Worlds.Fire_OnSendWorldClients(World.ID, visibleClientID, joiningClientMessage);
                if (joiningClientMessages == null)
                    joiningClientMessages = new List<NetworkMessage>();
                joiningClientMessages.Add(joiningClientMessage);
            }

            if (joiningClientMessages != null)
            {
                NetworkMessage joiningPacked = new NetworkMessage(NetSquareMessageID.ClientsJoinWorld);
                joiningPacked.Pack(joiningClientMessages);
                World.server.SendToClient(joiningPacked, client.ClientID);
            }

            NetsquareTransformFrame clientTransform;
            if (World.Clients.TryGetValue(client.ClientID, out clientTransform) && !clientTransform.Equals(client.LastPosition))
                client.LastPosition.Set(clientTransform);
        }
        /// <summary>
        /// Executes the get chunk for position operation.
        /// </summary>
        private SpatialChunk GetChunkForPosition(NetsquareTransformFrame transform)
        {
            short chunkX;
            short chunkY;
            if (!TryGetChunkPosition(transform, out chunkX, out chunkY))
                return null;

            return GetChunk(chunkX, chunkY);
        }

        /// <summary>
        /// Packs signed chunk coordinates into one allocation-free dictionary key.
        /// </summary>
        /// <param name="chunkX">Chunk X coordinate.</param>
        /// <param name="chunkY">Chunk Y coordinate.</param>
        /// <returns>Unique key for the coordinate pair.</returns>
        private static int GetChunkKey(short chunkX, short chunkY)
        {
            return (chunkX << 16) | (ushort)chunkY;
        }
        /// <summary>
        /// Executes the get chunk operation.
        /// </summary>
        private SpatialChunk GetChunk(short x, short y)
        {
            if (!HasChunk(x, y))
                return null;

            return Chunks[x, y];
        }

        /// <summary>
        /// Executes the has chunk operation.
        /// </summary>
        private bool HasChunk(short chunkX, short chunkY)
        {
            return chunkX >= 0 && chunkX < Width && chunkY >= 0 && chunkY < Height;
        }

        /// <summary>
        /// Executes the try get chunk position operation.
        /// </summary>
        private bool TryGetChunkPosition(NetsquareTransformFrame transform, out short chunkX, out short chunkY)
        {
            chunkX = -1;
            chunkY = -1;

            if (!Bounds.IsInBounds(transform))
                return false;

            int x = (int)Math.Floor((transform.x - Bounds.MinX) / ChunkSize);
            int y = (int)Math.Floor((transform.z - Bounds.MinY) / ChunkSize);
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return false;

            chunkX = (short)x;
            chunkY = (short)y;
            return true;
        }

        /// <summary>
        /// Checks whether a client is still inside its current chunk plus hysteresis.
        /// </summary>
        /// <param name="client">Client to check.</param>
        /// <param name="transform">Current transform.</param>
        /// <returns>True when the client can stay in the current chunk.</returns>
        private bool IsInsideCurrentChunkWithHysteresis(ChunkedClient client, NetsquareTransformFrame transform)
        {
            if (ChunkHysteresis <= 0f || !HasChunk(client.ChunkX, client.ChunkY))
                return false;

            float minX = Bounds.MinX + client.ChunkX * ChunkSize - ChunkHysteresis;
            float maxX = Bounds.MinX + (client.ChunkX + 1) * ChunkSize + ChunkHysteresis;
            float minZ = Bounds.MinY + client.ChunkY * ChunkSize - ChunkHysteresis;
            float maxZ = Bounds.MinY + (client.ChunkY + 1) * ChunkSize + ChunkHysteresis;
            return transform.x >= minX && transform.x <= maxX && transform.z >= minZ && transform.z <= maxZ;
        }

        /// <summary>
        /// Executes the get chunk position operation.
        /// </summary>
        private void GetChunkPosition(NetsquareTransformFrame transform, out short chunkX, out short chunkY)
        {
            TryGetChunkPosition(transform, out chunkX, out chunkY);
        }

        /// <summary>
        /// Returns whether all spatial bounds are finite numbers.
        /// </summary>
        /// <param name="xStart">Minimum X coordinate.</param>
        /// <param name="yStart">Minimum Y coordinate.</param>
        /// <param name="xEnd">Maximum X coordinate.</param>
        /// <param name="yEnd">Maximum Y coordinate.</param>
        /// <returns>True when every coordinate is finite.</returns>
        private static bool AreFiniteBounds(float xStart, float yStart, float xEnd, float yEnd)
        {
            // Reject NaN and infinity before dimension arithmetic or numeric casts.
            return !float.IsNaN(xStart) && !float.IsInfinity(xStart) &&
                !float.IsNaN(yStart) && !float.IsInfinity(yStart) &&
                !float.IsNaN(xEnd) && !float.IsInfinity(xEnd) &&
                !float.IsNaN(yEnd) && !float.IsInfinity(yEnd);
        }

        /// <summary>
        /// Creates the bounded chunk grid and its neighbourhood links.
        /// </summary>
        private void CreateChunks(float xStart, float yStart, float xEnd, float yEnd)
        {
            double widthValue = Math.Floor(((double)xEnd - xStart) / ChunkSize) + 1d;
            double heightValue = Math.Floor(((double)yEnd - yStart) / ChunkSize) + 1d;
            if (widthValue < 1d || heightValue < 1d ||
                widthValue > short.MaxValue || heightValue > short.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(ChunkSize), "Chunk grid dimensions are invalid.");
            }

            int width = (int)widthValue;
            int height = (int)heightValue;
            long totalChunkCount = (long)width * height;
            if (totalChunkCount > MaximumChunkCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumChunkCount),
                    "The chunk grid requires " + totalChunkCount +
                    " chunks, exceeding the configured maximum of " + MaximumChunkCount + ".");
            }

            Width = (short)width;
            Height = (short)height;
            Chunks = new SpatialChunk[width, height];

            // create empty chunks
            for (short x = 0; x < Width; x++)
                for (short y = 0; y < Height; y++)
                {
                    SpatialChunk chunk = new SpatialChunk(x, y);
                    Chunks[x, y] = chunk;
                }

            // bind neighbour
            for (short x = 0; x < Width; x++)
                for (short y = 0; y < Height; y++)
                    BindNeighbour(x, y);

            void BindNeighbour(short _x, short _y)
            {
                for (int x = _x - 1; x <= _x + 1; x++)
                    for (int y = _y - 1; y <= _y + 1; y++)
                    {
                        if (x >= 0 && y >= 0 && HasChunk((short)x, (short)y))
                            Chunks[_x, _y].AddNeighbour(Chunks[x, y]);
                    }
            }
        }

        /// <summary>
        /// Executes the for each operation.
        /// </summary>
        public override void ForEach(Action<uint, IEnumerable<uint>> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            foreach (KeyValuePair<uint, ChunkedClient> client in Clients)
                callback(client.Key, new HashSet<uint>(client.Value.GetVisibleIDsSnapshot()));
        }

        /// <summary>
        /// Executes an internal callback with immutable visibility snapshots and no per-client copy.
        /// </summary>
        /// <param name="callback">Internal synchronization callback.</param>
        internal override void ForEachVisibleSnapshot(Action<uint, uint[]> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            foreach (KeyValuePair<uint, ChunkedClient> client in Clients)
                callback(client.Key, client.Value.GetVisibleIDsSnapshot());
        }

        /// <summary>
        /// Creates a debug snapshot of this chunked spatializer.
        /// </summary>
        /// <returns>Spatializer debug snapshot.</returns>
        public override NetSquareSpatializerSnapshot CreateSnapshot()
        {
            NetSquareSpatializerSnapshot snapshot = base.CreateSnapshot();
            snapshot.ChunkSize = ChunkSize;
            snapshot.ChunkHysteresis = ChunkHysteresis;
            snapshot.MinX = Bounds.MinX;
            snapshot.MinY = Bounds.MinY;
            snapshot.MaxX = Bounds.MaxX;
            snapshot.MaxY = Bounds.MaxY;
            snapshot.ChunkWidth = Width;
            snapshot.ChunkHeight = Height;

            for (short x = 0; x < Width; x++)
                for (short y = 0; y < Height; y++)
                {
                    SpatialChunk chunk = Chunks[x, y];
                    snapshot.Chunks.Add(new NetSquareSpatialChunkSnapshot
                    {
                        X = x,
                        Y = y,
                        ClientCount = chunk.Clients.Count,
                        StaticEntityCount = chunk.StaticEntityCount
                    });
                }

            return snapshot;
        }

        /// <summary>
        /// Executes the add static entity operation.
        /// </summary>
        public override void AddStaticEntity(short type, uint id, NetsquareTransformFrame transform)
        {
            short chunkX;
            short chunkY;
            if (TryGetChunkPosition(transform, out chunkX, out chunkY))
            {
                SpatialChunk chunk = GetChunk(chunkX, chunkY);
                if (chunk != null)
                {
                    chunk.AddStaticEntity(new StaticEntity(type, id, transform));
                    StaticEntitiesCount++;
                    return;
                }
            }

            Writer.Write("Fail adding static entity. can't get chunk for pos " + transform.x + " " + transform.y, ConsoleColor.Red);
        }
    }
}
#endregion
