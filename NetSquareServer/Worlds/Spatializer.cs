using NetSquare.Core;
using NetSquareCore;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace NetSquareServer.Worlds
{
    public class Spatializer
    {
        public ConcurrentDictionary<uint, SpatialClient> Clients;
        public float MaxViewDistance { get; private set; }
        public bool Started { get; private set; }
        public int Frequency { get; private set; }
        public NetSquareWorld World { get; private set; }

        public Spatializer(NetSquareWorld world)
        {
            World = world;
            Clients = new ConcurrentDictionary<uint, SpatialClient>();
        }

        /// <summary>
        /// Add a client to this spatializer
        /// </summary>
        /// <param name="client">ID of the client to add</param>
        public void AddClient(ConnectedClient client)
        {
            AddClient(client, Position.zero);
        }

        /// <summary>
        /// add a client to this spatializer and set his position
        /// </summary>
        /// <param name="clientID">ID of the client to add</param>
        /// <param name="pos">spawn position</param>
        public void AddClient(ConnectedClient client, Position pos)
        {
            SpatialClient spatializedClient = new SpatialClient(this, client, pos);
            if (!Clients.ContainsKey(client.ID.UInt32))
                while (!Clients.TryAdd(client.ID.UInt32, spatializedClient))
                    continue;
        }

        /// <summary>
        /// Remove a client from the spatializer
        /// </summary>
        /// <param name="clientID">ID of the client to remove</param>
        public void RemoveClient(uint clientID)
        {
            SpatialClient client;
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
        public void SetClientPosition(uint clientID, Position pos)
        {
            if (Clients.ContainsKey(clientID))
                Clients[clientID].Position = pos;
        }

        /// <summary>
        /// get all visible clients for a given client, according to a maximum view distance
        /// </summary>
        /// <param name="clientID">ID of the client to get visibles</param>
        /// <param name="maxDistance">maximum view distance of  the client</param>
        /// <returns></returns>
        public HashSet<uint> GetVisibleClients(uint clientID)
        {
            if (!Clients.ContainsKey(clientID))
                return new HashSet<uint>();
            return Clients[clientID].VisibleIDs;
        }

        /// <summary>
        /// Start spatializer loop that handle clients Spawn and unspawn
        /// </summary>
        /// <param name="frequency">frequency of loop processing</param>
        /// <param name="maxViewDistance">maximum client view distance (clients will spawn and unspawn according to this value)</param>
        public void StartSpatializer(int frequency, float maxViewDistance)
        {
            MaxViewDistance = maxViewDistance;
            if (frequency <= 0)
                frequency = 1;
            if (frequency > 30)
                frequency = 30;
            Frequency = (int)((1f / (float)frequency) * 1000f);
            Started = true;
            Thread spatializerThread = new Thread(SpawnUnspawnLoop);
            spatializerThread.IsBackground = true;
            spatializerThread.Start();
        }

        /// <summary>
        /// Stop spatializer Spawn / Unspawn loop
        /// </summary>
        public void StopSpatializer()
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
                foreach (var client in Clients)
                {
                    client.Value.ProcessVisible();

                    //// wait 1 ms for idle this thread, because frequency here is not realy important, better prevend server CPU usage
                    //Thread.Sleep(1);
                }
                spatialWatch.Stop();
                int freq = Frequency - (int)spatialWatch.ElapsedMilliseconds;
                if (freq <= 0)
                    freq = 1;
                Thread.Sleep(freq);
            }
        }
    }
}