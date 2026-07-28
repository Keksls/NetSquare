using NetSquare.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

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
        /// Gets the current lifecycle state.
        /// </summary>
        public NetSquareWorldLifecycleState LifecycleState
        {
            get { return (NetSquareWorldLifecycleState)Volatile.Read(ref lifecycleState); }
        }
        /// <summary>
        /// Gets whether this world still accepts operations.
        /// </summary>
        public bool IsActive { get { return LifecycleState == NetSquareWorldLifecycleState.Active; } }
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
        /// Coordinates synchronizer and spatializer lifecycle changes.
        /// </summary>
        private readonly object lifecycleLock = new object();
        /// <summary>
        /// Stores the atomic lifecycle state.
        /// </summary>
        private int lifecycleState;

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
            lock (lifecycleLock)
            {
                EnsureActive();
                if (UseSynchronizer)
                    return;

                if (frequency <= 0)
                    // Read the shared server setting through the initialized configuration contract.
                    frequency = NetSquareConfigurationManager.Get<NetSquareConfiguration>().SynchronizingFrequency;
                if (frequency > 60)
                    frequency = 60;

                Synchronizer synchronizer = new Synchronizer(server, this, synchronizeUsingUdp);
                synchronizer.StartSynchronizing(frequency);
                Synchronizer = synchronizer;
                UseSynchronizer = true;
            }
        }

        /// <summary>
        /// Stop synchronization for this world
        /// </summary>
        public void StopUsingSynchronizer()
        {
            lock (lifecycleLock)
            {
                Synchronizer synchronizer = Synchronizer;
                if (synchronizer == null)
                    return;

                synchronizer.Stop();
                Synchronizer = null;
                UseSynchronizer = false;
            }
        }

        /// <summary>
        /// synchronization will now use spatialization for better sync performances (use it on large worlds)
        /// Set as null to stop using spatialization
        /// </summary>
        public void SetSpatializer(Spatializer spatializer)
        {
            lock (lifecycleLock)
            {
                if (spatializer != null)
                    EnsureActive();

                // A null value releases the currently installed spatializer.
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

                // Replace the previous worker only after the new instance was created successfully.
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
        }

        /// <summary>
        /// Atomically reserves this world for manager-owned removal.
        /// </summary>
        /// <returns>True when the world transitioned from active to removing.</returns>
        internal bool TryBeginRemoval()
        {
            lock (lifecycleLock)
            {
                if (!IsActive)
                    return false;

                Volatile.Write(
                    ref lifecycleState,
                    (int)NetSquareWorldLifecycleState.Removing);
                return true;
            }
        }

        /// <summary>
        /// Restores an active state when the manager could not detach the world.
        /// </summary>
        internal void CancelRemoval()
        {
            Interlocked.CompareExchange(
                ref lifecycleState,
                (int)NetSquareWorldLifecycleState.Active,
                (int)NetSquareWorldLifecycleState.Removing);
        }

        /// <summary>
        /// Stops every owned worker and releases retained world state after manager detachment.
        /// </summary>
        internal void CompleteRemoval()
        {
            List<Exception> exceptions = null;
            lock (lifecycleLock)
            {
                try
                {
                    Synchronizer?.Stop();
                }
                catch (Exception exception)
                {
                    exceptions = new List<Exception> { exception };
                }

                try
                {
                    Spatializer?.Stop();
                }
                catch (Exception exception)
                {
                    if (exceptions == null)
                        exceptions = new List<Exception>();
                    exceptions.Add(exception);
                }

                Synchronizer = null;
                Spatializer = null;
                UseSynchronizer = false;
                UseSpatializer = false;
                Clients.Clear();
                Volatile.Write(ref lifecycleState, (int)NetSquareWorldLifecycleState.Removed);
            }

            if (exceptions != null)
                throw new AggregateException("World resources failed to stop cleanly.", exceptions);
        }

        /// <summary>
        /// Rejects operations that would reactivate a removing or removed world.
        /// </summary>
        private void EnsureActive()
        {
            if (!IsActive)
                throw new InvalidOperationException(
                    "World " + ID + " is " + LifecycleState.ToString().ToLowerInvariant() + ".");
        }

        /// <summary>
        /// Try to add a client to this world. Can fail if world is full or client already is in this world
        /// </summary>
        /// <param name="clientID">id of the client to add</param>
        /// <param name="clientTransform">transform of the client</param>
        /// <returns>true if success</returns>
        public bool TryJoinWorld(uint clientID, NetsquareTransformFrame clientTransform)
        {
            lock (lifecycleLock)
            {
                if (!IsActive || Clients.Count >= MaxClientsInWorld)
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
        }

        /// <summary>
        /// Try to remove a client from this world. Can fail if client not is in this world
        /// </summary>
        /// <param name="clientID">id of the client to remove</param>
        /// <returns>true if success</returns>
        public bool TryLeaveWorld(uint clientID)
        {
            lock (lifecycleLock)
            {
                NetsquareTransformFrame clientTransform;
                if (!Clients.TryRemove(clientID, out clientTransform))
                    return false;

                Spatializer?.RemoveClient(clientID);
                return true;
            }
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
            if (!IsActive)
                return;

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
            NetSquareWorldSnapshot snapshot = new NetSquareWorldSnapshot
            {
                ID = ID,
                Name = Name,
                ClientCount = Clients.Count,
                MaxClientsInWorld = MaxClientsInWorld,
                UseSynchronizer = UseSynchronizer,
                UseSpatializer = UseSpatializer
            };

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

            if (UseSpatializer && Spatializer != null)
            {
                snapshot.Spatializer = Spatializer.CreateSnapshot();
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
        private HashSet<uint> GetBroadcastTargets(uint clientID, bool useSpatialization, bool excludeSelf)
        {
            if (!IsActive)
                return new HashSet<uint>();

            HashSet<uint> clients = null;

            if (UseSpatializer && useSpatialization && Spatializer != null && clientID != 0)
                clients = Spatializer.GetVisibleClients(clientID);
            else
                clients = new HashSet<uint>(Clients.Keys);

            if (excludeSelf)
                clients.Remove(clientID);

            return clients;
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
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void Broadcast(NetworkMessage message, bool useSpatialization = true, bool excludeSender = false)
        {
            server.SendToClients(message, GetBroadcastTargets(message.ClientID, useSpatialization, excludeSender));
        }

        /// <summary>
        /// Send message to anyone in this world using UDP
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void BroadcastUnreliable(NetworkMessage message, bool useSpatialization = true, bool excludeSender = false)
        {
            server.SendToClientsUnreliable(message, GetBroadcastTargets(message.ClientID, useSpatialization, excludeSender));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void Broadcast(NetworkMessage message, uint excludedClientID, bool useSpatialization = true, bool excludeSender = false)
        {
            HashSet<uint> clients = GetBroadcastTargets(message.ClientID, useSpatialization, excludeSender);
            clients.Remove(excludedClientID);
            server.SendToClients(message, clients);
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void Broadcast(NetworkMessage message, IEnumerable<uint> excludedClientIDs, bool useSpatialization = true, bool excludeSender = false)
        {
            HashSet<uint> clients = GetBroadcastTargets(message.ClientID, useSpatialization, excludeSender);
            RemoveExcludedClients(clients, excludedClientIDs);
            server.SendToClients(message, clients);
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void Broadcast(byte[] message, uint clientID, bool useSpatialization = true, bool excludeSender = false)
        {
            server.SendToClients(message, GetBroadcastTargets(clientID, useSpatialization, excludeSender));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void BroadcastUDP(NetworkMessage message, bool useSpatialization = true, bool excludeSender = false)
        {
            server.SendToClientsUnreliable(message, GetBroadcastTargets(message.ClientID, useSpatialization, excludeSender));
        }
        #endregion
    }
}
