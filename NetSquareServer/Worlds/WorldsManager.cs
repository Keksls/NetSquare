using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using NetSquareServer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetSquareServer.Worlds
{
    public class WorldsManager
    {
        public Dictionary<ushort, NetSquareWorld> Worlds = new Dictionary<ushort, NetSquareWorld>(); // worldID => World object
        /// <summary>
        /// WorldID, ClientID, Message to broadcast
        /// </summary>
        public event Action<ushort, uint, NetworkMessage> OnClientJoinWorld;
        /// <summary>
        /// WorldID, ClientID, Message to broadcast
        /// </summary>
        public event Action<ushort, uint, NetworkMessage> OnSendWorldClients;
        private Dictionary<uint, ushort> ClientsWorlds = new Dictionary<uint, ushort>(); // clientID => worldID
        private NetSquare_Server server;

        public WorldsManager(NetSquare_Server _server)
        {
            server = _server;
            server.Dispatcher.AddHeadAction(NetSquareMessageType.ClientJoinWorld, "ClientJoinWorld", TryAddClientToWorld);
            server.Dispatcher.AddHeadAction(NetSquareMessageType.ClientLeaveWorld, "ClientLeaveWorld", TryRemoveClientFromWorld);
            server.Dispatcher.AddHeadAction(NetSquareMessageType.ClientSetPosition, "ClientSetPosition", ClientSetPosition);
        }

        internal void Fire_OnSendWorldClients(ushort worldID, uint clientID, NetworkMessage message)
        {
            OnClientJoinWorld?.Invoke(worldID, clientID, message);
        }

        /// <summary>
        /// Add a world
        /// </summary>
        /// <param name="name">Name of the world to add</param>
        /// <param name="nbMaxClients">Maximum clients that can join this world</param>
        /// <returns>ID of the world</returns>
        public NetSquareWorld AddWorld(string name = "", ushort nbMaxClients = 128)
        {
            ushort id = (Worlds.Count == 0) ? (ushort)0 : Worlds.Keys.Max<ushort>();
            id++;
            NetSquareWorld world = new NetSquareWorld(server, id, name, nbMaxClients);
            Worlds.Add(id, world);
            Writer.Write("World " + id + " added", ConsoleColor.Green);
            return world;
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
        /// A client just deconnected from server
        /// </summary>
        /// <param name="clientID">ID of disconnected client</param>
        public void ClientDisconnected(UInt24 clientID)
        {
            if (IsClientInWorld(clientID.UInt32))
            {
                NetSquareWorld world = GetWorld(GetClientWorld(clientID.UInt32));
                world.Synchronizer.RemoveMessagesFromClient(clientID);
                world.Spatializer.RemoveClient(clientID.UInt32);
                bool leave = world.TryLeaveWorld(clientID.UInt32);
                ClientsWorlds.Remove(clientID.UInt32);
                // tell anyone in this world that a client just leave the world
                world.Broadcast(new NetworkMessage(NetSquareMessageType.ClientLeaveWorld).Set(clientID));
            }
        }

        /// <summary>
        /// Is the client in some world
        /// </summary>
        /// <param name="clientID">Id of the client to check</param>
        /// <returns>true if in some world</returns>
        public bool IsClientInWorld(uint clientID)
        {
            return ClientsWorlds.ContainsKey(clientID);
        }

        /// <summary>
        /// Get the worldID witch a client is in
        /// </summary>
        /// <param name="clientID">ID of the client</param>
        /// <returns>ID of th world, or 0 if none. Check before with 'IsClientinWorld()'</returns>
        public ushort GetClientWorld(uint clientID)
        {
            if (IsClientInWorld(clientID))
                return ClientsWorlds[clientID];
            return 0;
        }

        #region Network Messages
        /// <summary>
        /// server juste receive a synchronization message from a client, we have to dispatch it into the right world synchronizer
        /// </summary>
        /// <param name="message">message received from client</param>
        public void ReceiveSyncronizationMessage(NetworkMessage message)
        {
            if (ClientsWorlds.ContainsKey(message.ClientID.UInt32))
                Worlds[ClientsWorlds[message.ClientID.UInt32]].Synchronizer.AddMessage(message);
        }

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
                bool added = world.TryJoinWorld(message.ClientID.UInt32);
                // reply to client the added state
                server.Reply(message, new NetworkMessage().Set(added));
                // add clientID / worldID mapping
                if (added)
                {
                    ClientsWorlds.Remove(message.ClientID.UInt32);
                    ClientsWorlds.Add(message.ClientID.UInt32, worldID);
                    Writer.Write("Client " + message.ClientID + " join world " + worldID, ConsoleColor.Gray);

                    // send already connected clients to new client
                    if (!world.UseSpatializer) // if spatializer is used, it will handle this event, so let's do nothing here
                    {
                        NetworkMessage joinMessage = new NetworkMessage(NetSquareMessageType.ClientJoinWorld, message.ClientID);
                        OnClientJoinWorld?.Invoke(worldID, message.ClientID.UInt32, joinMessage);
                        world.Broadcast(joinMessage);
                        HashSet<uint> clients = new HashSet<uint>(world.Clients);
                        foreach (var clientID in clients)
                        {
                            if (clientID == message.ClientID.UInt32)
                                continue;
                            NetworkMessage connectedClientMessage = new NetworkMessage(NetSquareMessageType.ClientJoinWorld, clientID);
                            OnSendWorldClients?.Invoke(worldID, clientID, connectedClientMessage);
                            message.Client.AddTCPMessage(connectedClientMessage);
                        }
                    }
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
            if (IsClientInWorld(message.ClientID.UInt32))
            {
                NetSquareWorld world = GetWorld(GetClientWorld(message.ClientID.UInt32));
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
                if (IsClientInWorld(message.ClientID.UInt32))
                {
                    ushort worldID = GetClientWorld(message.ClientID.UInt32);
                    NetSquareWorld world = GetWorld(worldID);
                    if (world != null)
                    {
                        // world exit so let's try add client into it
                        leave = world.TryLeaveWorld(message.ClientID.UInt32);
                        ClientsWorlds.Remove(message.ClientID.UInt32);
                        // remove clientID / worldID apping
                        if (leave)
                        {
                            Writer.Write("Client " + message.ClientID + " leave world " + worldID, ConsoleColor.Gray);
                            if (!world.UseSpatializer) // if spatializer is used, it will handle this event, so let's do nothing here
                            {
                                // tell anyone in this world that a client just leave the world
                                world.Broadcast(new NetworkMessage(NetSquareMessageType.ClientLeaveWorld).Set(message.ClientID));
                            }
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

        /// <summary>
        /// A client just set his possition to the server
        /// </summary>
        /// <param name="message">message that contains 3 floats : x, y and z that represent his 3d position in the current World</param>
        public void ClientSetPosition(NetworkMessage message)
        {
            try
            {
                if (IsClientInWorld(message.ClientID.UInt32))
                {
                    NetSquareWorld world = GetWorld(GetClientWorld(message.ClientID.UInt32));
                    if (world != null)
                    {
                        world.SetClientPosition(message.ClientID.UInt32, new Position(message.GetFloat(), message.GetFloat(), message.GetFloat()));
                        message.RestartRead();
                        ReceiveSyncronizationMessage(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Writer.Write("Fail to set client position : \n\r" + ex.Message, ConsoleColor.Red);
            }
        }
        #endregion
    }
}