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
        public event Action<uint> OnClientLeaveWorld;
        public event Action<uint, float, float, float> OnClientMove;
        public event Action<NetworkMessage> OnSynchronize;
        public bool SynchronizeUsingUDP { get; set; }
        private NetSquare_Client client;

        public WorldsManager(NetSquare_Client _client, bool synchronizeUsingUDP)
        {
            SynchronizeUsingUDP = synchronizeUsingUDP;
            client = _client;
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientJoinWorld, "ClientJoinCurrentWorld", ClientJoinCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientLeaveWorld, "ClientLeaveCurrentWorld", ClientLeaveCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientSetPosition, "ClientSetPosition", ClientSetPosition);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientsLeaveWorld, "ClientsLeaveCurrentWorld", ClientsLeaveCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientsJoinWorld, "ClientsJoinCurrentWorld", ClientsJoinCurrentWorld);
        }

        #region Public Network Methods
        /// <summary>
        /// Try to Join a world. Can fail if worldID don't exists or client already in world.
        /// if success, OnJoinWorld will be invoked, else OnFailJoinWorld will be invoked
        /// </summary>
        /// <param name="worldID">ID of the world to join</param>
        /// <param name="position">position of the client in the world to join</param>
        /// <param name="Callback">Callback raised after server try added. if true, join success</param>
        public void TryJoinWorld(ushort worldID, Position? position, Action<bool> Callback)
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
                    if (position.HasValue)
                        SetPosition(position.Value);
                    List<NetworkMessage> messages = response.Unpack();
                    foreach (NetworkMessage message in messages)
                        OnClientJoinWorld?.Invoke(message);
                    Callback?.Invoke(true);
                }
                else
                    Callback?.Invoke(false);
            });
        }

        /// <summary>
        /// Try to Join a world. Can fail if worldID don't exists or client already in world.
        /// if success, OnJoinWorld will be invoked, else OnFailJoinWorld will be invoked
        /// </summary>
        /// <param name="worldID">ID of the world to join</param>
        /// <param name="Callback">Callback raised after server try added. if true, join success</param>
        public void TryJoinWorld(ushort worldID, Action<bool> Callback)
        {
            TryJoinWorld(worldID, null, Callback);
        }

        /// <summary>
        /// Try to leave the current world. Can fail if not in world.
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
            OnClientJoinWorld?.Invoke(message);
        }

        private void ClientLeaveCurrentWorld(NetworkMessage message)
        {
            OnClientLeaveWorld?.Invoke(message.GetUInt24().UInt32);
        }

        private void ClientsLeaveCurrentWorld(NetworkMessage message)
        {
            if (OnClientLeaveWorld == null)
                return;

            while (message.CanGetUInt24())
                OnClientLeaveWorld(message.GetUInt24().UInt32);
        }

        private void ClientsJoinCurrentWorld(NetworkMessage packedMessage)
        {
            if (OnClientJoinWorld == null)
                return;

            List<NetworkMessage> messages = packedMessage.UnpackWithoutHead();
            foreach (var message in messages)
                OnClientJoinWorld(message);
        }

        private void ClientSetPosition(NetworkMessage message)
        {
            if (OnClientMove == null)
                return;
            if (message.IsBlockMessage())
                while (message.NextBlock())
                {
                    OnClientMove(message.GetUInt24().UInt32, message.GetFloat(), message.GetFloat(), message.GetFloat());
                }
            else
                OnClientMove(message.ClientID, message.GetFloat(), message.GetFloat(), message.GetFloat());
        }
        #endregion
    }
}