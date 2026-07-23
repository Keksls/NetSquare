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
        /// Stores the reusable clients snapshot for spatialization ticks.
        /// </summary>
        private readonly List<SpatialClient> spatializationClients = new List<SpatialClient>();
        /// <summary>
        /// Stores the reusable static-entity snapshot for spatialization ticks.
        /// </summary>
        private readonly List<StaticEntity> spatializationStaticEntities = new List<StaticEntity>();
        /// <summary>
        /// Stores the reusable visibility buffer for synchronization ticks.
        /// </summary>
        private readonly List<uint> synchronizationVisibleClientIDs = new List<uint>();

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
            // Build shared reusable snapshots once per tick instead of once for every client.
            spatializationClients.Clear();
            foreach (KeyValuePair<uint, SpatialClient> client in Clients)
                spatializationClients.Add(client.Value);

            spatializationStaticEntities.Clear();
            lock (staticEntitiesLock)
                spatializationStaticEntities.AddRange(StaticEntities);

            foreach (SpatialClient client in spatializationClients)
            {
                client.ProcessVisibleClients(spatializationClients);
                client.ProcessVisibleStaticEntities(spatializationStaticEntities);
            }
        }
        /// <summary>
        /// Synchronization loop that pack and send visible clients to the clients
        /// </summary>
        protected override unsafe void SynchLoop()
        {
            Dictionary<uint, List<INetSquareSynchFrame>> frameSnapshot = DrainStoredFrames();
            try
            {
                if (frameSnapshot.Count == 0)
                    return;

                // Reuse one sequential visibility buffer for the whole synchronization pass.
                List<uint> visibleClientIDs = synchronizationVisibleClientIDs;
                foreach (KeyValuePair<uint, SpatialClient> client in Clients)
                {
                    visibleClientIDs.Clear();
                    lock (client.Value.SyncRoot)
                        visibleClientIDs.AddRange(client.Value.VisibleIDs);

                    if (visibleClientIDs.Count == 0)
                        continue;

                    NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageID.SetSynchFramesPacked);
                    foreach (uint visibleClientID in visibleClientIDs)
                    {
                        List<INetSquareSynchFrame> frames;
                        if (frameSnapshot.TryGetValue(visibleClientID, out frames) && frames.Count > 0)
                            NetSquareSynchFramesUtils.SerializePackedFrames(synchMessage, visibleClientID, frames);
                    }

                    if (synchMessage.HasWriteData)
                        World.server.SendToClient(synchMessage, client.Key);
                }
            }
            finally
            {
                ReturnDrainedFrames(frameSnapshot);
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
            List<SpatialClient> snapshot = new List<SpatialClient>(Clients.Count);
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
