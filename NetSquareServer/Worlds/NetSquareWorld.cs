using NetSquare.Core;
using NetSquareCore;
using System;
using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public class NetSquareWorld
    {
        public event Action<uint, List<StaticEntity>> OnShowStaticEntities;
        public event Action<uint, List<StaticEntity>> OnHideStaticEntities;
        public event Action<uint> OnClientJoinWorld;
        public ushort ID { get; private set; }
        public HashSet<uint> Clients { get; private set; }
        public ushort MaxClientsInWorld { get; private set; }
        public string Name { get; private set; }
        public bool UseSpatializer { get; private set; }
        public bool UseSynchronizer { get; private set; }
        public Spatializer Spatializer { get; private set; }
        public Synchronizer Synchronizer { get; private set; }
        internal NetSquare_Server server;

        /// <summary>
        /// instantiate a new World
        /// </summary>
        /// <param name="id">ID of the world (must be unique)</param>
        /// <param name="name">Name of the World</param>
        /// <param name="maxClients">Number max oc clients in this world</param>
        public NetSquareWorld(NetSquare_Server _server, ushort id, string name = "", ushort maxClients = 128)
        {
            if (string.IsNullOrEmpty(name))
                name = "World " + id;
            ID = id;
            MaxClientsInWorld = maxClients;
            Clients = new HashSet<uint>();
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
            Spatializer = spatializer;
            UseSpatializer = true;
        }

        /// <summary>
        /// Try to add a client to this world. Can fail if world is full or client already is in this world
        /// </summary>
        /// <param name="clientID">id of the client to add</param>
        /// <returns>true if success</returns>
        public bool TryJoinWorld(uint clientID)
        {
            if (Clients.Contains(clientID) || Clients.Count >= MaxClientsInWorld)
                return false;
            Clients.Add(clientID);
            Spatializer?.AddClient(server.GetClient(clientID));
            OnClientJoinWorld?.Invoke(clientID);
            return true;
        }

        /// <summary>
        /// Try to remove a client from this world. Can fail if client not is in this world
        /// </summary>
        /// <param name="clientID">id of the client to remove</param>
        /// <returns>true if success</returns>
        public bool TryLeaveWorld(uint clientID)
        {
            Spatializer?.RemoveClient(clientID);
            if (Clients.Remove(clientID))
            {
                return true;
            }
            return false;
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

        #region Broadcast
        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void Broadcast(NetworkMessage message, bool useSpatialization = true)
        {
            if (UseSpatializer && useSpatialization)
                server.SendToClients(message, Spatializer.GetVisibleClients(message.ClientID));
            else
                server.SendToClients(message, new HashSet<uint>(Clients));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void Broadcast(NetworkMessage message, uint excludedClientID, bool useSpatialization = true)
        {
            if (UseSpatializer && useSpatialization)
                server.SendToClients(message, Spatializer.GetVisibleClients(message.ClientID));
            else
            {
                HashSet<uint> clients = new HashSet<uint>(Clients);
                clients.Remove(excludedClientID);
                server.SendToClients(message, clients);
            }
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void Broadcast(NetworkMessage message, IEnumerable<uint> excludedClientIDs, bool useSpatialization = true)
        {
            if (UseSpatializer && useSpatialization)
                server.SendToClients(message, Spatializer.GetVisibleClients(message.ClientID));
            else
            {
                HashSet<uint> clients = new HashSet<uint>(Clients);
                foreach (uint excludedClientID in excludedClientIDs)
                    clients.Remove(excludedClientID);
                server.SendToClients(message, clients);
            }
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void Broadcast(byte[] message, uint clientID, bool useSpatialization = true)
        {
            if (UseSpatializer && useSpatialization)
                server.SendToClients(message, Spatializer.GetVisibleClients(clientID));
            else
                server.SendToClients(message, new HashSet<uint>(Clients));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="useSpatialization">if this world use spatialization, broadcast to anyone visible only</param>
        public void BroadcastUDP(NetworkMessage message, bool useSpatialization = true)
        {
            if (UseSpatializer && useSpatialization)
                server.SendToClientsUDP(message, Spatializer.GetVisibleClients(message.ClientID));
            else
                server.SendToClientsUDP(message, new HashSet<uint>(Clients));
        }
        #endregion
    }
}