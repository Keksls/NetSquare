using NetSquare.Core;
using System;
using System.Collections.Generic;

namespace NetSquareServer.Lobbies
{
    public class NetSquareLobby
    {
        public event Action<uint> OnClientJoinLobby;
        public ushort ID { get; private set; }
        public HashSet<uint> Clients { get; private set; }
        public ushort MaxClientsInLobby { get; private set; }
        public string Name { get; private set; }

        /// <summary>
        /// instantiate a new Lobby
        /// </summary>
        /// <param name="id">ID of the lobby (must be unique)</param>
        /// <param name="name">Name of the Lobby</param>
        /// <param name="maxClients">Number max oc clients in this lobby</param>
        public NetSquareLobby(ushort id, string name = "", ushort maxClients = 128)
        {
            if (string.IsNullOrEmpty(name))
                name = "Lobby " + id;
            ID = id;
            MaxClientsInLobby = maxClients;
            Clients = new HashSet<uint>();
        }

        /// <summary>
        /// Try to add a client to this lobby. Can fail if lobby is full or client already is in this lobby
        /// </summary>
        /// <param name="clientID">id of the client to add</param>
        /// <returns>true if success</returns>
        public bool TryJoinLobby(uint clientID)
        {
            if (Clients.Contains(clientID) || Clients.Count >= MaxClientsInLobby)
                return false;
            Clients.Add(clientID);
            OnClientJoinLobby?.Invoke(clientID);
            return true;
        }

        /// <summary>
        /// Try to remove a client from this lobby. Can fail if client not is in this lobby
        /// </summary>
        /// <param name="clientID">id of the client to remove</param>
        /// <returns>true if success</returns>
        public bool TryLeaveLobby(uint clientID)
        {
            return Clients.Remove(clientID);
        }

        /// <summary>
        /// Send message to anyone in this lobby
        /// </summary>
        /// <param name="message">message to send</param>
        public void Broadcast(NetworkMessage message)
        {
            NetSquare_Server.Instance.SendToClients(message, new HashSet<uint>(Clients));
        }
    }
}