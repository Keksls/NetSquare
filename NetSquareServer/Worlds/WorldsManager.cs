using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquare.Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the worlds manager component.
    /// </summary>
    public class WorldsManager
    {
        /// <summary>
        /// Gets or sets the worlds value.
        /// </summary>
        public ConcurrentDictionary<ushort, NetSquareWorld> Worlds = new ConcurrentDictionary<ushort, NetSquareWorld>(); // worldID => World object
        /// <summary>
        /// WorldID, ClientID, Transform of the client, Message to broadcast. Send new client data to already conneced clients
        /// </summary>
        public event Action<ushort, uint, NetsquareTransformFrame, NetworkMessage> OnClientJoinWorld;
        /// <summary>
        /// WorldID, ClientID, Message to broadcast. Send connected clients to new client
        /// </summary>
        public event Action<ushort, uint, NetworkMessage> OnSendWorldClients;
        /// <summary>
        /// ClientID, Transform of the client. Client just move
        /// </summary>
        public event Action<uint, NetsquareTransformFrame> OnClientMove;
        /// <summary>
        /// Gets or sets the clients worlds value.
        /// </summary>
        private ConcurrentDictionary<uint, ushort> ClientsWorlds = new ConcurrentDictionary<uint, ushort>(); // clientID => worldID
        /// <summary>
        /// Stores the world membership lock value.
        /// </summary>
        private readonly object worldMembershipLock = new object();
        /// <summary>
        /// Stores the next world id value.
        /// </summary>
        private int nextWorldId;
        /// <summary>
        /// Stores the server value.
        /// </summary>
        private NetSquareServer server;

        /// <summary>
        /// Initializes a new instance of the worlds manager class.
        /// </summary>
        public WorldsManager(NetSquareServer _server)
        {
            server = _server;
            server.Dispatcher.AddHeadAction(NetSquareMessageID.ClientJoinWorld, "ClientJoinWorld", TryAddClientToWorld);
            server.Dispatcher.AddHeadAction(NetSquareMessageID.ClientLeaveWorld, "ClientLeaveWorld", TryRemoveClientFromWorld);
            server.Dispatcher.AddHeadAction(NetSquareMessageID.SetSynchFrame, "SetSynchFrame", SetSynchFrame);
            server.Dispatcher.AddHeadAction(NetSquareMessageID.SetSynchFrames, "SetSynchFrames", SetSynchFrames);
        }

        /// <summary>
        /// A client just move in the world
        /// </summary>
        /// <param name="clientID"> ID of the client</param>
        /// <param name="transform"> New transform of the client</param>
        internal void Fire_OnClientMove(uint clientID, NetsquareTransformFrame transform)
        {
            OnClientMove?.Invoke(clientID, transform);
        }

        /// <summary>
        /// Send new client data to already conneced clients
        /// </summary>
        /// <param name="worldID"> Id of the world</param>
        /// <param name="clientID"> Id of the client</param>
        /// <param name="message"> Message to broadcast</param>
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
            int id = Interlocked.Increment(ref nextWorldId);
            if (id > ushort.MaxValue)
                throw new InvalidOperationException("No world ID is available.");

            return AddWorld((ushort)id, name, nbMaxClients);
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
            if (!Worlds.TryAdd(id, world))
                throw new InvalidOperationException("World " + id + " already exists.");

            TrackWorldId(id);
            Writer.Write("World " + id + " added", ConsoleColor.Green);
            return world;
        }

        /// <summary>
        /// Executes the track world id operation.
        /// </summary>
        private void TrackWorldId(ushort id)
        {
            int current;
            do
            {
                current = nextWorldId;
                if (id <= current)
                    return;
            }
            while (Interlocked.CompareExchange(ref nextWorldId, id, current) != current);
        }

        /// <summary>
        /// get a world by ID if exists
        /// </summary>
        /// <param name="id">ID of the world to get</param>
        /// <returns>World object if exists</returns>
        public NetSquareWorld GetWorld(ushort id)
        {
            NetSquareWorld world;
            return Worlds.TryGetValue(id, out world) ? world : null;
        }

        /// <summary>
        /// Executes the try get client world operation.
        /// </summary>
        private bool TryGetClientWorld(uint clientID, out ushort worldID, out NetSquareWorld world)
        {
            worldID = 0;
            world = null;
            if (!ClientsWorlds.TryGetValue(clientID, out worldID))
                return false;

            return Worlds.TryGetValue(worldID, out world) && world != null;
        }

        /// <summary>
        /// A client just deconnected from server
        /// </summary>
        /// <param name="clientID">ID of disconnected client</param>
        public void ClientDisconnected(uint clientID)
        {
            lock (worldMembershipLock)
            {
                ushort worldID;
                if (!ClientsWorlds.TryRemove(clientID, out worldID))
                    return;

                NetSquareWorld world = GetWorld(worldID);
                if (world == null)
                    return;

                // Tell visible clients before the leaver is removed from the spatializer.
                world.Broadcast(new NetworkMessage(NetSquareMessageID.ClientLeaveWorld, clientID).Set(clientID));
                world.Synchronizer?.RemoveMessagesFromClient(clientID);
                world.TryLeaveWorld(clientID);
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
            ushort worldID;
            return ClientsWorlds.TryGetValue(clientID, out worldID) ? worldID : (ushort)0;
        }

        #region Network Messages
        /// <summary>
        /// server juste receive a synchronization message from a client, we have to dispatch it into the right world synchronizer
        /// </summary>
        /// <param name="message">message received from client</param>
        public void ReceiveSyncronizationMessage(NetworkMessage message)
        {
            ushort worldID;
            NetSquareWorld world;
            if (TryGetClientWorld(message.ClientID, out worldID, out world))
                world.Synchronizer?.AddMessage(message);
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
                ushort worldID = message.Serializer.GetUShort();
                NetsquareTransformFrame clientTransform = new NetsquareTransformFrame(message);
                // get world instance
                NetSquareWorld world = GetWorld(worldID);
                if (world == null)
                {
                    Writer.Write("World " + worldID + " don't exists", ConsoleColor.Red);
                    server.Reply(message, new NetworkMessage().Set(false));
                    return;
                }

                bool added = false;
                lock (worldMembershipLock)
                {
                    if (!ClientsWorlds.ContainsKey(message.ClientID))
                    {
                        added = world.TryJoinWorld(message.ClientID, clientTransform);
                        if (added && !ClientsWorlds.TryAdd(message.ClientID, worldID))
                        {
                            world.TryLeaveWorld(message.ClientID);
                            added = false;
                        }
                    }
                }

                // reply to client the added state
                NetworkMessage reply = new NetworkMessage().Set(added);
                server.PrepareReply(message, reply);

                if (added)
                {
                    Writer.Write("Client " + message.ClientID + " join world " + worldID + " at pos : " + clientTransform.x + ", " + clientTransform.y + ", " + clientTransform.z, ConsoleColor.Gray);

                    // send already connected clients to new client
                    if (!world.UseSpatializer) // if spatializer is used, it will handle this event, so let's do nothing here
                    {
                        // send new client to connected clients but the new
                        NetworkMessage joinMessage = new NetworkMessage(NetSquareMessageID.ClientJoinWorld, message.ClientID);
                        clientTransform.Serialize(joinMessage);
                        OnClientJoinWorld?.Invoke(worldID, message.ClientID, clientTransform, joinMessage);
                        world.Broadcast(joinMessage, message.ClientID, true);

                        // send connected clients to new client but him
                        List<NetworkMessage> messages = new List<NetworkMessage>();
                        foreach (var client in world.Clients)
                        {
                            if (client.Key == message.ClientID)
                                continue;
                            // create new message
                            NetworkMessage connectedClientMessage = new NetworkMessage(NetSquareMessageID.ClientJoinWorld, client.Key);
                            // set Transform frame
                            client.Value.Serialize(connectedClientMessage);
                            // send message so server event for being custom binded
                            OnSendWorldClients?.Invoke(worldID, client.Key, connectedClientMessage);
                            // add message to list for packing
                            messages.Add(connectedClientMessage);
                        }
                        // pack messages
                        reply.Pack(messages);
                    }
                }
                // reply to the client
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
            ushort worldID;
            NetSquareWorld world;
            if (TryGetClientWorld(message.ClientID, out worldID, out world))
                world.Broadcast(message, useSpatialization);
        }

        /// <summary>
        /// broadcast message from client to any client in the same world
        /// </summary>
        /// <param name="message">message we want to broadcast</param>
        /// <param name="useSpatialization">if this client's world use spatialization, broadcast to anyone visible only</param>
        public void BroadcastToWorld(byte[] message, uint clientID, bool useSpatialization = true)
        {
            ushort worldID;
            NetSquareWorld world;
            if (TryGetClientWorld(clientID, out worldID, out world))
                world.Broadcast(message, clientID, useSpatialization);
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
                ushort worldID = 0;
                NetSquareWorld world = null;

                lock (worldMembershipLock)
                {
                    if (ClientsWorlds.TryRemove(message.ClientID, out worldID))
                    {
                        world = GetWorld(worldID);
                        if (world != null)
                        {
                            // world exist so let's try remove client from it
                            leave = world.TryLeaveWorld(message.ClientID);
                            if (leave)
                                world.Synchronizer?.RemoveMessagesFromClient(message.ClientID);
                        }
                    }
                }

                if (leave && world != null)
                {
                    Writer.Write("Client " + message.ClientID + " leave world " + worldID, ConsoleColor.Gray);
                    if (!world.UseSpatializer) // if spatializer is used, it will handle this event, so let's do nothing here
                    {
                        // tell anyone in this world that a client just leave the world
                        world.Broadcast(new NetworkMessage(NetSquareMessageID.ClientLeaveWorld).Set(message.ClientID));
                    }
                }

                if (message.Client != null)
                    server.Reply(message, new NetworkMessage().Set(leave));
            }
            catch (Exception ex)
            {
                // reply to the client. Reply false because client was not added to world
                server.Reply(message, new NetworkMessage().Set(false));
                Writer.Write("Fail to leave World : client " + message.ClientID + Environment.NewLine + ex.ToString(), ConsoleColor.Red);
            }
        }

        /// <summary>
        /// A client just send a synch frame to the server
        /// </summary>
        /// <param name="message">message that contains a synch frames</param>
        private void SetSynchFrame(NetworkMessage message)
        {
            try
            {
                ushort worldID;
                NetSquareWorld world;
                if (TryGetClientWorld(message.ClientID, out worldID, out world))
                {
                    // if we use a spatializer, we store the frame into it so it can be used for spatialization and send to visible clients later as packed message
                    if (world.UseSpatializer && world.Spatializer != null)
                    {
                        INetSquareSynchFrame frame = NetSquareSynchFramesUtils.GetFrame(message);
                        world.Spatializer.StoreSynchFrame(message.ClientID, frame);
                    }
                    // if we don't use a spatializer, we send the new position directly to everyone in the world
                    else
                    {
                        world.Broadcast(message.Serializer.Buffer, message.ClientID, true);
                    }
                    message.RestartRead();
                }
            }
            catch (Exception ex)
            {
                Writer.Write("Fail to set client position : \n\r" + ex.Message, ConsoleColor.Red);
            }
        }

        /// <summary>
        /// A client just send some synch frames to the server
        /// </summary>
        /// <param name="message">message that contains multiple synch frames</param>
        private unsafe void SetSynchFrames(NetworkMessage message)
        {
            try
            {
                ushort worldID;
                NetSquareWorld world;
                if (TryGetClientWorld(message.ClientID, out worldID, out world))
                {
                    // if we use a spatializer, we store the frames into it so it can be used for spatialization and send to visible clients later as packed message
                    if (world.UseSpatializer && world.Spatializer != null)
                    {
                        world.Spatializer.StoreSynchFrames(message.ClientID, NetSquareSynchFramesUtils.GetFrames(message));
                    }
                    // if we don't use a spatializer, we send the new position directly to everyone in the world
                    else
                    {
                        world.Broadcast(message.Serializer.Buffer, message.ClientID, true);
                    }
                    message.RestartRead();
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
