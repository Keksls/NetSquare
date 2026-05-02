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
        /// Gets or sets the bounds value.
        /// </summary>
        public SpatialBounds Bounds { get; private set; }
        /// <summary>
        /// Gets or sets the chunk size value.
        /// </summary>
        public float ChunkSize { get; private set; }
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
        /// Initializes a new instance of the chunked spatializer class.
        /// </summary>
        public ChunkedSpatializer(NetSquareWorld world, float spatializationFreq, float synchFreq, float chunkSize, float xStart, float yStart, float xEnd, float yEnd) : base(world, spatializationFreq, synchFreq)
        {
            if (chunkSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
            if (xEnd < xStart || yEnd < yStart)
                throw new ArgumentException("Chunked spatializer bounds are invalid.");

            Clients = new ConcurrentDictionary<uint, ChunkedClient>();
            ChunkSize = chunkSize;
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

            List<ChunkedClient> snapshot = GetClientsSnapshot();
            foreach (ChunkedClient client in snapshot)
                RefreshClientChunk(client);

            foreach (ChunkedClient client in snapshot)
                ProcessVisible(client);
        }

        /// <summary>
        /// Synch clients transforms, pack them into messages and send them to clients
        /// </summary>
        protected override unsafe void SynchLoop()
        {
            if (Chunks == null)
                return;

            Dictionary<uint, List<INetSquareSynchFrame>> frameSnapshot = DrainStoredFrames();
            if (frameSnapshot.Count == 0)
                return;

            syncStopWatch.Restart();
            int width = Chunks.GetLength(0);
            int height = Chunks.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    SpatialChunk chunk = Chunks[x, y];
                    if (chunk == null || chunk.Clients.Count == 0)
                        continue;

                    List<uint> targetIDs = new List<uint>(chunk.Clients.Keys);
                    if (targetIDs.Count == 0)
                        continue;

                    NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageID.SetSynchFramesPacked);
                    foreach (uint clientID in targetIDs)
                    {
                        List<INetSquareSynchFrame> frames;
                        if (frameSnapshot.TryGetValue(clientID, out frames) && frames.Count > 0)
                            NetSquareSynchFramesUtils.SerializePackedFrames(synchMessage, clientID, frames);
                    }

                    if (synchMessage.HasWriteData)
                        World.server.SendToClients(synchMessage, targetIDs);
                }
            }
            syncStopWatch.Stop();
            UpdateSynchFrequency((int)syncStopWatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Executes the get clients snapshot operation.
        /// </summary>
        private List<ChunkedClient> GetClientsSnapshot()
        {
            List<ChunkedClient> snapshot = new List<ChunkedClient>();
            foreach (var pair in Clients)
                snapshot.Add(pair.Value);
            return snapshot;
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

                leaving = new HashSet<uint>(client.VisibleIDs);
                client.VisibleIDs.Clear();
            }

            NetworkMessage leavingMessage = new NetworkMessage(NetSquareMessageID.ClientsLeaveWorld);
            foreach (uint oldVisible in leaving)
                leavingMessage.Set(new UInt24(oldVisible));

            if (leavingMessage.HasWriteData)
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

            HashSet<uint> currentVisible = new HashSet<uint>(chunk.Clients.Keys);
            HashSet<uint> oldVisible;
            lock (client.SyncRoot)
                oldVisible = new HashSet<uint>(client.VisibleIDs);

            // leaving clients
            NetworkMessage leavingMessage = new NetworkMessage(NetSquareMessageID.ClientsLeaveWorld);
            foreach (uint oldVisibleClient in oldVisible)
                if (!currentVisible.Contains(oldVisibleClient))
                    leavingMessage.Set(new UInt24(oldVisibleClient));

            if (leavingMessage.HasWriteData)
                World.server.SendToClient(leavingMessage, client.ClientID);

            // joining clients
            NetworkMessage joiningPacked = new NetworkMessage(NetSquareMessageID.ClientsJoinWorld);
            List<NetworkMessage> joiningClientMessages = new List<NetworkMessage>();
            foreach (uint clientID in currentVisible)
            {
                if (oldVisible.Contains(clientID))
                    continue;

                NetsquareTransformFrame transform;
                if (!World.Clients.TryGetValue(clientID, out transform))
                    continue;

                NetworkMessage joiningClientMessage = new NetworkMessage(0, clientID);
                transform.Serialize(joiningClientMessage);
                World.server.Worlds.Fire_OnSendWorldClients(World.ID, clientID, joiningClientMessage);
                joiningClientMessages.Add(joiningClientMessage);
            }

            if (joiningClientMessages.Count > 0)
            {
                joiningPacked.Pack(joiningClientMessages);
                World.server.SendToClient(joiningPacked, client.ClientID);
            }

            lock (client.SyncRoot)
                client.VisibleIDs = currentVisible;

            NetsquareTransformFrame clientTransform;
            if (World.Clients.TryGetValue(client.ClientID, out clientTransform) && !clientTransform.Equals(client.LastPosition))
                client.LastPosition = clientTransform;
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
        /// Executes the get chunk position operation.
        /// </summary>
        private void GetChunkPosition(NetsquareTransformFrame transform, out short chunkX, out short chunkY)
        {
            TryGetChunkPosition(transform, out chunkX, out chunkY);
        }

        /// <summary>
        /// Executes the create chunks operation.
        /// </summary>
        private void CreateChunks(float xStart, float yStart, float xEnd, float yEnd)
        {
            int width = (int)Math.Floor((xEnd - xStart) / ChunkSize) + 1;
            int height = (int)Math.Floor((yEnd - yStart) / ChunkSize) + 1;
            if (width <= 0 || height <= 0 || width > short.MaxValue || height > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(ChunkSize), "Chunk grid dimensions are invalid.");

            Width = (short)width;
            Height = (short)height;
            Chunks = new SpatialChunk[Width, Height];

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
            foreach (var client in Clients)
            {
                HashSet<uint> visible;
                lock (client.Value.SyncRoot)
                    visible = new HashSet<uint>(client.Value.VisibleIDs);
                callback(client.Key, visible);
            }
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
