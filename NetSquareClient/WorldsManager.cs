using NetSquare.Core;
using NetSquare.Core.Messages;
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
        public event Action<uint, INetSquareSynchFrame[]> OnReceiveSynchFrames;
        public bool SynchronizeUsingUDP { get; set; }
        public bool AutoSendFrames = true;
        private NetSquareClient client;
        private List<INetSquareSynchFrame> currentClientFrames = new List<INetSquareSynchFrame>();

        public WorldsManager(NetSquareClient _client)
        {
            client = _client;
            client.Dispatcher.AddHeadAction(NetSquareMessageID.ClientJoinWorld, "ClientJoinCurrentWorld", ClientJoinCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageID.ClientLeaveWorld, "ClientLeaveCurrentWorld", ClientLeaveCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageID.ClientsLeaveWorld, "ClientsLeaveCurrentWorld", ClientsLeaveCurrentWorld);
            client.Dispatcher.AddHeadAction(NetSquareMessageID.ClientsJoinWorld, "ClientsJoinCurrentWorld", ClientsJoinCurrentWorld);

            client.Dispatcher.AddHeadAction(NetSquareMessageID.SetSynchFrame, "SetSynchFrame", SetSynchFrame);
            client.Dispatcher.AddHeadAction(NetSquareMessageID.SetSynchFrames, "SetSynchFrames", SetSynchFrames);
            client.Dispatcher.AddHeadAction(NetSquareMessageID.SetSynchFramesPacked, "SetSynchFramesPacked", SetSynchFramesPacked);
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
            NetworkMessage message = new NetworkMessage(NetSquareMessageID.ClientJoinWorld).Set(worldID);
            clientTransform.Serialize(message);
            // send the message to the server
            client.SendMessage(message, (reply) =>
            {
                // if the server reply true, the client is in the world
                if (reply.Serializer.GetBool())
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

            client.SendMessage(new NetworkMessage(NetSquareMessageID.ClientLeaveWorld), (response) =>
            {
                if (response.Serializer.GetBool())
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
            message.SetType(NetSquareMessageType.BroadcastCurrentWorld);
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
            message.SetType(NetSquareMessageType.SynchronizeMessageCurrentWorld);
            if (SynchronizeUsingUDP)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
        }
        #endregion

        #region Synch Frames
        /// <summary>
        /// Synchronize message Data with other client in this world. 
        /// Server will pack clients message and send to anyone in the world at regular interval
        /// Must be in a world
        /// </summary>
        /// <param name="synchFrame"> synch frame to send</param>
        public void SendSynchFrame(INetSquareSynchFrame synchFrame)
        {
            if (!IsInWorld)
                return;
            NetworkMessage message = new NetworkMessage(NetSquareMessageID.SetSynchFrame, client.ClientID);
            synchFrame.Serialize(message);
            if (SynchronizeUsingUDP)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
        }

        /// <summary>
        /// Store a synch frame to send later
        /// </summary>
        /// <param name="synchFrame"> synch frame to store</param>
        public void StoreSynchFrame(INetSquareSynchFrame synchFrame)
        {
            currentClientFrames.Add(synchFrame);
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

            NetworkMessage message = new NetworkMessage(NetSquareMessageID.SetSynchFrames, client.ClientID);
            NetSquareSynchFramesUtils.SerializeFrames(message, currentClientFrames);
            currentClientFrames.Clear();

            if (SynchronizeUsingUDP)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
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
            OnClientLeaveWorld?.Invoke(message.Serializer.GetUInt24().UInt32);
        }

        /// <summary>
        /// Some clients leave the current world
        /// </summary>
        /// <param name="message"> message to read</param>
        private void ClientsLeaveCurrentWorld(NetworkMessage message)
        {
            if (OnClientLeaveWorld == null)
                return;

            while (message.Serializer.CanGetUInt24())
                OnClientLeaveWorld(message.Serializer.GetUInt24().UInt32);
        }

        private void SetSynchFrame(NetworkMessage message)
        {
            if (OnReceiveSynchFrames == null)
                return;
            message.Serializer.DummyRead(1);
            OnReceiveSynchFrames(message.ClientID, new INetSquareSynchFrame[] { NetSquareSynchFramesUtils.GetFrame(message) });
        }

        private unsafe void SetSynchFrames(NetworkMessage message)
        {
            if (OnReceiveSynchFrames == null)
                return;

            INetSquareSynchFrame[] frames = NetSquareSynchFramesUtils.GetFrames(message);
            if (frames.Length > 0)
                OnReceiveSynchFrames(message.ClientID, frames);
        }

        private unsafe void SetSynchFramesPacked(NetworkMessage message)
        {
            if (OnReceiveSynchFrames == null)
                return;

            NetSquareSynchFramesUtils.GetPackedFrames(message, OnReceiveSynchFrames);
        }
        #endregion
    }
}