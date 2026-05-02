using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the simple spatializer component.
    /// </summary>
    public class SimpleSpatializer : Spatializer
    {
        /// <summary>
        /// Stores the clients value.
        /// </summary>
        public ConcurrentDictionary<uint, SpatialClient> Clients;
        /// <summary>
        /// Stores the static entities value.
        /// </summary>
        public List<StaticEntity> StaticEntities;
        /// <summary>
        /// Gets or sets the max view distance value.
        /// </summary>
        public float MaxViewDistance { get; private set; }
        /// <summary>
        /// Gets or sets the extra distance a visible client can move before leaving visibility.
        /// </summary>
        public float VisibilityHysteresis { get; set; }
        /// <summary>
        /// Stores the static entities lock value.
        /// </summary>
        private readonly object staticEntitiesLock = new object();

        /// <summary>
        /// Instantiate a new simple spatializer based on distance between clients
        /// </summary>
        /// <param name="world"> world to spatialize</param>
        /// <param name="spatializationFreq"> frequency of spatialization loop</param>
        /// <param name="synchFreq"> frequency of synch loop</param>
        /// <param name="maxViewDistance"> maximum view distance of the clients</param>
        public SimpleSpatializer(NetSquareWorld world, float spatializationFreq, float synchFreq, float maxViewDistance, float visibilityHysteresis = 0f) : base(world, spatializationFreq, synchFreq)
        {
            MaxViewDistance = maxViewDistance;
            VisibilityHysteresis = visibilityHysteresis < 0f ? 0f : visibilityHysteresis;
            Clients = new ConcurrentDictionary<uint, SpatialClient>();
            StaticEntities = new List<StaticEntity>();
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

            SpatialClient spatializedClient = new SpatialClient(this, client);
            Clients.TryAdd(client.ID, spatializedClient);
        }

        /// <summary>
        /// Remove a client from the spatializer
        /// </summary>
        /// <param name="clientID">ID of the client to remove</param>
        public override void RemoveClient(uint clientID)
        {
            SpatialClient client;
            Clients.TryRemove(clientID, out client);
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
            SpatialClient client;
            if (!Clients.TryGetValue(clientID, out client))
                return new HashSet<uint>();

            lock (client.SyncRoot)
                return new HashSet<uint>(client.VisibleIDs);
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
            Dictionary<uint, List<INetSquareSynchFrame>> frameSnapshot = DrainStoredFrames();
            if (frameSnapshot.Count == 0)
                return;

            foreach (var client in Clients)
            {
                // get visible clients
                HashSet<uint> visibleClients = GetVisibleClients(client.Key);
                // create new synch message
                NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageID.SetSynchFramesPacked);

                // add visible clients to the message
                foreach (var visibleClient in visibleClients)
                {
                    List<INetSquareSynchFrame> frames;
                    if (frameSnapshot.TryGetValue(visibleClient, out frames) && frames.Count > 0)
                        NetSquareSynchFramesUtils.SerializePackedFrames(synchMessage, visibleClient, frames);
                }
                // send message to client
                if (synchMessage.HasWriteData)
                    World.server.SendToClient(synchMessage, client.Key);
            }
        }

        /// <summary>
        /// Executes the for each operation.
        /// </summary>
        public override void ForEach(Action<uint, IEnumerable<uint>> callback)
        {
            foreach (var client in Clients)
            {
                HashSet<uint> visible = GetVisibleClients(client.Key);
                callback(client.Key, visible);
            }
        }

        /// <summary>
        /// Creates a debug snapshot of this simple spatializer.
        /// </summary>
        /// <returns>Spatializer debug snapshot.</returns>
        public override NetSquareSpatializerSnapshot CreateSnapshot()
        {
            NetSquareSpatializerSnapshot snapshot = base.CreateSnapshot();
            snapshot.MaxViewDistance = MaxViewDistance;
            snapshot.VisibilityHysteresis = VisibilityHysteresis;
            return snapshot;
        }

        /// <summary>
        /// Executes the add static entity operation.
        /// </summary>
        public override void AddStaticEntity(short type, uint id, NetsquareTransformFrame pos)
        {
            lock (staticEntitiesLock)
                StaticEntities.Add(new StaticEntity(type, id, pos));
            StaticEntitiesCount++;
        }

        /// <summary>
        /// Executes the get clients snapshot operation.
        /// </summary>
        internal List<SpatialClient> GetClientsSnapshot()
        {
            List<SpatialClient> snapshot = new List<SpatialClient>();
            foreach (var pair in Clients)
                snapshot.Add(pair.Value);
            return snapshot;
        }

        /// <summary>
        /// Executes the get static entities snapshot operation.
        /// </summary>
        internal List<StaticEntity> GetStaticEntitiesSnapshot()
        {
            lock (staticEntitiesLock)
                return new List<StaticEntity>(StaticEntities);
        }
    }
}
#endregion
