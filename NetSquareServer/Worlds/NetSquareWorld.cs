using NetSquare.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the net square world component.
    /// </summary>
    public class NetSquareWorld
    {
        /// <summary>
        /// Occurs when show static entities is raised.
        /// </summary>
        public event Action<uint, List<StaticEntity>> OnShowStaticEntities;
        /// <summary>
        /// Occurs when hide static entities is raised.
        /// </summary>
        public event Action<uint, List<StaticEntity>> OnHideStaticEntities;
        /// <summary>
        /// Occurs when client join world is raised.
        /// </summary>
        public event Action<uint, NetsquareTransformFrame> OnClientJoinWorld;
        /// <summary>
        /// Gets or sets the id value.
        /// </summary>
        public ushort ID { get; private set; }
        /// <summary>
        /// Gets or sets the clients value.
        /// </summary>
        public ConcurrentDictionary<uint, NetsquareTransformFrame> Clients { get; private set; }
        /// <summary>
        /// Gets or sets the max clients in world value.
        /// </summary>
        public ushort MaxClientsInWorld { get; private set; }
        /// <summary>
        /// Gets or sets the name value.
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// Gets or sets the use spatializer value.
        /// </summary>
        public bool UseSpatializer { get; private set; }
        /// <summary>
        /// Gets or sets the use synchronizer value.
        /// </summary>
        public bool UseSynchronizer { get; private set; }
        /// <summary>
        /// Gets or sets the spatializer value.
        /// </summary>
        public Spatializer Spatializer { get; private set; }
        /// <summary>
        /// Gets or sets the synchronizer value.
        /// </summary>
        public Synchronizer Synchronizer { get; private set; }
        /// <summary>
        /// Stores the server value.
        /// </summary>
        internal NetSquareServer server;

        /// <summary>
        /// instantiate a new World
        /// </summary>
        /// <param name="id">ID of the world (must be unique)</param>
        /// <param name="name">Name of the World</param>
        /// <param name="maxClients">Number max oc clients in this world</param>
        public NetSquareWorld(NetSquareServer _server, ushort id, string name = "", ushort maxClients = 128)
        {
            if (string.IsNullOrEmpty(name))
                name = "World " + id;
            ID = id;
            MaxClientsInWorld = maxClients;
            Clients = new ConcurrentDictionary<uint, NetsquareTransformFrame>();
            server = _server;
            Name = name;
        }

        /// <summary>
        /// fire OnShowStaticEntities event
        /// </summary>
        /// <param name="clientID">ID of the client</param>
        /// <param name="entities">entities to show</param>
        internal void Fire_OnShowStaticEntities(uint clientID, List<StaticEntity> entities)
        {
            OnShowStaticEntities?.Invoke(clientID, entities);
        }

        /// <summary>
        /// fire OnHideStaticEntities event
        /// </summary>
        /// <param name="clientID">ID of the client</param>
        /// <param name="entities">entities to show</param>
        internal void Fire_OnHideStaticEntities(uint clientID, List<StaticEntity> entities)
        {
            OnHideStaticEntities?.Invoke(clientID, entities);
        }

        /// <summary>
        /// Start synchronizer, use it if you send sync messages (such as position / rotation / input / annimation / ...)
        /// </summary>
        /// <param name="frequency">frequency of the synchronization (Hz => times / s)</param>
        public void StartSynchronizer(int frequency = -1, bool synchronizeUsingUdp = false)
        {
            if (frequency <= 0)
                frequency = NetSquareConfigurationManager.Configuration.SynchronizingFrequency;
            if (frequency > 60)
                frequency = 60;
            Synchronizer = new Synchronizer(server, this, synchronizeUsingUdp);
            Synchronizer.StartSynchronizing(frequency);
            UseSynchronizer = true;
        }

        /// <summary>
        /// Stop synchronization for this world
        /// </summary>
        public void StopUsingSynchronizer()
        {
            Synchronizer.Stop();
            UseSynchronizer = false;
        }

        /// <summary>
        /// synchronization will now use spatialization for better sync performances (use it on large worlds)
        /// Set as null to stop using spatialization
        /// </summary>
        public void SetSpatializer(Spatializer spatializer)
        {
            // if spatializer is null, stop using spatializer
            if (spatializer == null)
            {
                if (Spatializer != null)
                {
                    Spatializer.Stop();
                    Spatializer = null;
                }
                UseSpatializer = false;
                return;
            }

            // if spatializer is not null, start using spatializer
            if (Spatializer != null && Spatializer != spatializer)
                Spatializer.Stop();

            Spatializer = spatializer;
            UseSpatializer = true;
            foreach (uint clientID in Clients.Keys)
            {
                ConnectedClient client = server.SafeGetClient(clientID);
                if (client != null)
                    Spatializer.AddClient(client);
            }
        }

        /// <summary>
        /// Try to add a client to this world. Can fail if world is full or client already is in this world
        /// </summary>
        /// <param name="clientID">id of the client to add</param>
        /// <param name="clientTransform">transform of the client</param>
        /// <returns>true if success</returns>
        public bool TryJoinWorld(uint clientID, NetsquareTransformFrame clientTransform)
        {
            if (Clients.Count >= MaxClientsInWorld)
                return false;

            if (!Clients.TryAdd(clientID, clientTransform))
                return false;

            if (Clients.Count > MaxClientsInWorld)
            {
                NetsquareTransformFrame removedTransform;
                Clients.TryRemove(clientID, out removedTransform);
                return false;
            }

            try
            {
                if (Spatializer != null)
                {
                    ConnectedClient client = server.SafeGetClient(clientID);
                    if (client == null)
                    {
                        NetsquareTransformFrame removedTransform;
                        Clients.TryRemove(clientID, out removedTransform);
                        return false;
                    }

                    Spatializer.AddClient(client);
                }
            }
            catch
            {
                NetsquareTransformFrame removedTransform;
                Clients.TryRemove(clientID, out removedTransform);
                throw;
            }

            OnClientJoinWorld?.Invoke(clientID, clientTransform);
            return true;
        }

        /// <summary>
        /// Try to remove a client from this world. Can fail if client not is in this world
        /// </summary>
        /// <param name="clientID">id of the client to remove</param>
        /// <returns>true if success</returns>
        public bool TryLeaveWorld(uint clientID)
        {
            NetsquareTransformFrame clientTransform;
            if (!Clients.TryRemove(clientID, out clientTransform))
                return false;

            Spatializer?.RemoveClient(clientID);
            return true;
        }

        /// <summary>
        /// Add a spatialized static entity to the world. Only if this world use a spatializer
        /// </summary>
        /// <param name="type">Type of the entity</param>
        /// <param name="id">ID of  the entity</param>
        /// <param name="pos">Position of the entity</param>
        public void AddStaticEntity(short type, uint id, NetsquareTransformFrame pos)
        {
            Spatializer?.AddStaticEntity(type, id, pos);
        }

        /// <summary>
        /// Set the transform of a client in this world
        /// </summary>
        /// <param name="clientID"> ID of the client</param>
        /// <param name="transform"> new transform of the client</param>
        public void SetClientTransform(uint clientID, NetsquareTransformFrame transform)
        {
            NetsquareTransformFrame currentTransform;
            while (Clients.TryGetValue(clientID, out currentTransform))
            {
                if (Clients.TryUpdate(clientID, transform, currentTransform))
                {
                    server.Worlds.Fire_OnClientMove(clientID, transform);
                    return;
                }
            }
        }

        #region Debug Snapshot
        /// <summary>
        /// Creates a thread-safe debug snapshot of this world.
        /// </summary>
        /// <returns>World debug snapshot.</returns>
        public NetSquareWorldSnapshot CreateSnapshot()
        {
            return CreateSnapshot(true);
        }

        /// <summary>
        /// Creates a thread-safe debug snapshot of this world.
        /// </summary>
        /// <param name="includeDetails">Whether to include per-client and visibility details.</param>
        /// <returns>World debug snapshot.</returns>
        public NetSquareWorldSnapshot CreateSnapshot(bool includeDetails)
        {
            NetSquareWorldSnapshot snapshot = new NetSquareWorldSnapshot
            {
                ID = ID,
                Name = Name,
                ClientCount = Clients.Count,
                MaxClientsInWorld = MaxClientsInWorld,
                UseSynchronizer = UseSynchronizer,
                UseSpatializer = UseSpatializer
            };

            if (includeDetails)
            {
                foreach (var pair in Clients)
                {
                    NetsquareTransformFrame transform = pair.Value;
                    snapshot.Clients.Add(new NetSquareWorldClientSnapshot
                    {
                        ClientID = pair.Key,
                        X = transform.x,
                        Y = transform.y,
                        Z = transform.z
                    });
                }
            }

            if (UseSpatializer && Spatializer != null)
            {
                snapshot.Spatializer = Spatializer.CreateSnapshot(includeDetails);
                if (includeDetails)
                    ApplySpatializerClientData(snapshot);
            }

            return snapshot;
        }

        /// <summary>
        /// Applies spatializer visibility and pending-frame data to client snapshots.
        /// </summary>
        /// <param name="snapshot">World snapshot to enrich.</param>
        private static void ApplySpatializerClientData(NetSquareWorldSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Spatializer == null)
                return;

            for (int i = 0; i < snapshot.Clients.Count; i++)
            {
                NetSquareWorldClientSnapshot client = snapshot.Clients[i];
                List<uint> visible;
                if (snapshot.Spatializer.VisibleClientsByClientID.TryGetValue(client.ClientID, out visible))
                    client.VisibleClientIDs = visible;

                int pendingFrames;
                if (snapshot.Spatializer.PendingFramesByClientID.TryGetValue(client.ClientID, out pendingFrames))
                    client.PendingFrameCount = pendingFrames;
            }
        }
        #endregion

        #region Broadcast
        /// <summary>
        /// Executes the get broadcast targets operation.
        /// </summary>
        private HashSet<uint> GetBroadcastTargets(uint clientID, bool useSpatialization)
        {
            if (UseSpatializer && useSpatialization && Spatializer != null)
                return Spatializer.GetVisibleClients(clientID);

            return new HashSet<uint>(Clients.Keys);
        }

        /// <summary>
        /// Executes the remove excluded clients operation.
        /// </summary>
        private static void RemoveExcludedClients(HashSet<uint> clients, IEnumerable<uint> excludedClientIDs)
        {
            if (excludedClientIDs == null)
                return;

            foreach (uint excludedClientID in excludedClientIDs)
                clients.Remove(excludedClientID);
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void Broadcast(NetworkMessage message, bool useSpatialization = true)
        {
            server.SendToClients(message, GetBroadcastTargets(message.ClientID, useSpatialization));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void Broadcast(NetworkMessage message, uint excludedClientID, bool useSpatialization = true)
        {
            HashSet<uint> clients = GetBroadcastTargets(message.ClientID, useSpatialization);
            clients.Remove(excludedClientID);
            server.SendToClients(message, clients);
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void Broadcast(NetworkMessage message, IEnumerable<uint> excludedClientIDs, bool useSpatialization = true)
        {
            HashSet<uint> clients = GetBroadcastTargets(message.ClientID, useSpatialization);
            RemoveExcludedClients(clients, excludedClientIDs);
            server.SendToClients(message, clients);
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void Broadcast(byte[] message, uint clientID, bool useSpatialization = true)
        {
            server.SendToClients(message, GetBroadcastTargets(clientID, useSpatialization));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void BroadcastUDP(NetworkMessage message, bool useSpatialization = true)
        {
            server.SendToClientsUDP(message, GetBroadcastTargets(message.ClientID, useSpatialization));
        }
        #endregion
    }
}
