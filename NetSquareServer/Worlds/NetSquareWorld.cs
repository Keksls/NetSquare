using NetSquare.Core;
using NetSquareCore;
using System;
using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public class NetSquareWorld
    {
        public event Action<uint> OnClientJoinWorld;
        public ushort ID { get; private set; }
        public HashSet<uint> Clients { get; private set; }
        public ushort MaxClientsInWorld { get; private set; }
        public string Name { get; private set; }
        public bool UseSpatializer { get; private set; }
        public float SpatializerMaxDistance { get; private set; }
        public Spatializer Spatializer { get; private set; }
        internal NetSquare_Server server;

        /// <summary>
        /// instantiate a new World
        /// </summary>
        /// <param name="id">ID of the world (must be unique)</param>
        /// <param name="name">Name of the World</param>
        /// <param name="maxClients">Number max oc clients in this world</param>
        public NetSquareWorld(NetSquare_Server _server, ushort id, string name = "", ushort maxClients = 128, bool useSpatializer = false, float spatializerMaxDistance = 100f, int spatializerFrequency = 2)
        {
            if (string.IsNullOrEmpty(name))
                name = "World " + id;
            ID = id;
            MaxClientsInWorld = maxClients;
            Clients = new HashSet<uint>();
            server = _server;
            if (useSpatializer)
                StartUsingSpatializer(spatializerMaxDistance, spatializerFrequency);
        }

        /// <summary>
        /// synchronization will now use spatialization for better sync performances (use it on large worlds)
        /// </summary>
        /// <param name="maxDistance">maximum player view distance (Join and Leave events will be sended whene player enter or leave the maxDistance area)</param>
        public void StartUsingSpatializer(float maxDistance, int frequency)
        {
            Spatializer = new Spatializer(this);
            Spatializer.StartSpatializer(frequency, maxDistance);
            UseSpatializer = true;
            SpatializerMaxDistance = maxDistance;
        }

        /// <summary>
        /// synchronization will no longer use spatialization. Disable spatialization for small or screen-space worlds
        /// </summary>
        public void StopUsingSpatializer()
        {
            Spatializer?.StopSpatializer();
            Spatializer = null;
            UseSpatializer = false;
            SpatializerMaxDistance = -1f;
        }

        /// <summary>
        /// Set client position on spatializer
        /// </summary>
        /// <param name="clientID">ID of the client</param>
        /// <param name="position">client position</param>
        public void SetClientPosition(uint clientID, Position position)
        {
            if (UseSpatializer)
                Spatializer.SetClientPosition(clientID, position);
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
            if (Clients.Remove(clientID))
            {

                Spatializer?.RemoveClient(clientID);
                return true;
            }
            return false;
        }

        #region Broadcast
        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        public void Broadcast(NetworkMessage message)
        {
            server.SendToClients(message, new HashSet<uint>(Clients));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        public void Broadcast(byte[] message)
        {
            server.SendToClients(message, new HashSet<uint>(Clients));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        public void BroadcastUDP(NetworkMessage message)
        {
            server.SendToClientsUDP(message, new HashSet<uint>(Clients));
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        public void BroadcastUDP(ushort headID, byte[] message)
        {
            server.SendToClientsUDP(headID, message, new HashSet<uint>(Clients));
        }
        /// <summary>
        /// Send message to visible clients in this world
        /// </summary>
        /// <param name="message">message to send</param>
        public void BroadcastVisible(NetworkMessage message)
        {
            server.SendToClients(message, Spatializer.GetVisibleClients(message.ClientID.UInt32));
        }

        /// <summary>
        /// Send message to visible clients in this world
        /// </summary>
        /// <param name="message">message to send</param>
        /// <param name="clientID">clientID of the sender</param>
        public void BroadcastVisible(byte[] message, uint clientID)
        {
            server.SendToClients(message, Spatializer.GetVisibleClients(clientID));
        }

        /// <summary>
        /// Send message to visible clients in this world
        /// </summary>
        /// <param name="message">message to send</param>
        public void BroadcastUDPVisible(NetworkMessage message)
        {
            server.SendToClientsUDP(message, Spatializer.GetVisibleClients(message.ClientID.UInt32));
        }

        /// <summary>
        /// Send message to visible clients in this world
        /// </summary>
        /// <param name="headID">head ID of the message</param>
        /// <param name="message">message to send</param>
        /// <param name="clientID">clientID of the sender</param>
        public void BroadcastUDPVisible(ushort headID, byte[] message, uint clientID)
        {
            server.SendToClientsUDP(headID, message, Spatializer.GetVisibleClients(clientID));
        }
        #endregion

        #region Send
        /// <summary>
        /// Send a message to specific client according to clientID
        /// </summary>
        /// <param name="clientID">ID of the client to send message</param>
        /// <param name="message">message to send</param>
        public void SendToClient(uint clientID, NetworkMessage message)
        {
            server.SendToClient(message, clientID);
        }

        /// <summary>
        /// Send a message to specific client according to clientID
        /// </summary>
        /// <param name="clientID">ID of the client to send message</param>
        /// <param name="message">message to send</param>
        public void SendToClient(uint clientID, byte[] message)
        {
            server.SendToClient(message, clientID);
        }

        /// <summary>
        /// Send a message to specific client according to clientID
        /// </summary>
        /// <param name="clientID">ID of the client to send message</param>
        /// <param name="message">message to send</param>
        public void SendToClientUDP(uint clientID, NetworkMessage message)
        {
            server.SendToClientUDP(message, clientID);
        }

        /// <summary>
        /// Send a message to specific client according to clientID
        /// </summary>
        /// <param name="clientID">ID of the client to send message</param>
        /// <param name="headID">headID of the message</param>
        /// <param name="message">message to send</param>
        public void SendToClientUDP(uint clientID, ushort headID, byte[] message)
        {
            server.SendToClientUDP(headID, message, clientID);
        }
        #endregion
    }
}