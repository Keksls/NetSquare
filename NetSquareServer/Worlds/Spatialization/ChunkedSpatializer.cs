using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using NetSquareServer.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        public bool Started { get; private set; }
        public int Frequency { get; private set; }
        private ConcurrentDictionary<uint, ChunkedClient> Clients;

        public ChunkedSpatializer(NetSquareWorld world, float chunkSize, float xStart, float yStart, float xEnd, float yEnd) : base(world)
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
            AddClient(client, Transform.zero);
        }

        /// <summary>
        /// add a client to this spatializer and set his position
        /// </summary>
        /// <param name="clientID">ID of the client to add</param>
        /// <param name="pos">spawn position</param>
        public override void AddClient(ConnectedClient client, Transform pos)
        {
            var chunk = GetChunkForPosition(pos);
            if (chunk != null)
            {
                ChunkedClient chunkClient = new ChunkedClient(client.ID, chunk.x, chunk.y, pos);
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
        /// <param name="pos">position</param>
        public override void SetClientPosition(uint clientID, Transform pos)
        {
            if (Clients.ContainsKey(clientID))
            {
                Clients[clientID].SetPostition(pos);
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
        /// Start spatializer loop that handle clients Spawn and unspawn
        /// </summary>
        /// <param name="frequency">frequency of loop processing</param>
        public override void StartSpatializer(float frequency)
        {
            if (Started)
                return;

            if (frequency <= 0f)
                frequency = 1f;
            if (frequency > 30f)
                frequency = 30f;
            Frequency = (int)((1f / frequency) * 1000f);
            Started = true;
            Thread spatializerThread = new Thread(SpawnUnspawnLoop);
            spatializerThread.IsBackground = true;
            spatializerThread.Start();
        }

        /// <summary>
        /// Stop spatializer Spawn / Unspawn loop
        /// </summary>
        public override void StopSpatializer()
        {
            Started = false;
        }

        Stopwatch spatialWatch = new Stopwatch();
        private void SpawnUnspawnLoop()
        {
            while (Started)
            {
                spatialWatch.Reset();
                spatialWatch.Start();

                // refresh chunked client chunks
                foreach (var client in Clients)
                    RefreshClientChunk(client.Value);

                Thread.Sleep(1);

                // process visible clients
                foreach (var client in Clients)
                    ProcessVisible(client.Value);

                spatialWatch.Stop();
                int freq = Frequency - (int)spatialWatch.ElapsedMilliseconds;
                if (freq <= 0)
                    freq = 1;
                Thread.Sleep(freq);
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

                World.Fire_OnHideStaticEntities(client.ClientID, oldChunk.StaticEntities);
                client.SetChunk(chunkX, chunkY);
                World.Fire_OnHideStaticEntities(client.ClientID, newChunk.StaticEntities);
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
                World.server.Worlds.Fire_OnSpatializePlayer(World.ID, client.ClientID, client.Position);
                client.LastPosition = client.Position;
            }

        }

        private SpatialChunk GetChunkForPosition(Transform position)
        {
            short chunkX, chunkY;
            GetChunkPosition(position, out chunkX, out chunkY);
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

        private void GetChunkPosition(Transform position, out short chunkX, out short chunkY)
        {
            chunkX = (short)Math.Round((position.x - Bounds.MinX) / ChunkSize, MidpointRounding.ToEven);
            chunkY = (short)Math.Round((position.z - Bounds.MinY) / ChunkSize, MidpointRounding.ToEven);
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

        public override Transform GetClientPosition(uint clientID)
        {
            if (Clients.ContainsKey(clientID))
                return Clients[clientID].Position;
            return Transform.zero;
        }

        public override void ForEach(Action<uint, IEnumerable<uint>> callback)
        {
            foreach (var client in Clients)
            {
                lock (client.Value.VisibleIDs)
                    callback(client.Key, client.Value.VisibleIDs);
            }
        }

        public override void AddStaticEntity(short type, uint id, Transform pos)
        {
            short chunkX, chunkY;
            GetChunkPosition(pos, out chunkX, out chunkY);
            if (HasChunk(chunkX, chunkY))
            {
                GetChunk(chunkX, chunkY).StaticEntities.Add(new StaticEntity(type, id, pos));
                StaticEntitiesCount++;
            }
            else
                Writer.Write("Fail adding static entity. can't get chunk for pos " + pos.x + " " + pos.y, ConsoleColor.Red);
        }
    }
}