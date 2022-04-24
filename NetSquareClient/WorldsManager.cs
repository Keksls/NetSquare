using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using System;
using System.Collections.Generic;

namespace NetSquareClient
{
    public class WorldsManager
    {
        public bool IsInWorld { get; private set; }
        public ushort CurrentWorldID { get; private set; }
        public event Action<NetworkMessage> OnClientJoinWorld;
        public event Action<UInt24> OnClientLeaveWorld;
        public event Action<UInt24, float, float, float> OnClientMove;
        public event Action<NetworkMessage> OnSynchronize;
        public HashSet<UInt24> ClientsInWorld { get; private set; }
        public bool SynchronizeUsingUDP { get; set; }
        private NetSquare_Client client;

        public WorldsManager(NetSquare_Client _client, bool synchronizeUsingUDP)
        {
            SynchronizeUsingUDP = synchronizeUsingUDP;
            ClientsInWorld = new HashSet<UInt24>();
            client = _client;
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientJoinWorld, "ClientJoinCurrentWorld", ClientJoinCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientLeaveWorld, "ClientLeaveCurrentWorld", ClientLeaveCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientSetPosition, "ClientSetPosition", ClientSetPosition);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientsLeaveWorld, "ClientsLeaveCurrentWorld", ClientsLeaveCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientsJoinWorld, "ClientJoinCurrentWorld", ClientsJoinCurrentWorld);
        }

        #region Public Network Methods
        /// <summary>
        /// Try to Join a world. Can fail if worldID don't exists or client already in world.
        /// if success, OnJoinWorld will be invoked, else OnFailJoinWorld will be invoked
        /// </summary>
        /// <param name="worldID">ID of the world to join</param>
        /// <param name="Callback">Callback raised after server try added. if true, join success</param>
        public void TryJoinWorld(ushort worldID, Position position, Action<bool> Callback)
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
                    SetPosition(position);
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
            if (SynchronizeUsingUDP)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
        }

        /// <summary>
        /// Synchronize message Data with other client in this world. 
        /// Server will pack clients message and send to anyone in the world at regular interval
        /// Must be in a world
        /// </summary>
        /// <param name="position">position of the player</param>
        public void SetPosition(Position position)
        {
            SetPosition(position.x, position.y, position.z);
        }

        /// <summary>
        /// Synchronize message Data with other client in this world. 
        /// Server will pack clients message and send to anyone in the world at regular interval
        /// Must be in a world
        /// </summary>
        /// <param name="x">x position of the player</param>
        /// <param name="x">y position of the player</param>
        /// <param name="x">z position of the player</param>
        public void SetPosition(float x, float y, float z)
        {
            if (!IsInWorld)
                return;
            NetworkMessage message = new NetworkMessage(NetSquareMessageType.ClientSetPosition, client.ClientID)
                .Set(x).Set(y).Set(z);
            message.SetType(MessageType.SynchronizeMessageCurrentWorld);
            if (SynchronizeUsingUDP)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
        }
        #endregion

        #region Private Utils
        internal void Fire_OnSyncronize(NetworkMessage message)
        {
            OnSynchronize?.Invoke(message);
        }

        private void ClientJoinCurrentWorld(NetworkMessage message)
        {
            ClientsInWorld.Add(message.ClientID);
            OnClientJoinWorld?.Invoke(message);
        }

        private void ClientLeaveCurrentWorld(NetworkMessage message)
        {
            UInt24 clientID = message.GetUInt24();
            ClientsInWorld.Remove(clientID);
            OnClientLeaveWorld?.Invoke(clientID);
        }

        private void ClientsLeaveCurrentWorld(NetworkMessage message)
        {
            while (message.CanGetUInt24())
            {
                UInt24 clientID = message.GetUInt24();
                ClientsInWorld.Remove(clientID);
                OnClientLeaveWorld?.Invoke(clientID);
            }
        }

        private void ClientsJoinCurrentWorld(NetworkMessage packedMessage)
        {
            List<NetworkMessage> messages = packedMessage.Unpack();
            foreach (var message in messages)
            {
                if (ClientsInWorld.Add(message.ClientID))
                    OnClientJoinWorld?.Invoke(message);
            }
        }

        private void ClientSetPosition(NetworkMessage message)
        {
            if (OnClientMove == null)
                return;
            List<NetworkMessage> unpacked = message.Unpack();
            foreach(var block in unpacked)
                OnClientMove(block.ClientID, block.GetFloat(), block.GetFloat(), block.GetFloat());
        }
        #endregion
    }
}