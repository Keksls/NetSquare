using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using NetSquareServer.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace NetSquareServer.Worlds
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
            AddClient(client, NetsquareTransformFrame.zero);
        }

        /// <summary>
        /// add a client to this spatializer and set his position
        /// </summary>
        /// <param name="client">the client to add</param>
        /// <param name="transform">spawn position</param>
        public override void AddClient(ConnectedClient client, NetsquareTransformFrame transform)
        {
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

            var chunk = GetChunkForPosition(c.Position);
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
        /// set a client position
        /// </summary>
        /// <param name="clientID">id of the client that just moved</param>
        /// <param name="transform">position</param>
        protected override void SetClientTransformFrame(uint clientID, NetsquareTransformFrame transform)
        {
            if (Clients.ContainsKey(clientID))
            {
                Clients[clientID].SetPostition(transform);
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
        protected override void SynchLoop()
        {
            if (Chunks == null)
            {
                return;
            }
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
                        NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageType.SetTransformsFramesPacked);
                        // iterate on each clients in the chunk
                        lock (chunk.Clients)
                        {
                            foreach (var client in chunk.Clients.Values)
                            {
                                // if client has transform frames to send
                                if (ClientsTransformFrames.ContainsKey(client.ClientID) && ClientsTransformFrames[client.ClientID].Count > 0)
                                {
                                    // pack client id and frames count
                                    synchMessage.Set(new UInt24(client.ClientID));
                                    synchMessage.Set((byte)ClientsTransformFrames[client.ClientID].Count);
                                    // iterate on each frames of the client to pack them
                                    lock (ClientsTransformFrames)
                                    {
                                        foreach (var frame in ClientsTransformFrames[client.ClientID])
                                        {
                                            frame.Serialize(synchMessage);
                                        }
                                        // clear frames
                                        ClientsTransformFrames[client.ClientID].Clear();
                                    }
                                }
                            }
                            // send message to clients
                            if (synchMessage.HasBlock)
                                World.server.SendToClients(synchMessage, chunk.Clients.Keys);
                        }
                    }
                }
            }
        }

        private void RefreshClientChunk(ChunkedClient client)
        {
            // get new chunk position
            short chunkX = (short)Math.Round((client.Position.x - Bounds.MinX) / ChunkSize, MidpointRounding.ToEven);
            short chunkY = (short)Math.Round((client.Position.z - Bounds.MinY) / ChunkSize, MidpointRounding.ToEven);
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
            NetworkMessage leavingMessage = new NetworkMessage(NetSquareMessageType.ClientsLeaveWorld);
            // pack message
            foreach (uint oldVisible in client.VisibleIDs)
                if (!chunk.Clients.ContainsKey(oldVisible)) // client just leave FOV
                    leavingMessage.Set(new UInt24(oldVisible));
            // send packed message to client
            if (leavingMessage.HasBlock)
                World.server.SendToClient(leavingMessage, client.ClientID);

            // joining clients
            NetworkMessage JoiningPacked = new NetworkMessage(NetSquareMessageType.ClientsJoinWorld);
            List<NetworkMessage> JoiningClientMessages = new List<NetworkMessage>();

            // iterate on each clients in my spatializer
            lock (chunk.Clients)
            {
                foreach (uint clientID in chunk.Clients.Keys)
                    if (!client.VisibleIDs.Contains(clientID)) // new client in FOV
                    {
                        //create new join message
                        NetworkMessage joiningClientMessage = new NetworkMessage(0, clientID);
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

            // client has move since last spatialization
            if (!client.Position.Equals(client.LastPosition))
            {
                client.LastPosition = client.Position;
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
            Width = (short)((xEnd - xStart) / ChunkSize);
            Height = (short)((yEnd - yStart) / ChunkSize);
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

        public override NetsquareTransformFrame GetClientTransform(uint clientID)
        {
            if (Clients.ContainsKey(clientID))
                return Clients[clientID].Position;
            return NetsquareTransformFrame.zero;
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