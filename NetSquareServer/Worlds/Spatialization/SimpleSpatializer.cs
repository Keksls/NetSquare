using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NetSquare.Server.Worlds
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
            SpatialClient spatializedClient = new SpatialClient(this, client);
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
                    NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageID.SetSynchFramesPacked);

                    // add visible clients to the message
                    foreach (var visibleClient in visibleClients)
                    {
                        // if client has transform frames to send
                        if (ClientsTransformFrames.ContainsKey(visibleClient) && ClientsTransformFrames[visibleClient].Count > 0)
                        {
                            // create new byte array to pack transform frames for this client
                            UInt24 clientId = new UInt24(visibleClient);
                            ushort nbFrames = (ushort)ClientsTransformFrames[visibleClient].Count;
                            byte[] bytes = new byte[5 + nbFrames * NetsquareTransformFrame.Size];
                            // write transform values using pointer
                            fixed (byte* ptr = bytes)
                            {
                                byte* b = ptr;
                                // write client id
                                *b = clientId.b0;
                                b++;
                                *b = clientId.b1;
                                b++;
                                *b = clientId.b2;
                                b++;

                                // lock client frames list so we can read it safely
                                lock (ClientsTransformFrames)
                                {
                                    // write frames count
                                    *b = (byte)nbFrames;
                                    b++;
                                    *b = (byte)(nbFrames >> 8);
                                    b++;

                                    // iterate on each frames of the client to pack them
                                    for (ushort i = 0; i < nbFrames; i++)
                                    {
                                        ClientsTransformFrames[visibleClient][i].Serialize(ref b);
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
                    if (synchMessage.HasWriteData)
                        World.server.SendToClient(synchMessage, client.Key);
                }
            }
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