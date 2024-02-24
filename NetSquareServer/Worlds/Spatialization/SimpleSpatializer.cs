using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public class SimpleSpatializer : Spatializer
    {
        public ConcurrentDictionary<uint, SpatialClient> Clients;
        public List<StaticEntity> StaticEntities;
        public float MaxViewDistance { get; private set; }

        /// <summary>
        /// Instantiate a new simple spatializer based on distance between clients
        /// </summary>
        /// <param name="world"> world to spatialize</param>
        /// <param name="spatializationFreq"> frequency of spatialization loop</param>
        /// <param name="synchFreq"> frequency of synch loop</param>
        /// <param name="maxViewDistance"> maximum view distance of the clients</param>
        public SimpleSpatializer(NetSquareWorld world, float spatializationFreq, float synchFreq, float maxViewDistance) : base(world, spatializationFreq, synchFreq)
        {
            MaxViewDistance = maxViewDistance;
            Clients = new ConcurrentDictionary<uint, SpatialClient>();
            StaticEntities = new List<StaticEntity>();
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
            SpatialClient spatializedClient = new SpatialClient(this, client, transform);
            if (!Clients.ContainsKey(client.ID))
                while (!Clients.TryAdd(client.ID, spatializedClient))
                    continue;
        }

        /// <summary>
        /// Remove a client from the spatializer
        /// </summary>
        /// <param name="clientID">ID of the client to remove</param>
        public override void RemoveClient(uint clientID)
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
        /// <param name="transform">position</param>
        protected override void SetClientTransformFrame(uint clientID, NetsquareTransformFrame transform)
        {
            if (Clients.ContainsKey(clientID))
                Clients[clientID].Transform = transform;
        }

        /// <summary>
        /// get all visible clients for a given client, according to a maximum view distance
        /// </summary>
        /// <param name="clientID">ID of the client to get visibles</param>
        /// <param name="maxDistance">maximum view distance of  the client</param>
        /// <returns></returns>
        public override HashSet<uint> GetVisibleClients(uint clientID)
        {
            if (!Clients.ContainsKey(clientID))
                return new HashSet<uint>();
            return new HashSet<uint>(Clients[clientID].VisibleIDs);
        }

        /// <summary>
        /// Main spatialization loop
        /// Process visible clients and static entities
        /// </summary>
        protected override void SpatializationLoop()
        {
            foreach (var client in Clients)
            {
                client.Value.ProcessVisibleClients();
                client.Value.ProcessVisibleStaticEntities();
            }
        }

        /// <summary>
        /// Synchronization loop that pack and send visible clients to the clients
        /// </summary>
        protected override unsafe void SynchLoop()
        {
            foreach (var client in Clients)
            {
                lock (Clients)
                {
                    // get visible clients
                    HashSet<uint> visibleClients = GetVisibleClients(client.Key);
                    // create new synch message
                    NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageType.SetTransformsFramesPacked);

                    // add visible clients to the message
                    foreach (var visibleClient in visibleClients)
                    {
                        // if client has transform frames to send
                        if (ClientsTransformFrames.ContainsKey(visibleClient) && ClientsTransformFrames[visibleClient].Count > 0)
                        {
                            // create new byte array to pack transform frames for this client
                            UInt24 clientId = new UInt24(visibleClient);
                            byte nbFrames = (byte)ClientsTransformFrames[visibleClient].Count;
                            byte[] bytes = new byte[4 + nbFrames * 33];
                            // write transform values using pointer
                            fixed (byte* p = bytes)
                            {
                                // write client id
                                *p = clientId.b0;
                                *(p + 1) = clientId.b1;
                                *(p + 2) = clientId.b2;

                                // lock client frames list so we can read it safely
                                lock (ClientsTransformFrames)
                                {
                                    // write frames count
                                    *(p + 3) = nbFrames;

                                    // iterate on each frames of the client to pack them
                                    for (byte i = 0; i < nbFrames; i++)
                                    {
                                        ClientsTransformFrames[visibleClient][i].Serialize(p + 4 + i * 33);
                                    }
                                    // clear frames
                                    ClientsTransformFrames[visibleClient].Clear();
                                }
                            }
                            // set message bytes
                            synchMessage.Set(bytes, false);
                        }
                    }
                    // send message to client
                    if (synchMessage.HasBlock)
                        World.server.SendToClient(synchMessage, client.Key);
                }
            }
        }

        public override NetsquareTransformFrame GetClientTransform(uint clientID)
        {
            if (Clients.ContainsKey(clientID))
                return Clients[clientID].Transform;
            return NetsquareTransformFrame.zero;
        }

        public override void ForEach(Action<uint, IEnumerable<uint>> callback)
        {
            foreach (var client in Clients)
                callback(client.Key, GetVisibleClients(client.Key));
        }

        public override void AddStaticEntity(short type, uint id, NetsquareTransformFrame pos)
        {
            StaticEntities.Add(new StaticEntity(type, id, pos));
            StaticEntitiesCount++;
        }
    }
}