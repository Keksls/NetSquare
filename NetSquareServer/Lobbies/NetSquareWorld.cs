using NetSquare.Core;
using System;
using System.Collections.Generic;

namespace NetSquareServer.Lobbies
{
    public class NetSquareWorld
    {
        public event Action<uint> OnClientJoinWorld;
        public ushort ID { get; private set; }
        public HashSet<uint> Clients { get; private set; }
        public ushort MaxClientsInWorld { get; private set; }
        public string Name { get; private set; }
        private NetSquare_Server server;

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
            return Clients.Remove(clientID);
        }

        /// <summary>
        /// Send message to anyone in this world
        /// </summary>
        /// <param name="message">message to send</param>
        public void Broadcast(NetworkMessage message)
        {
            server.SendToClients(message, new HashSet<uint>(Clients));
        }
    }
}