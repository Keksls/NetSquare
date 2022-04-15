using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareServer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetSquareServer.Lobbies
{
    public class WorldsManager
    {
        public Dictionary<ushort, NetSquareWorld> Worlds = new Dictionary<ushort, NetSquareWorld>(); // worldID => World object
        /// <summary>
        /// WorldID, ClientID, Message to broadcast
        /// </summary>
        public event Action<ushort, uint, NetworkMessage> OnClientJoinWorld;
        private Dictionary<uint, ushort> ClientsLobbies = new Dictionary<uint, ushort>(); // clientID => worldID
        private NetSquare_Server server;

        public WorldsManager(NetSquare_Server _server)
        {
            server = _server;
            server.Dispatcher.AddHeadAction(NetSquareMessageType.ClientJoinWorld, "ClientJoinWorld", TryAddClientToWorld);
            server.Dispatcher.AddHeadAction(NetSquareMessageType.ClientLeaveWorld, "ClientLeaveWorld", TryRemoveClientFromWorld);
            server.OnClientDisconnected += Instance_OnClientDisconnected;
        }

        /// <summary>
        /// Raised when a client disconnect the server. If the client is in a world, must tell anyone in the world
        /// </summary>
        /// <param name="clientID">ID of the disconnected client</param>
        private void Instance_OnClientDisconnected(uint clientID)
        {
            TryRemoveClientFromWorld(new NetworkMessage() { ClientID = clientID });
        }

        /// <summary>
        /// Add a world
        /// </summary>
        /// <param name="name">Name of the world to add</param>
        /// <param name="nbMaxClients">Maximum clients that can join this world</param>
        /// <returns>ID of the world</returns>
        public ushort AddWorld(string name = "", ushort nbMaxClients = 128)
        {
            ushort id = (Worlds.Count == 0) ? (ushort)0 : Worlds.Keys.Max<ushort>();
            id++;
            Worlds.Add(id, new NetSquareWorld(server, id, name, nbMaxClients));
            Writer.Write("World " + id + " added", ConsoleColor.Green);
            return id;
        }

        /// <summary>
        /// get a world by ID if exists
        /// </summary>
        /// <param name="id">ID of the world to get</param>
        /// <returns>World object if exists</returns>
        public NetSquareWorld GetWorld(ushort id)
        {
            if (Worlds.ContainsKey(id))
                return Worlds[id];
            return null;
        }

        /// <summary>
        /// Is the client in some world
        /// </summary>
        /// <param name="clientID">Id of the client to check</param>
        /// <returns>true if in some world</returns>
        public bool IsClientInWorld(uint clientID)
        {
            return ClientsLobbies.ContainsKey(clientID);
        }

        /// <summary>
        /// Get the worldID witch a client is in
        /// </summary>
        /// <param name="clientID">ID of the client</param>
        /// <returns>ID of th world, or 0 if none. Check before with 'IsClientinWorld()'</returns>
        public ushort GetClientWorld(uint clientID)
        {
            if (IsClientInWorld(clientID))
                return ClientsLobbies[clientID];
            return 0;
        }

        #region Network Messages
        /// <summary>
        /// message from client for joining a world
        /// </summary>
        /// <param name="message">message must contain world ID</param>
        public void TryAddClientToWorld(NetworkMessage message)
        {
            try
            {
                // get world ID
                ushort worldID = message.GetUShort();
                // get world instance
                NetSquareWorld world = GetWorld(worldID);
                // throw new exception if world don't exists
                if (world == null)
                    throw new Exception("World " + worldID + " don't exists");
                // world exit so let's try add client into it
                bool added = world.TryJoinWorld(message.ClientID);
                // reply to client the added state
                server.Reply(message, new NetworkMessage().Set(added));
                // add clientID / worldID mapping
                if (added)
                {
                    ClientsLobbies.Remove(message.ClientID);
                    ClientsLobbies.Add(message.ClientID, worldID);
                    NetworkMessage joinMessage = new NetworkMessage(NetSquareMessageType.ClientJoinWorld, message.ClientID);
                    OnClientJoinWorld?.Invoke(worldID, message.ClientID, joinMessage);
                    world.Broadcast(joinMessage);
                    Writer.Write("Client " + joinMessage.ClientID + " join world " + worldID, ConsoleColor.Gray);
                }
            }
            catch (Exception ex)
            {
                // reply to the client. Reply false because client was not added to world
                server.Reply(message, new NetworkMessage().Set(false));
                Writer.Write("Fail to join World : client " + message.ClientID + Environment.NewLine + ex.ToString(), ConsoleColor.Red);
            }
        }

        /// <summary>
        /// broadcast message from client to any client in the same world
        /// </summary>
        /// <param name="message">message we want to broadcast</param>
        public void BroadcastToWorld(NetworkMessage message)
        {
            if (IsClientInWorld(message.ClientID))
            {
                NetSquareWorld world = GetWorld(GetClientWorld(message.ClientID));
                if (world != null)
                    world.Broadcast(message);
            }
        }

        /// <summary>
        /// message from client for leaving current world
        /// </summary>
        /// <param name="message">empty message</param>
        public void TryRemoveClientFromWorld(NetworkMessage message)
        {
            try
            {
                bool leave = false;
                if (IsClientInWorld(message.ClientID))
                {
                    ushort worldID = GetClientWorld(message.ClientID);
                    NetSquareWorld world = GetWorld(worldID);
                    if (world != null)
                    {
                        // world exit so let's try add client into it
                        leave = world.TryLeaveWorld(message.ClientID);
                        ClientsLobbies.Remove(message.ClientID);
                        // remove clientID / worldID apping
                        if (leave)
                        {
                            Writer.Write("Client " + message.ClientID + " leave world " + worldID, ConsoleColor.Gray);
                            // tell anyone in this world that a client just leave the world
                            world.Broadcast(new NetworkMessage(NetSquareMessageType.ClientLeaveWorld).Set(message.ClientID));
                        }
                    }
                    // reply to client the added state
                    if (message.Client != null)
                        server.Reply(message, new NetworkMessage().Set(leave));
                }
            }
            catch (Exception ex)
            {
                // reply to the client. Reply false because client was not added to world
                server.Reply(message, new NetworkMessage().Set(false));
                Writer.Write("Fail to leave World : client " + message.ClientID + Environment.NewLine + ex.ToString(), ConsoleColor.Red);
            }
        }
        #endregion
    }
}