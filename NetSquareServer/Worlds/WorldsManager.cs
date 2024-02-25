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
        /// WorldID, ClientID, Message to broadcast. Send new client data to already conneced clients
        /// </summary>
        public event Action<ushort, uint, NetworkMessage> OnClientJoinWorld;
        /// <summary>
        /// WorldID, ClientID, Message to broadcast. Send connected clients to new client
        /// </summary>
        public event Action<ushort, uint, NetworkMessage> OnSendWorldClients;
        /// <summary>
        /// WorldID, ClientID, client Position
        /// </summary>
        public event Action<ushort, uint, NetsquareTransformFrame> OnSpatializePlayer;
        private Dictionary<uint, ushort> ClientsWorlds = new Dictionary<uint, ushort>(); // clientID => worldID
        private NetSquare_Server server;

        public WorldsManager(NetSquare_Server _server)
        {
            server = _server;
            server.Dispatcher.AddHeadAction(NetSquareMessageType.ClientJoinWorld, "ClientJoinWorld", TryAddClientToWorld);
            server.Dispatcher.AddHeadAction(NetSquareMessageType.ClientLeaveWorld, "ClientLeaveWorld", TryRemoveClientFromWorld);
            server.Dispatcher.AddHeadAction(NetSquareMessageType.SetTransform, "SetTransform", SetTransform);
            server.Dispatcher.AddHeadAction(NetSquareMessageType.SetTransformFrames, "SetTransformFrames", SetTransformFrames);
        }

        internal void Fire_OnSendWorldClients(ushort worldID, uint clientID, NetworkMessage message)
        {
            OnSendWorldClients?.Invoke(worldID, clientID, message);
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
            return AddWorld(id, name, nbMaxClients);
        }

        /// <summary>
        /// Add a world
        /// </summary>
        /// <param name="name">Name of the world to add</param>
        /// <param name="nbMaxClients">Maximum clients that can join this world</param>
        /// <returns>ID of the world</returns>
        public NetSquareWorld AddWorld(ushort id, string name = "", ushort nbMaxClients = 128)
        {
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
        public void ClientDisconnected(uint clientID)
        {
            if (IsInWorld(clientID))
            {
                NetSquareWorld world = GetWorld(GetClientWorldID(clientID));
                // tell anyone in this world that a client just leave the world
                world.Broadcast(new NetworkMessage(NetSquareMessageType.ClientLeaveWorld, clientID).Set(clientID));
                world.Synchronizer?.RemoveMessagesFromClient(clientID);
                world.TryLeaveWorld(clientID);
                ClientsWorlds.Remove(clientID);
            }
        }

        /// <summary>
        /// Is the client in some world
        /// </summary>
        /// <param name="clientID">Id of the client to check</param>
        /// <returns>true if in some world</returns>
        public bool IsInWorld(uint clientID)
        {
            return ClientsWorlds.ContainsKey(clientID);
        }

        /// <summary>
        /// Is the client in some world
        /// </summary>
        /// <param name="clientID">Id of the client to check</param>
        /// <returns>true if in some world</returns>
        public bool IsInWorld(UInt24 clientID)
        {
            return IsInWorld(clientID.UInt32);
        }

        /// <summary>
        /// Get the worldID witch a client is in
        /// </summary>
        /// <param name="clientID">ID of the client</param>
        /// <returns>ID of the world, or 0 if none. Check before with 'IsClientInWorld()'</returns>
        public ushort GetClientWorldID(uint clientID)
        {
            if (IsInWorld(clientID))
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
            if (ClientsWorlds.ContainsKey(message.ClientID))
                Worlds[ClientsWorlds[message.ClientID]].Synchronizer?.AddMessage(message);
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
                bool added = world.TryJoinWorld(message.ClientID);
                // reply to client the added state
                NetworkMessage reply = new NetworkMessage().Set(added);
                server.PrepareReply(message, reply);

                // add clientID / worldID mapping
                if (added)
                {
                    ClientsWorlds.Remove(message.ClientID);
                    ClientsWorlds.Add(message.ClientID, worldID);
                    Writer.Write("Client " + message.ClientID + " join world " + worldID, ConsoleColor.Gray);

                    // send already connected clients to new client
                    if (!world.UseSpatializer) // if spatializer is used, it will handle this event, so let's do nothing here
                    {
                        // send new client to connected clients but the new
                        NetworkMessage joinMessage = new NetworkMessage(NetSquareMessageType.ClientJoinWorld, message.ClientID);
                        OnClientJoinWorld?.Invoke(worldID, message.ClientID, joinMessage);
                        world.Broadcast(joinMessage, message.ClientID, true);

                        // send connected clients to new client but him
                        HashSet<uint> clients = new HashSet<uint>(world.Clients);
                        List<NetworkMessage> messages = new List<NetworkMessage>();
                        foreach (var clientID in clients)
                        {
                            if (clientID == message.ClientID)
                                continue;

                            NetworkMessage connectedClientMessage = new NetworkMessage(NetSquareMessageType.ClientJoinWorld, clientID);
                            OnSendWorldClients?.Invoke(worldID, clientID, connectedClientMessage);
                            messages.Add(connectedClientMessage);
                        }
                        reply.Pack(messages);
                    }
                }

                server.Reply(message, reply);
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
        /// <param name="useSpatialization">if this client's world use spatialization, broadcast to anyone visible only</param>
        public void BroadcastToWorld(NetworkMessage message, bool useSpatialization = true)
        {
            if (IsInWorld(message.ClientID))
            {
                NetSquareWorld world = GetWorld(GetClientWorldID(message.ClientID));
                if (world != null)
                    world.Broadcast(message, useSpatialization);
            }
        }

        /// <summary>
        /// broadcast message from client to any client in the same world
        /// </summary>
        /// <param name="message">message we want to broadcast</param>
        /// <param name="useSpatialization">if this client's world use spatialization, broadcast to anyone visible only</param>
        public void BroadcastToWorld(byte[] message, uint clientID, bool useSpatialization = true)
        {
            if (IsInWorld(clientID))
            {
                NetSquareWorld world = GetWorld(GetClientWorldID(clientID));
                if (world != null)
                    world.Broadcast(message, clientID, useSpatialization);
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
                if (IsInWorld(message.ClientID))
                {
                    ushort worldID = GetClientWorldID(message.ClientID);
                    NetSquareWorld world = GetWorld(worldID);
                    if (world != null)
                    {
                        // world exit so let's try add client into it
                        leave = world.TryLeaveWorld(message.ClientID);
                        ClientsWorlds.Remove(message.ClientID);
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
        /// <param name="message">message that contains a transform frames</param>
        private void SetTransform(NetworkMessage message)
        {
            try
            {
                if (IsInWorld(message.ClientID))
                {
                    NetSquareWorld world = GetWorld(GetClientWorldID(message.ClientID));
                    if (world != null)
                    {
                        NetsquareTransformFrame transform = new NetsquareTransformFrame(message);
                        // if we use a spatializer, we store the frame into it so it can be used for spatialization and send to visible clients later as packed message
                        if (world.UseSpatializer)
                        {
                            world.Spatializer.StoreClientTransformFrame(message.ClientID, transform);
                        }
                        // if we don't use a spatializer, we send the new position directly to everyone in the world
                        else
                        {
                            world.Broadcast(message.Data, message.ClientID, true);
                        }
                        message.RestartRead();
                    }
                }
            }
            catch (Exception ex)
            {
                Writer.Write("Fail to set client position : \n\r" + ex.Message, ConsoleColor.Red);
            }
        }

        /// <summary>
        /// A client just set his possition to the server
        /// </summary>
        /// <param name="message">message that contains multiple transform frames</param>
        private unsafe void SetTransformFrames(NetworkMessage message)
        {
            try
            {
                if (IsInWorld(message.ClientID))
                {
                    NetSquareWorld world = GetWorld(GetClientWorldID(message.ClientID));
                    if (world != null)
                    {
                        // if we use a spatializer, we store the frame into it so it can be used for spatialization and send to visible clients later as packed message
                        if (world.UseSpatializer)
                        {
                            fixed (byte* ptr = message.Data)
                            {
                                byte* b = ptr + message.currentReadingIndex;
                                ushort nbFrames = *(ushort*)(b);
                                b += 2;
                                NetsquareTransformFrame[] frames = new NetsquareTransformFrame[nbFrames];
                                for (ushort i = 0; i < nbFrames; i++)
                                {
                                    frames[i].Deserialize(ref b);
                                }
                                world.Spatializer.StoreClientTransformFrames(message.ClientID, frames);
                            }
                        }
                        // if we don't use a spatializer, we send the new position directly to everyone in the world
                        else
                        {
                            world.Broadcast(message.Data, message.ClientID, true);
                        }
                        message.RestartRead();
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