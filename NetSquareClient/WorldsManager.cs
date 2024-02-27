using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using System;
using System.Collections.Generic;

namespace NetSquare.Client
{
    public class WorldsManager
    {
        public bool IsInWorld { get; private set; }
        public ushort CurrentWorldID { get; private set; }
        /// <summary>
        /// Event raised when a client join the world.
        /// ClientID, transform of the client, message received
        /// </summary>
        public event Action<uint, NetsquareTransformFrame, NetworkMessage> OnClientJoinWorld;
        public event NetSquareAction OnSynchronize;
        public event Action<uint> OnClientLeaveWorld;
        public event Action<uint, NetsquareTransformFrame[]> OnClientMove;
        public bool SynchronizeUsingUDP { get; set; }
        public bool AutoSendFrames = true;
        private NetSquareClient client;
        private List<NetsquareTransformFrame> currentClientFrames = new List<NetsquareTransformFrame>();

        public WorldsManager(NetSquareClient _client)
        {
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
        /// <param name="clientTransform">position of the client in the world to join</param>
        /// <param name="Callback">Callback raised after server try added. if true, join success</param>
        public void TryJoinWorld(ushort worldID, NetsquareTransformFrame clientTransform, Action<bool> Callback)
        {
            // if already in world, return false, we must leave the world before join another
            if (IsInWorld)
            {
                Callback?.Invoke(false);
                return;
            }

            // send a message to the server to join the world
            NetworkMessage message = new NetworkMessage(NetSquareMessageType.ClientJoinWorld).Set(worldID);
            clientTransform.Serialize(message);
            // send the message to the server
            client.SendMessage(message, (reply) =>
            {
                // if the server reply true, the client is in the world
                if (reply.GetBool())
                {
                    IsInWorld = true;
                    CurrentWorldID = worldID;
                    Callback?.Invoke(true);
                    // unpack the reply to get all clients in the world
                    List<NetworkMessage> replyMessages = reply.Unpack();
                    foreach (NetworkMessage replyMessage in replyMessages)
                        ClientJoinCurrentWorld(replyMessage);
                }
                else
                    Callback?.Invoke(false);
            });
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
            // set TypeID as broadcast
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
            // set TypeID as synchronize
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
        public unsafe void SendFrames()
        {
            if (!IsInWorld)
                return;
            if (currentClientFrames.Count == 0)
                return;
            NetworkMessage message = new NetworkMessage(NetSquareMessageType.SetTransformFrames, client.ClientID);

            ushort nbFrames = (ushort)currentClientFrames.Count;
            byte[] bytes = new byte[2 + nbFrames * NetsquareTransformFrame.Size];
            // write transform values using pointer
            fixed (byte* ptr = bytes)
            {
                byte* b = ptr;
                // write frames count
                *b = (byte)nbFrames;
                b++;
                *b = (byte)(nbFrames >> 8);
                b++;
                // iterate on each frames of the client to pack them
                for (ushort i = 0; i < nbFrames; i++)
                {
                    currentClientFrames[i].Serialize(ref b);
                }
            }
            message.Set(bytes, false);

            if (SynchronizeUsingUDP)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
            currentClientFrames.Clear();
        }
        #endregion

        #region Private Utils
        /// <summary>
        /// Fire OnSynchronize event
        /// </summary>
        /// <param name="message">message to send</param>
        internal void Fire_OnSyncronize(NetworkMessage message)
        {
            OnSynchronize?.Invoke(message);
        }

        /// <summary>
        /// A client join the current world
        /// </summary>
        /// <param name="message"> message to read</param>
        private void ClientJoinCurrentWorld(NetworkMessage message)
        {
            NetsquareTransformFrame transform = new NetsquareTransformFrame(message);
            OnClientJoinWorld?.Invoke(message.ClientID, transform, message);
        }

        /// <summary>
        /// Some clients join the current world
        /// </summary>
        /// <param name="packedMessage"> message to read</param>
        private void ClientsJoinCurrentWorld(NetworkMessage packedMessage)
        {
            if (OnClientJoinWorld == null)
                return;

            List<NetworkMessage> messages = packedMessage.UnpackWithoutHead();
            foreach (var message in messages)
                ClientJoinCurrentWorld(message);
        }

        /// <summary>
        /// A client leave the current world
        /// </summary>
        /// <param name="message"> message to read</param>
        private void ClientLeaveCurrentWorld(NetworkMessage message)
        {
            OnClientLeaveWorld?.Invoke(message.GetUInt24().UInt32);
        }

        /// <summary>
        /// Some clients leave the current world
        /// </summary>
        /// <param name="message"> message to read</param>
        private void ClientsLeaveCurrentWorld(NetworkMessage message)
        {
            if (OnClientLeaveWorld == null)
                return;

            while (message.CanGetUInt24())
                OnClientLeaveWorld(message.GetUInt24().UInt32);
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

            ushort nbFrames = message.GetUShort();
            NetsquareTransformFrame[] frames = new NetsquareTransformFrame[nbFrames];
            for (ushort i = 0; i < nbFrames; i++)
            {
                frames[i].Deserialize(message);
            }
            OnClientMove(message.ClientID, frames);
        }

        private unsafe void SetTransformsFramesPacked(NetworkMessage message)
        {
            if (OnClientMove == null)
                return;

            message.RestartRead();
            fixed (byte* ptr = message.Data)
            {
                byte* b = ptr + message.currentReadingIndex;
                while (message.CanGetUInt24())
                {
                    uint clientID = (uint)(*b | (*(b + 1) << 8) | (*(b + 2) << 16));
                    b += 3;
                    ushort nbFrames = *(ushort*)(b);
                    b += 2;
                    NetsquareTransformFrame[] frames = new NetsquareTransformFrame[nbFrames];
                    for (ushort i = 0; i < nbFrames; i++)
                    {
                        frames[i].Deserialize(ref b);
                    }
                    OnClientMove(clientID, frames);
                    message.DummyRead(5 + nbFrames * NetsquareTransformFrame.Size);
                }
            }
        }
        #endregion
    }
}