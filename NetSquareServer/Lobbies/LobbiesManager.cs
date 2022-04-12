using NetSquare.Core;
using NetSquareServer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetSquareServer.Lobbies
{
    public static class LobbiesManager
    {
        public static Dictionary<ushort, NetSquareLobby> Lobbies = new Dictionary<ushort, NetSquareLobby>(); // lobbyID => Lobby object
        private static Dictionary<uint, ushort> ClientsLobbies = new Dictionary<uint, ushort>(); // clientID => lobbyID

        static LobbiesManager()
        {
            NetSquare_Server.Instance.OnClientDisconnected += Instance_OnClientDisconnected;
        }

        /// <summary>
        /// Raised when a client disconnect the server. If the client is in a lobby, must tell anyone in the lobby
        /// </summary>
        /// <param name="clientID">ID of the disconnected client</param>
        private static void Instance_OnClientDisconnected(uint clientID)
        {
            TryRemoveClientFromLobby(new NetworkMessage() { ClientID = clientID });
        }

        /// <summary>
        /// Add a lobby
        /// </summary>
        /// <param name="name">Name of the lobby to add</param>
        /// <param name="nbMaxClients">Maximum clients that can join this lobby</param>
        /// <returns>ID of the lobby</returns>
        public static ushort AddLobby(string name = "", ushort nbMaxClients = 128)
        {
            ushort id = (Lobbies.Count == 0) ? (ushort)0 : Lobbies.Keys.Max<ushort>();
            id++;
            Lobbies.Add(id, new NetSquareLobby(id, name, nbMaxClients));
            Writer.Write("Lobby " + id + " added", ConsoleColor.Green);
            return id;
        }

        /// <summary>
        /// get a lobby by ID if exists
        /// </summary>
        /// <param name="id">ID of the lobby to get</param>
        /// <returns>Lobby object if exists</returns>
        public static NetSquareLobby GetLobby(ushort id)
        {
            if (Lobbies.ContainsKey(id))
                return Lobbies[id];
            return null;
        }

        /// <summary>
        /// Is the client in some lobby
        /// </summary>
        /// <param name="clientID">Id of the client to check</param>
        /// <returns>true if in some lobby</returns>
        public static bool IsClientInLobby(uint clientID)
        {
            return ClientsLobbies.ContainsKey(clientID);
        }

        /// <summary>
        /// Get the lobbyID witch a client is in
        /// </summary>
        /// <param name="clientID">ID of the client</param>
        /// <returns>ID of th lobby, or 0 if none. Check before with 'IsClientinLobby()'</returns>
        public static ushort GetClientLobby(uint clientID)
        {
            if (IsClientInLobby(clientID))
                return ClientsLobbies[clientID];
            return 0;
        }

        #region Network Messages
        /// <summary>
        /// message from client for joining a lobby
        /// </summary>
        /// <param name="message">message must contain lobby ID</param>
        [NetSquareAction(65535)]
        public static void TryAddClientToLobby(NetworkMessage message)
        {
            try
            {
                // get lobby ID
                ushort lobbyID = message.GetUShort();
                // get lobby instance
                NetSquareLobby lobby = GetLobby(lobbyID);
                // throw new exception if lobby don't exists
                if (lobby == null)
                    throw new Exception("Lobby " + lobbyID + " don't exists");
                // lobby exit so let's try add client into it
                bool added = lobby.TryJoinLobby(message.ClientID);
                // reply to client the added state
                NetSquare_Server.Instance.Reply(message, new NetworkMessage().Set(added));
                // add clientID / lobbyID mapping
                if (added)
                {
                    ClientsLobbies.Remove(message.ClientID);
                    ClientsLobbies.Add(message.ClientID, lobbyID);
                    lobby.Broadcast(new NetworkMessage(65532).Set(message.ClientID));
                    Writer.Write("Client " + message.ClientID + " join lobby " + lobbyID, ConsoleColor.Gray);
                }
            }
            catch (Exception ex)
            {
                // reply to the client. Reply false because client was not added to lobby
                NetSquare_Server.Instance.Reply(message, new NetworkMessage().Set(false));
                Writer.Write("Fail to join Lobby : client " + message.ClientID + Environment.NewLine + ex.ToString(), ConsoleColor.Red);
            }
        }

        /// <summary>
        /// broadcast message from client to any client in the same lobby
        /// </summary>
        /// <param name="message">message we want to broadcast</param>
        public static void BroadcastToLobby(NetworkMessage message)
        {
            if (IsClientInLobby(message.ClientID))
            {
                NetSquareLobby lobby = GetLobby(GetClientLobby(message.ClientID));
                if (lobby != null)
                    lobby.Broadcast(message);
            }
        }

        /// <summary>
        /// message from client for leaving current lobby
        /// </summary>
        /// <param name="message">empty message</param>
        [NetSquareAction(65534)]
        public static void TryRemoveClientFromLobby(NetworkMessage message)
        {
            try
            {
                bool leave = false;
                if (IsClientInLobby(message.ClientID))
                {
                    ushort lobbyID = GetClientLobby(message.ClientID);
                    NetSquareLobby lobby = GetLobby(lobbyID);
                    if (lobby != null)
                    {
                        // lobby exit so let's try add client into it
                        leave = lobby.TryLeaveLobby(message.ClientID);
                        ClientsLobbies.Remove(message.ClientID);
                        // remove clientID / lobbyID apping
                        if (leave)
                        {
                            Writer.Write("Client " + message.ClientID + " leave lobby " + lobbyID, ConsoleColor.Gray);
                            // tell anyone in this lobby that a client just leave the lobby
                            lobby.Broadcast(new NetworkMessage(65531).Set(message.ClientID));
                        }
                    }
                    // reply to client the added state
                    if (message.Client != null)
                        NetSquare_Server.Instance.Reply(message, new NetworkMessage().Set(leave));
                }
            }
            catch (Exception ex)
            {
                // reply to the client. Reply false because client was not added to lobby
                NetSquare_Server.Instance.Reply(message, new NetworkMessage().Set(false));
                Writer.Write("Fail to leave Lobby : client " + message.ClientID + Environment.NewLine + ex.ToString(), ConsoleColor.Red);
            }
        }
        #endregion
    }
}