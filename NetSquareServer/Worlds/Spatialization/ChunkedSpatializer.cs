using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquare.Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace NetSquare.Server.Worlds
{
    public class ChunkedSpatializer : Spatializer
    {
        public SpatialBounds Bounds { get; private set; }
        public float ChunkSize { get; private set; }
        private SpatialChunk[,] Chunks;
        private short Width;
        private short Height;
        private ConcurrentDictionary<uint, ChunkedClient> Clients;

        public ChunkedSpatializer(NetSquareWorld world, float spatializationFreq, float synchFreq, float chunkSize, float xStart, float yStart, float xEnd, float yEnd) : base(world, spatializationFreq, synchFreq)
        {
            Clients = new ConcurrentDictionary<uint, ChunkedClient>();
            ChunkSize = chunkSize;
            Bounds = new SpatialBounds(xStart, yStart, xEnd, yEnd);
            CreateChunks(xStart, yStart, xEnd, yEnd);
        }

        /// <summary>
        /// Add a client to this spatializer
        /// </summary>
        /// <param name="client">ID of the client to add</param>
        public override void AddClient(ConnectedClient client)
        {
            NetsquareTransformFrame transform = World.Clients[client.ID];
            var chunk = GetChunkForPosition(transform);
            if (chunk != null)
            {
                ChunkedClient chunkClient = new ChunkedClient(client.ID, chunk.x, chunk.y, transform);
                if (!Clients.ContainsKey(client.ID))
                    while (!Clients.TryAdd(client.ID, chunkClient))
                        continue;
                chunk.AddClient(chunkClient);
            }
        }

        /// <summary>
        /// Remove a client from the spatializer
        /// </summary>
        /// <param name="clientID">ID of the client to remove</param>
        public override void RemoveClient(uint clientID)
        {
            ChunkedClient c;
            while (!Clients.TryGetValue(clientID, out c))
            {
                if (!Clients.ContainsKey(clientID))
                    return;
                else
                    continue;
            }

            var chunk = GetChunkForPosition(World.Clients[clientID]);
            if (chunk != null)
                chunk.RemoveClient(clientID);

            ChunkedClient client;
            while (!Clients.TryRemove(clientID, out client))
            {
                if (!Clients.ContainsKey(clientID))
                    return;
                else
                    continue;
            }
        }

        /// <summary>
        /// get all visible clients for a given client, according to a maximum view distance
        /// </summary>
        /// <param name="clientID">ID of the client to get visibles</param>
        /// <param name="maxDistance">maximum view distance of  the client</param>
        /// <returns></returns>
        public override HashSet<uint> GetVisibleClients(uint clientID)
        {
            return Clients[clientID].VisibleIDs;
        }

        /// <summary>
        /// Refresh the spatialization of clients in this world
        /// Process visible clients
        /// </summary>
        protected override void SpatializationLoop()
        {
            if (Clients == null)
            {
                return;
            }
            // refresh chunked client chunks
            foreach (var client in Clients)
                RefreshClientChunk(client.Value);

            Thread.Sleep(1);

            // process visible clients
            foreach (var client in Clients)
                ProcessVisible(client.Value);
        }

        /// <summary>
        /// Synch clients transforms, pack them into messages and send them to clients
        /// </summary>
        protected override unsafe void SynchLoop()
        {
            if (Chunks == null)
            {
                return;
            }
            syncStopWatch.Restart();
            // iterate on each chunk
            int width = Chunks.GetLength(0);
            int height = Chunks.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var chunk = Chunks[x, y];
                    if (chunk != null && chunk.Clients.Count > 0)
                    {
                        // create new synch message
                        NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageID.SetSynchFramesPacked);
                        // iterate on each clients in the chunk
                        lock (chunk.Clients)
                        {
                            foreach (var client in chunk.Clients.Values)
                            {
                                // if client has transform frames to send
                                if (ClientsTransformFrames.ContainsKey(client.ClientID) && ClientsTransformFrames[client.ClientID].Count > 0)
                                {
                                    // lock client frames list so we can read it safely
                                    lock (ClientsTransformFrames)
                                    {
                                        NetSquareSynchFramesUtils.SerializePackedFrames(synchMessage, client.ClientID, ClientsTransformFrames[client.ClientID]);
                                        // clear frames
                                        ClientsTransformFrames[client.ClientID].Clear();
                                    }
                                }
                            }
                            // send message to clients
                            if (synchMessage.HasWriteData)
                                World.server.SendToClients(synchMessage, chunk.Clients.Keys);
                        }
                    }
                }
            }
            syncStopWatch.Stop();
            UpdateSynchFrequency((int)syncStopWatch.ElapsedMilliseconds);
        }

        private void RefreshClientChunk(ChunkedClient client)
        {
            NetsquareTransformFrame clientTransform = World.Clients[client.ClientID];
            // get new chunk position
            short chunkX = (short)Math.Round((clientTransform.x - Bounds.MinX) / ChunkSize, MidpointRounding.ToEven);
            short chunkY = (short)Math.Round((clientTransform.z - Bounds.MinY) / ChunkSize, MidpointRounding.ToEven);
            if (!HasChunk(chunkX, chunkY))
                return;

            // client in wrong chunk, remove from it and add to new
            if (chunkX != client.ChunkX || chunkY != client.ChunkY)
            {
                // remove from old chunk
                var oldChunk = GetChunk(client.ChunkX, client.ChunkY);
                oldChunk.RemoveClient(client.ClientID);

                // add to new chunk
                var newChunk = GetChunk(chunkX, chunkY);
                newChunk.AddClient(client);

                if (oldChunk.StaticEntities.Count > 0)
                    World.Fire_OnHideStaticEntities(client.ClientID, oldChunk.StaticEntities);
                client.SetChunk(chunkX, chunkY);
                if (newChunk.StaticEntities.Count > 0)
                    World.Fire_OnShowStaticEntities(client.ClientID, newChunk.StaticEntities);
            }
        }

        private void ProcessVisible(ChunkedClient client)
        {
            var chunk = GetChunk(client.ChunkX, client.ChunkY);

            // leaving clients
            NetworkMessage leavingMessage = new NetworkMessage(NetSquareMessageID.ClientsLeaveWorld);
            // pack message
            foreach (uint oldVisible in client.VisibleIDs)
                if (!chunk.Clients.ContainsKey(oldVisible)) // client just leave FOV
                    leavingMessage.Set(new UInt24(oldVisible));
            // send packed message to client
            if (leavingMessage.HasWriteData)
                World.server.SendToClient(leavingMessage, client.ClientID);

            // joining clients
            NetworkMessage JoiningPacked = new NetworkMessage(NetSquareMessageID.ClientsJoinWorld);
            List<NetworkMessage> JoiningClientMessages = new List<NetworkMessage>();

            // iterate on each clients in my spatializer
            lock (chunk.Clients)
            {
                foreach (uint clientID in chunk.Clients.Keys)
                    if (!client.VisibleIDs.Contains(clientID)) // new client in FOV
                    {
                        //create new join message
                        NetworkMessage joiningClientMessage = new NetworkMessage(0, clientID);
                        lock (World.Clients)
                        {
                            // set transform frame
                            World.Clients[clientID].Serialize(joiningClientMessage);
                        }
                        // send message to server event for being custom binded
                        World.server.Worlds.Fire_OnSendWorldClients(World.ID, clientID, joiningClientMessage);
                        // add message to list for packing
                        JoiningClientMessages.Add(joiningClientMessage);
                    }
                client.VisibleIDs = new HashSet<uint>(chunk.Clients.Keys);
            }

            // send packed message
            if (JoiningClientMessages.Count > 0)
            {
                JoiningPacked.Pack(JoiningClientMessages);
                World.server.SendToClient(JoiningPacked, client.ClientID);
            }

            lock (World.Clients)
            {
                // client has move since last spatialization
                if (!World.Clients[client.ClientID].Equals(client.LastPosition))
                {
                    client.LastPosition = World.Clients[client.ClientID];
                }
            }
        }

        private SpatialChunk GetChunkForPosition(NetsquareTransformFrame transform)
        {
            short chunkX, chunkY;
            GetChunkPosition(transform, out chunkX, out chunkY);
            return GetChunk(chunkX, chunkY);
        }

        private SpatialChunk GetChunk(short x, short y)
        {
            return Chunks[x, y];
        }

        private bool HasChunk(short chunkX, short chunkY)
        {
            return chunkX >= 0 && chunkX < Width && chunkY >= 0 && chunkY < Height;
        }

        private void GetChunkPosition(NetsquareTransformFrame transform, out short chunkX, out short chunkY)
        {
            chunkX = (short)Math.Round((transform.x - Bounds.MinX) / ChunkSize, MidpointRounding.ToEven);
            chunkY = (short)Math.Round((transform.z - Bounds.MinY) / ChunkSize, MidpointRounding.ToEven);
        }

        private void CreateChunks(float xStart, float yStart, float xEnd, float yEnd)
        {
            Width = (short)(((xEnd - xStart) / ChunkSize) + 1);
            Height = (short)(((yEnd - yStart) / ChunkSize) + 1);
            Chunks = new SpatialChunk[Width, Height];

            // create empty chunks
            for (short x = 0; x < Width; x++)
                for (short y = 0; y < Width; y++)
                {
                    SpatialChunk chunk = new SpatialChunk(x, y);
                    Chunks[x, y] = chunk;
                }

            // bind neighbour
            for (short x = 0; x < Width; x++)
                for (short y = 0; y < Width; y++)
                    bindNeighbour(x, y);

            void bindNeighbour(short _x, short _y)
            {
                for (int x = _x - 1; x <= _x + 1; x++)
                    for (int y = _y - 1; y <= _y + 1; y++)
                    {
                        if (x >= 0 && y >= 0 && HasChunk((short)x, (short)y))
                            Chunks[_x, _y].AddNeighbour(Chunks[x, y]);
                    }
            }
        }

        public override void ForEach(Action<uint, IEnumerable<uint>> callback)
        {
            foreach (var client in Clients)
            {
                lock (client.Value.VisibleIDs)
                    callback(client.Key, client.Value.VisibleIDs);
            }
        }

        public override void AddStaticEntity(short type, uint id, NetsquareTransformFrame transform)
        {
            short chunkX, chunkY;
            GetChunkPosition(transform, out chunkX, out chunkY);
            if (HasChunk(chunkX, chunkY))
            {
                GetChunk(chunkX, chunkY).StaticEntities.Add(new StaticEntity(type, id, transform));
                StaticEntitiesCount++;
            }
            else
                Writer.Write("Fail adding static entity. can't get chunk for pos " + transform.x + " " + transform.y, ConsoleColor.Red);
        }
    }
}