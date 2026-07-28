using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquare.Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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
        /// Gets the number of active worlds.
        /// </summary>
        public int WorldCount { get { return Worlds.Count; } }
        /// <summary>
        /// Gets the number of clients currently attached to a world.
        /// </summary>
        public int SessionCount { get { return ClientsWorlds.Count; } }
        /// <summary>
        /// Occurs after a world has been removed and its resources have been released.
        /// </summary>
        public event Action<NetSquareWorld> OnWorldRemoved;
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
            lock (worldMembershipLock)
            {
                // Removed identifiers are reused after the allocator wraps.
                for (int attempt = 0; attempt < ushort.MaxValue; attempt++)
                {
                    nextWorldId++;
                    if (nextWorldId > ushort.MaxValue)
                        nextWorldId = 1;

                    ushort id = (ushort)nextWorldId;
                    if (!Worlds.ContainsKey(id))
                        return AddWorldCore(id, name, nbMaxClients);
                }

                throw new InvalidOperationException("No world ID is available.");
            }
        }

        /// <summary>
        /// Add a world
        /// </summary>
        /// <param name="id">Unique world identifier.</param>
        /// <param name="name">Name of the world to add</param>
        /// <param name="nbMaxClients">Maximum clients that can join this world</param>
        /// <returns>Created world.</returns>
        public NetSquareWorld AddWorld(ushort id, string name = "", ushort nbMaxClients = 128)
        {
            lock (worldMembershipLock)
                return AddWorldCore(id, name, nbMaxClients);
        }

        /// <summary>
        /// Creates and registers a world while the manager lifecycle lock is held.
        /// </summary>
        /// <param name="id">Unique world identifier.</param>
        /// <param name="name">World name.</param>
        /// <param name="nbMaxClients">Maximum world population.</param>
        /// <returns>Created world.</returns>
        private NetSquareWorld AddWorldCore(ushort id, string name, ushort nbMaxClients)
        {
            NetSquareWorld world = new NetSquareWorld(server, id, name, nbMaxClients);
            if (!Worlds.TryAdd(id, world))
                throw new InvalidOperationException("World " + id + " already exists.");

            if (id > nextWorldId)
                nextWorldId = id;
            Writer.Write("World " + id + " added", ConsoleColor.Green);
            return world;
        }

        /// <summary>
        /// Removes a world, stops its background workers, and detaches every client session.
        /// </summary>
        /// <param name="id">Identifier of the world to remove.</param>
        /// <returns>True when the world existed and was removed.</returns>
        public bool RemoveWorld(ushort id)
        {
            NetSquareWorld world;
            HashSet<uint> clientIDs = new HashSet<uint>();

            lock (worldMembershipLock)
            {
                if (!Worlds.TryGetValue(id, out world) || world == null || !world.TryBeginRemoval())
                    return false;

                NetSquareWorld removedWorld;
                if (!Worlds.TryRemove(id, out removedWorld))
                {
                    world.CancelRemoval();
                    return false;
                }
                if (!ReferenceEquals(world, removedWorld))
                {
                    // Preserve an externally replaced entry when the public dictionary was mutated.
                    Worlds.TryAdd(id, removedWorld);
                    world.CancelRemoval();
                    return false;
                }

                // Remove every matching membership while world joins and leaves are blocked.
                foreach (KeyValuePair<uint, ushort> membership in ClientsWorlds)
                {
                    if (membership.Value != id)
                        continue;

                    ushort removedWorldID;
                    if (ClientsWorlds.TryRemove(membership.Key, out removedWorldID))
                        clientIDs.Add(membership.Key);
                }

                foreach (uint clientID in world.Clients.Keys)
                    clientIDs.Add(clientID);
            }

            Exception cleanupException = null;
            try
            {
                world.CompleteRemoval();
            }
            catch (Exception exception)
            {
                cleanupException = exception;
            }

            if (clientIDs.Count > 0)
            {
                // Let connected clients reset their local membership before another world is created.
                server.SendToClients(
                    new NetworkMessage(NetSquareMessageID.WorldRemoved).Set(id),
                    clientIDs);
            }

            OnWorldRemoved?.Invoke(world);
            Writer.Write("World " + id + " removed", ConsoleColor.DarkYellow);

            if (cleanupException != null)
                throw new InvalidOperationException(
                    "World " + id + " was removed but one or more resources failed to stop.",
                    cleanupException);

            return true;
        }

        /// <summary>
        /// Removes every registered world and releases their owned resources.
        /// </summary>
        /// <returns>Number of worlds removed.</returns>
        public int RemoveAllWorlds()
        {
            List<Exception> exceptions = null;
            int removedCount = 0;
            ushort[] worldIDs = new List<ushort>(Worlds.Keys).ToArray();
            for (int index = 0; index < worldIDs.Length; index++)
            {
                try
                {
                    if (RemoveWorld(worldIDs[index]))
                        removedCount++;
                }
                catch (Exception exception)
                {
                    if (exceptions == null)
                        exceptions = new List<Exception>();
                    exceptions.Add(exception);
                }
            }

            if (exceptions != null)
                throw new AggregateException(
                    "One or more worlds failed to release all resources.",
                    exceptions);

            return removedCount;
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

            return Worlds.TryGetValue(worldID, out world) &&
                world != null && world.IsActive;
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
        /// Get the worldID witch a client is in
        /// </summary>
        /// <param name="clientID">ID of the client</param>
        /// <returns>ID of the world, or 0 if none. Check before with 'IsClientInWorld()'</returns>
        public ushort GetClientWorldID(uint clientID)
        {
            ushort worldID;
            return ClientsWorlds.TryGetValue(clientID, out worldID) ? worldID : (ushort)0;
        }

        #region Debug Snapshots
        /// <summary>
        /// Creates debug snapshots for all known worlds.
        /// </summary>
        /// <returns>World debug snapshots.</returns>
        public List<NetSquareWorldSnapshot> CreateSnapshots()
        {
            List<NetSquareWorldSnapshot> snapshots = new List<NetSquareWorldSnapshot>();
            foreach (NetSquareWorld world in Worlds.Values)
                if (world != null)
                    snapshots.Add(world.CreateSnapshot());

            return snapshots;
        }
        #endregion

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
            {
                if (world.UseSynchronizer)
                    world.Synchronizer?.AddMessage(message);
                else
                    world.BroadcastUDP(message, world.UseSpatializer, true);
            }
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
                    NetSquareWorld activeWorld;
                    if (!ClientsWorlds.ContainsKey(message.ClientID) &&
                        Worlds.TryGetValue(worldID, out activeWorld) &&
                        ReferenceEquals(world, activeWorld) &&
                        activeWorld.IsActive)
                    {
                        added = activeWorld.TryJoinWorld(message.ClientID, clientTransform);
                        if (added && !ClientsWorlds.TryAdd(message.ClientID, worldID))
                        {
                            activeWorld.TryLeaveWorld(message.ClientID);
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
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void BroadcastToWorld(NetworkMessage message, bool useSpatialization = true, bool excludeSender = true)
        {
            ushort worldID;
            NetSquareWorld world;
            if (TryGetClientWorld(message.ClientID, out worldID, out world))
                world.Broadcast(message, useSpatialization, excludeSender);
        }


        /// <summary>
        /// broadcast message from client to any client in the same world using UDP
        /// </summary>
        /// <param name="message">message we want to broadcast</param>
        /// <param name="useSpatialization">if this client's world use spatialization, broadcast to anyone visible only</param>
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void BroadcastToWorldUnreliable(NetworkMessage message, bool useSpatialization = true, bool excludeSender = true)
        {
            ushort worldID;
            NetSquareWorld world;
            if (TryGetClientWorld(message.ClientID, out worldID, out world))
                world.BroadcastUnreliable(message, useSpatialization, excludeSender);
        }

        /// <summary>
        /// broadcast message from client to any client in the same world
        /// </summary>
        /// <param name="message">message we want to broadcast</param>
        /// <param name="useSpatialization">if this client's world use spatialization, broadcast to anyone visible only</param>
        /// <param name="excludeSender">if true, the sender will not receive the message</param>
        public void BroadcastToWorld(byte[] message, uint clientID, bool useSpatialization = true, bool excludeSender = true)
        {
            ushort worldID;
            NetSquareWorld world;
            if (TryGetClientWorld(clientID, out worldID, out world))
                world.Broadcast(message, clientID, useSpatialization, excludeSender);
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
                    INetSquareSynchFrame frame = NetSquareSynchFramesUtils.GetFrame(message);
                    // if we use a spatializer, we store the frame into it so it can be used for spatialization and send to visible clients later as packed message
                    if (world.UseSpatializer && world.Spatializer != null)
                    {
                        world.Spatializer.StoreSynchFrame(message.ClientID, frame);
                    }
                    // if we don't use a spatializer, we send the new position directly to everyone in the world
                    else
                    {
                        ApplyLatestTransformFrame(world, message.ClientID, frame);
                        world.Broadcast(message.Serializer.Buffer, message.ClientID, true, true);
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
                    INetSquareSynchFrame[] frames = NetSquareSynchFramesUtils.GetFrames(message);
                    // if we use a spatializer, we store the frames into it so it can be used for spatialization and send to visible clients later as packed message
                    if (world.UseSpatializer && world.Spatializer != null)
                    {
                        world.Spatializer.StoreSynchFrames(message.ClientID, frames);
                    }
                    // if we don't use a spatializer, we send the new position directly to everyone in the world
                    else
                    {
                        ApplyLatestTransformFrame(world, message.ClientID, frames);
                        world.Broadcast(message.Serializer.Buffer, message.ClientID, true, true);
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
        /// Applies a frame to the authoritative world transform cache when it contains a transform.
        /// </summary>
        /// <param name="world">World that owns the client.</param>
        /// <param name="clientID">Client that sent the frame.</param>
        /// <param name="frame">Synchronization frame to inspect.</param>
        private static void ApplyLatestTransformFrame(NetSquareWorld world, uint clientID, INetSquareSynchFrame frame)
        {
            if (frame != null && frame.SynchFrameType == 0)
                world.SetClientTransform(clientID, (NetsquareTransformFrame)frame);
        }

        /// <summary>
        /// Applies the most recent transform frame to the authoritative world transform cache.
        /// </summary>
        /// <param name="world">World that owns the client.</param>
        /// <param name="clientID">Client that sent the frames.</param>
        /// <param name="frames">Synchronization frames to inspect.</param>
        private static void ApplyLatestTransformFrame(NetSquareWorld world, uint clientID, INetSquareSynchFrame[] frames)
        {
            NetsquareTransformFrame transformFrame;
            if (NetSquareSynchFramesUtils.TryGetMostRecentTransformFrame(frames, out transformFrame))
                world.SetClientTransform(clientID, transformFrame);
        }
        #endregion
    }
}
