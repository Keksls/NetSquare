using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;

namespace NetSquareClient
{
    public class WorldsManager
    {
        public bool IsInWorld { get; private set; }
        public ushort CurrentWorldID { get; private set; }
        public event Action<NetworkMessage> OnClientJoinWorld;
        public event Action<uint> OnClientLeaveWorld;
        public event Action<uint, NetsquareTransformFrame[]> OnClientMove;
        public event Action<NetworkMessage> OnSynchronize;
        public bool SynchronizeUsingUDP;
        public bool AutoSendFrames = true;
        private NetSquare_Client client;
        private List<NetsquareTransformFrame> currentClientFrames = new List<NetsquareTransformFrame>();

        public WorldsManager(NetSquare_Client _client, bool synchronizeUsingUDP)
        {
            SynchronizeUsingUDP = synchronizeUsingUDP;
            client = _client;
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientJoinWorld, "ClientJoinCurrentWorld", ClientJoinCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientLeaveWorld, "ClientLeaveCurrentWorld", ClientLeaveCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientsLeaveWorld, "ClientsLeaveCurrentWorld", ClientsLeaveCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.ClientsJoinWorld, "ClientsJoinCurrentWorld", ClientsJoinCurrentWorld);

            client.Dispatcher.AddHeadAction(NetSquareMessageType.SetTransform, "SetTransform", SetTransform);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.SetTransformFrames, "SetTransformFrames", SetTransformFrames);
            client.Dispatcher.AddHeadAction(NetSquareMessageType.SetTransformsFramesPacked, "SetTransformsFramesPacked", SetTransformsFramesPacked);

        }

        #region Public Network Methods
        /// <summary>
        /// Try to Join a world. Can fail if worldID don't exists or client already in world.
        /// if success, OnJoinWorld will be invoked, else OnFailJoinWorld will be invoked
        /// </summary>
        /// <param name="worldID">ID of the world to join</param>
        /// <param name="position">position of the client in the world to join</param>
        /// <param name="Callback">Callback raised after server try added. if true, join success</param>
        public void TryJoinWorld(ushort worldID, NetsquareTransformFrame? position, Action<bool> Callback)
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
                        SendTransformFrame(position.Value);
                    Callback?.Invoke(true);
                    List<NetworkMessage> messages = response.Unpack();
                    foreach (NetworkMessage message in messages)
                        OnClientJoinWorld?.Invoke(message);
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
        #endregion

        #region Transforms Frames
        /// <summary>
        /// Synchronize message Data with other client in this world. 
        /// Server will pack clients message and send to anyone in the world at regular interval
        /// Must be in a world
        /// </summary>
        /// <param name="transformFrame">transform frame of the player</param>
        public void SendTransformFrame(NetsquareTransformFrame transformFrame)
        {
            if (!IsInWorld)
                return;
            NetworkMessage message = new NetworkMessage(NetSquareMessageType.SetTransform, client.ClientID);
            message.Set((byte)1);
            transformFrame.Serialize(message);
            if (SynchronizeUsingUDP)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
        }

        /// <summary>
        /// Store a transform frame to send at the next frame
        /// </summary>
        /// <param name="transformFrame"> transform frame to store</param>
        public void StoreTransformFrame(NetsquareTransformFrame transformFrame)
        {
            currentClientFrames.Add(transformFrame);
        }

        /// <summary>
        /// Send all stored transform frames
        /// </summary>
        public void SendFrames()
        {
            if (!IsInWorld)
                return;
            if (currentClientFrames.Count == 0)
                return;
            NetworkMessage message = new NetworkMessage(NetSquareMessageType.SetTransformFrames, client.ClientID);
            message.Set((byte)currentClientFrames.Count);
            foreach (var frame in currentClientFrames)
                frame.Serialize(message);
            if (SynchronizeUsingUDP)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
            currentClientFrames.Clear();
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

        private void SetTransform(NetworkMessage message)
        {
            if (OnClientMove == null)
                return;
            message.DummyRead(1);
            OnClientMove(message.ClientID, new NetsquareTransformFrame[] { new NetsquareTransformFrame(message) });
        }

        private void SetTransformFrames(NetworkMessage message)
        {
            if (OnClientMove == null)
                return;

            byte nbFrames = message.GetByte();
            NetsquareTransformFrame[] frames = new NetsquareTransformFrame[nbFrames];
            for (int i = 0; i < nbFrames; i++)
            {
                frames[i] = new NetsquareTransformFrame(message);
            }
            OnClientMove(message.ClientID, frames);
        }

        private void SetTransformsFramesPacked(NetworkMessage message)
        {
            if (OnClientMove == null)
                return;

            List<NetworkMessage> messages = message.UnpackWithoutHead();

            message.RestartRead();
            while (message.CanGetNextBlock())
            {
                message.DummyRead(3);
                uint clientID = message.GetUInt24().UInt32;
                byte nbFrames = message.GetByte();
                NetsquareTransformFrame[] frames = new NetsquareTransformFrame[nbFrames];
                for (int i = 0; i < nbFrames; i++)
                {
                    frames[i] = new NetsquareTransformFrame(message);
                }
                OnClientMove(clientID, frames);
            }
        }
        #endregion
    }
}