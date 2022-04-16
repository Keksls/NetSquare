using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Generic;

namespace NetSquareClient
{
    public class WorldsManager
    {
        public bool IsInWorld { get; private set; }
        public ushort CurrentWorldID { get; private set; }
        public event Action<NetworkMessage> OnClientJoinWorld;
        public event Action<uint> OnClientLeaveWorld;
        public event Action<NetworkMessage> OnSynchronize;
        public HashSet<uint> ClientsInWorld { get; private set; }
        private NetSquare_Client client;

        public WorldsManager(NetSquare_Client _client)
        {
            ClientsInWorld = new HashSet<uint>();
            client = _client;
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientJoinWorld, "ClientJoinCurrentWorld", ClientJoinCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientLeaveWorld, "ClientLeaveCurrentWorld", ClientLeaveCurrentWorld);
        }

        /// <summary>
        /// Try to Join a world. Can fail if worldID don't exists or client already in world.
        /// if success, OnJoinWorld will be invoked, else OnFailJoinWorld will be invoked
        /// </summary>
        /// <param name="worldID">ID of the world to join</param>
        /// <param name="Callback">Callback raised after server try added. if true, join success</param>
        public void TryJoinWorld(ushort worldID, Action<bool> Callback)
        {
            if (IsInWorld)
            {
                Callback?.Invoke(false);
                return;
            }

            client.SendMessage(new NetworkMessage(NetSquareMessageType.ClientJoinWorld).Set(worldID), (response) =>
            {
                if (response.GetBool())
                {
                    IsInWorld = true;
                    CurrentWorldID = worldID;
                    Callback?.Invoke(true);
                }
                else
                    Callback?.Invoke(false);
            });
        }
        
        /// <summary>
        /// Try to leave the current world. Can fail if not in world.
        /// if success, OnJoinWorld will be invoked, else OnFailJoinWorld will be invoked
        /// </summary>
        /// <param name="Callback">Callback raised after server try leave. if true, leave success. can be null</param>
        public void TryleaveWorld(Action<bool> Callback)
        {
            if (!IsInWorld)
            {
                Callback?.Invoke(false);
                return;
            }

            client.SendMessage(new NetworkMessage(NetSquareMessageType.ClientLeaveWorld), (response) =>
            {
                if (response.GetBool())
                {
                    IsInWorld = false;
                    CurrentWorldID = 0;
                    Callback?.Invoke(true);
                }
                else
                    Callback?.Invoke(false);
            });
        }

        /// <summary>
        /// Send a networkMessage to any client in the same world I am. Must be in a world
        /// </summary>
        /// <param name="message">message to send</param>
        public void Broadcast(NetworkMessage message)
        {
            if (!IsInWorld)
                return;
            // set TypeID as 1, because 1 is the broadcast ID
            message.SetType(MessageType.BroadcastCurrentWorld);
            client.SendMessage(message);
        }

        /// <summary>
        /// Synchronize message Data with other client in this world. 
        /// Server will pack clients message and send to anyone in the world at regular interval
        /// Must be in a world
        /// </summary>
        /// <param name="message">message to sync</param>
        public void Synchronize(NetworkMessage message)
        {
            if (!IsInWorld)
                return;
            // set TypeID as 2, because 2 is the sync ID
            message.SetType(MessageType.SynchronizeMessageCurrentWorld);
            client.SendMessageUDP(message);
        }

        internal void Fire_OnSyncronize(NetworkMessage message)
        {
            OnSynchronize?.Invoke(message);
        }

        private void ClientJoinCurrentWorld(NetworkMessage message)
        {
            uint clientID = message.ClientID;
            ClientsInWorld.Add(clientID);
            OnClientJoinWorld?.Invoke(message);
        }

        private void ClientLeaveCurrentWorld(NetworkMessage message)
        {
            uint clientID = message.GetUInt();
            ClientsInWorld.Remove(clientID);
            OnClientLeaveWorld?.Invoke(clientID);
        }
    }
}