using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Generic;

namespace NetSquare.Client
{
    /// <summary>
    /// Represents the worlds manager component.
    /// </summary>
    public class WorldsManager
    {
        /// <summary>
        /// Gets or sets the is in world value.
        /// </summary>
        public bool IsInWorld { get; private set; }
        /// <summary>
        /// Gets or sets the current world id value.
        /// </summary>
        public ushort CurrentWorldID { get; private set; }
        /// <summary>
        /// Event raised when a client join the world.
        /// ClientID, transform of the client, message received
        /// </summary>
        public event Action<uint, NetsquareTransformFrame, NetworkMessage> OnClientJoinWorld;
        /// <summary>
        /// Occurs when synchronize is raised.
        /// </summary>
        public event NetSquareAction OnSynchronize;
        /// <summary>
        /// Occurs when client leave world is raised.
        /// </summary>
        public event Action<uint> OnClientLeaveWorld;
        /// <summary>
        /// Occurs when receive synch frames is raised.
        /// </summary>
        public event Action<uint, INetSquareSynchFrame[]> OnReceiveSynchFrames;
        /// <summary>
        /// Gets or sets the synchronize using udp value.
        /// </summary>
        public bool SynchronizeUsingUDP
        {
            get { return SynchronizationTransport == NetSquareSyncTransport.UnreliableUdp; }
            set { SynchronizationTransport = value ? NetSquareSyncTransport.UnreliableUdp : NetSquareSyncTransport.ReliableTcp; }
        }
        /// <summary>
        /// Gets or sets the transport used for world synchronization frames.
        /// </summary>
        public NetSquareSyncTransport SynchronizationTransport { get; set; }
        /// <summary>
        /// Gets or sets the maximum queued synchronization frames before older frames are dropped.
        /// </summary>
        public int MaxStoredSynchFrames { get; set; }
        /// <summary>
        /// Stores the auto send frames value.
        /// </summary>
        public bool AutoSendFrames = true;
        /// <summary>
        /// Stores the client value.
        /// </summary>
        private NetSquareClient client;
        /// <summary>
        /// Stores the current client frames value.
        /// </summary>
        private List<INetSquareSynchFrame> currentClientFrames = new List<INetSquareSynchFrame>();
        /// <summary>
        /// Stores the current client frames lock value.
        /// </summary>
        private readonly object currentClientFramesLock = new object();
        /// <summary>
        /// Stores the next synchronization frame sequence id.
        /// </summary>
        private uint nextSynchFrameSequenceID;
        /// <summary>
        /// Stores the synchronization frame sequence lock value.
        /// </summary>
        private readonly object synchFrameSequenceLock = new object();

        /// <summary>
        /// Initializes a new instance of the worlds manager class.
        /// </summary>
        public WorldsManager(NetSquareClient _client)
        {
            client = _client;
            SynchronizationTransport = NetSquareSyncTransport.ReliableTcp;
            MaxStoredSynchFrames = 256;
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
            if (SynchronizationTransport == NetSquareSyncTransport.UnreliableUdp)
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

            synchFrame = PrepareSynchFrame(synchFrame);
            NetworkMessage message = new NetworkMessage(NetSquareMessageID.SetSynchFrame, client.ClientID);
            synchFrame.Serialize(message);
            if (SynchronizationTransport == NetSquareSyncTransport.UnreliableUdp)
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
            if (synchFrame == null)
                return;

            lock (currentClientFramesLock)
            {
                synchFrame = PrepareSynchFrame(synchFrame);
                currentClientFrames.Add(synchFrame);
                TrimStoredSynchFrames();
            }
        }

        /// <summary>
        /// Send all stored transform frames
        /// </summary>
        public unsafe void SendFrames()
        {
            if (!IsInWorld)
                return;

            List<INetSquareSynchFrame> frames;
            lock (currentClientFramesLock)
            {
                if (currentClientFrames.Count == 0)
                    return;

                frames = new List<INetSquareSynchFrame>(currentClientFrames);
                currentClientFrames.Clear();
            }

            NetworkMessage message = new NetworkMessage(NetSquareMessageID.SetSynchFrames, client.ClientID);
            NetSquareSynchFramesUtils.SerializeFrames(message, frames);

            if (SynchronizationTransport == NetSquareSyncTransport.UnreliableUdp)
                client.SendMessageUDP(message);
            else
                client.SendMessage(message);
        }
        #endregion

        #region Private Utils
        /// <summary>
        /// Assigns a sequence id to the frame when it does not have one yet.
        /// </summary>
        /// <param name="synchFrame">Synchronization frame to prepare.</param>
        /// <returns>The prepared synchronization frame.</returns>
        private INetSquareSynchFrame PrepareSynchFrame(INetSquareSynchFrame synchFrame)
        {
            if (synchFrame.SequenceID == 0)
                synchFrame.SequenceID = GetNextSynchFrameSequenceID();

            return synchFrame;
        }

        /// <summary>
        /// Gets the next non-zero synchronization frame sequence id.
        /// </summary>
        /// <returns>The next sequence id.</returns>
        private uint GetNextSynchFrameSequenceID()
        {
            lock (synchFrameSequenceLock)
            {
                nextSynchFrameSequenceID++;
                if (nextSynchFrameSequenceID == 0)
                    nextSynchFrameSequenceID = 1;

                return nextSynchFrameSequenceID;
            }
        }

        /// <summary>
        /// Trims stored synchronization frames to the configured cap.
        /// </summary>
        private void TrimStoredSynchFrames()
        {
            if (MaxStoredSynchFrames <= 0 || currentClientFrames.Count <= MaxStoredSynchFrames)
                return;

            int removeCount = currentClientFrames.Count - MaxStoredSynchFrames;
            currentClientFrames.RemoveRange(0, removeCount);
        }

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

            List<NetworkMessage> messages = packedMessage.Unpack();
            foreach (var message in messages)
            {
                ClientJoinCurrentWorld(message);
            }
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

        /// <summary>
        /// Executes the set synch frame operation.
        /// </summary>
        private void SetSynchFrame(NetworkMessage message)
        {
            if (OnReceiveSynchFrames == null)
                return;

            OnReceiveSynchFrames(message.ClientID, new INetSquareSynchFrame[] { NetSquareSynchFramesUtils.GetFrame(message) });
        }

        /// <summary>
        /// Executes the set synch frames operation.
        /// </summary>
        private unsafe void SetSynchFrames(NetworkMessage message)
        {
            if (OnReceiveSynchFrames == null)
                return;

            INetSquareSynchFrame[] frames = NetSquareSynchFramesUtils.GetFrames(message);
            if (frames.Length > 0)
                OnReceiveSynchFrames(message.ClientID, frames);
        }

        /// <summary>
        /// Executes the set synch frames packed operation.
        /// </summary>
        private unsafe void SetSynchFramesPacked(NetworkMessage message)
        {
            if (OnReceiveSynchFrames == null)
                return;

            NetSquareSynchFramesUtils.GetPackedFrames(message, OnReceiveSynchFrames);
        }
        #endregion
    }
}
