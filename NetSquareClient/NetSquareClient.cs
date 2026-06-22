using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace NetSquare.Client
{
    /// <summary>
    /// Represents the net square client component.
    /// </summary>
    public class NetSquareClient
    {
        #region Events
        /// <summary>
        /// Occurs when disconected is raised.
        /// </summary>
        public event Action OnDisconected;
        /// <summary>
        /// Occurs when connected is raised.
        /// </summary>
        public event Action<uint> OnConnected;
        /// <summary>
        /// Occurs when connection fail is raised.
        /// </summary>
        public event Action OnConnectionFail;
        /// <summary>
        /// Occurs when unregistered message received is raised.
        /// </summary>
        public event Action<NetworkMessage> OnUnregisteredMessageReceived;
        /// <summary>
        /// Occurs when exception is raised.
        /// </summary>
        public event Action<Exception> OnException;
        #endregion

        #region Variables
        /// <summary>
        /// Stores the dispatcher value.
        /// </summary>
        public NetSquareDispatcher Dispatcher;
        /// <summary>
        /// Gets or sets the worlds manager value.
        /// </summary>
        public WorldsManager WorldsManager { get; private set; }
        /// <summary>
        /// Gets or sets the client value.
        /// </summary>
        public ConnectedClient Client { get; private set; }
        /// <summary>
        /// Gets or sets the port value.
        /// </summary>
        public int Port { get; private set; }
        /// <summary>
        /// Gets or sets the ip adress value.
        /// </summary>
        public string IPAdress { get; private set; }
        /// <summary>
        /// Gets or sets the client id value.
        /// </summary>
        public uint ClientID { get { return Client != null ? Client.ID : 0; } }
        /// <summary>
        /// Gets or sets the is connected value.
        /// </summary>
        public bool IsConnected { get { return Client?.TcpSocket?.Connected ?? false; } }
        /// <summary>
        /// Gets or sets the nb sending messages value.
        /// </summary>
        public int NbSendingMessages { get { return Client != null ? Client.NbMessagesToSend : 0; } }
        /// <summary>
        /// Gets or sets the nb processing messages value.
        /// </summary>
        public int NbProcessingMessages { get { return messagesQueue.Count; } }
        /// <summary>
        /// Stores the protocole type value.
        /// </summary>
        public NetSquareProtocoleType ProtocoleType;
        /// <summary>
        /// Gets or sets the is time synchonized value.
        /// </summary>
        public bool IsTimeSynchonized { get { return hasServerTimeOffset; } }
        /// <summary>
        /// Gets whether the server time is synchronized.
        /// </summary>
        public bool IsTimeSynchronized { get { return hasServerTimeOffset; } }
        /// <summary>
        /// Gets whether automatic server time synchronization is enabled.
        /// </summary>
        public bool IsAutoTimeSynchronizationEnabled { get { return isAutoSynchronizingTime; } }
        /// <summary>
        /// Stores the is synchronizing time value.
        /// </summary>
        private volatile bool isSynchronizingTime = false;
        /// <summary>
        /// Gets or sets the server time offset value.
        /// </summary>
        public float ServerTimeOffset { get; private set; }
        /// <summary>
        /// Gets the target server time offset used by smoothing.
        /// </summary>
        public float TargetServerTimeOffset { get; private set; }
        /// <summary>
        /// Gets or sets whether server time offset changes are smoothed.
        /// </summary>
        public bool SmoothServerTimeOffset { get; set; }
        /// <summary>
        /// Gets or sets the server time offset smoothing speed.
        /// </summary>
        public float ServerTimeOffsetSmoothingSpeed { get; set; }
        /// <summary>
        /// Gets or sets the timeout for one time synchronization request.
        /// </summary>
        public int TimeSynchronizationRequestTimeoutMs { get; set; }
        /// <summary>
        /// Gets or sets the maximum request attempts for one synchronization. Use 0 to derive it from precision.
        /// </summary>
        public int TimeSynchronizationMaxAttempts { get; set; }
        /// <summary>
        /// Gets the current automatic time synchronization interval.
        /// </summary>
        public int AutoTimeSynchronizationIntervalMs { get; private set; }
        /// <summary>
        /// Gets when the server time offset was last synchronized.
        /// </summary>
        public DateTime LastServerTimeSynchronizationUtc { get; private set; }
        /// <summary>
        /// Stores whether server time was synchronized at least once.
        /// </summary>
        private bool hasServerTimeOffset;
        /// <summary>
        /// Stores the last server time offset update timestamp.
        /// </summary>
        private DateTime lastServerTimeOffsetUpdateUtc;
        /// <summary>
        /// Stores the time synchronization lock value.
        /// </summary>
        private readonly object timeSynchronizationLock = new object();
        /// <summary>
        /// Stores the active time synchronization generation value.
        /// </summary>
        private volatile int timeSynchronizationGeneration;
        /// <summary>
        /// Stores the automatic time synchronization lock value.
        /// </summary>
        private readonly object autoTimeSynchronizationLock = new object();
        /// <summary>
        /// Stores whether automatic server time synchronization is running.
        /// </summary>
        private volatile bool isAutoSynchronizingTime;
        /// <summary>
        /// Stores the automatic time synchronization thread.
        /// </summary>
        private Thread autoTimeSynchronizationThread;
        /// <summary>
        /// Signals the automatic time synchronization thread to stop.
        /// </summary>
        private ManualResetEventSlim autoTimeSynchronizationStopSignal = new ManualResetEventSlim(false);
        /// <summary>
        /// Stores the high precision time synchronization protocol version.
        /// </summary>
        private const byte HighPrecisionTimeSynchronizationVersion = 1;
        /// <summary>
        /// Represents one server time synchronization sample.
        /// </summary>
        private struct TimeSynchronizationSample
        {
            public float Offset;
            public float RoundTrip;
        }
        /// <summary>
        /// Stores the nb reply asked value.
        /// </summary>
        private uint nbReplyAsked = 0;
        /// <summary>
        /// Stores the reply id lock value.
        /// </summary>
        private readonly object replyIDLock = new object();
        /// <summary>
        /// Stores the is started value.
        /// </summary>
        private bool isStarted;
        /// <summary>
        /// Stores the messages queue value.
        /// </summary>
        private ConcurrentQueue<NetworkMessage> messagesQueue = new ConcurrentQueue<NetworkMessage>();
        /// <summary>
        /// Stores the messages available value.
        /// </summary>
        private SemaphoreSlim messagesAvailable = new SemaphoreSlim(0);
        /// <summary>
        /// Stores the reply call back value.
        /// </summary>
        private ConcurrentDictionary<uint, NetSquareAction> replyCallBack = new ConcurrentDictionary<uint, NetSquareAction>();
        /// <summary>
        /// Stores reply callbacks that must run directly from the network processing thread.
        /// </summary>
        private ConcurrentDictionary<uint, bool> replyCallBackInlineExecution = new ConcurrentDictionary<uint, bool>();
        /// <summary>
        /// Stores the disconnect started value.
        /// </summary>
        private int disconnectStarted = 1;
        /// <summary>
        /// Stores the disconnect notice timeout ms value.
        /// </summary>
        public static int DisconnectNoticeTimeoutMs = 500;
        /// <summary>
        /// Stores the types dic value.
        /// </summary>
        private static Dictionary<Type, Action<NetworkMessage, object>> typesDic;
        #endregion

        /// <summary>
        /// Instantiate a new NetSquare client
        /// </summary>
        /// <param name="autoBindNetsquareActions">If true, will automatically bind all NetSquareActions from the assembly</param>
        public NetSquareClient(bool autoBindNetsquareActions = true)
        {
            Dispatcher = new NetSquareDispatcher();
            SmoothServerTimeOffset = true;
            ServerTimeOffsetSmoothingSpeed = 8f;
            TimeSynchronizationRequestTimeoutMs = 1500;
            TimeSynchronizationMaxAttempts = 0;
            AutoTimeSynchronizationIntervalMs = 30000;
            lastServerTimeOffsetUpdateUtc = DateTime.UtcNow;
            LastServerTimeSynchronizationUtc = DateTime.MinValue;
            if (autoBindNetsquareActions)
                Dispatcher.AutoBindHeadActionsFromAttributes();
            Dispatcher.AddHeadAction(NetSquareMessageID.Disconnecting, "ServerDisconnecting", ServerDisconnecting);
            WorldsManager = new WorldsManager(this);

            // initiate Type Dictionnary
            typesDic = new Dictionary<Type, Action<NetworkMessage, object>>
            {
                { typeof(short), (message, item) => { message.Set((short)Convert.ChangeType(item, typeof(short))); } },
                { typeof(int), (message, item) => { message.Set((int)Convert.ChangeType(item, typeof(int))); } },
                { typeof(long), (message, item) => { message.Set((long)Convert.ChangeType(item, typeof(long))); } },
                { typeof(float), (message, item) => { message.Set((float)Convert.ChangeType(item, typeof(float))); } },
                { typeof(double), (message, item) => { message.Set((double)Convert.ChangeType(item, typeof(double))); } },
                { typeof(ushort), (message, item) => { message.Set((ushort)Convert.ChangeType(item, typeof(ushort))); } },
                { typeof(uint), (message, item) => { message.Set((uint)Convert.ChangeType(item, typeof(uint))); } },
                { typeof(ulong), (message, item) => { message.Set((ulong)Convert.ChangeType(item, typeof(ulong))); } },
                { typeof(UInt24), (message, item) => { message.Set((UInt24)Convert.ChangeType(item, typeof(UInt24))); } },
                { typeof(bool), (message, item) => { message.Set((bool)Convert.ChangeType(item, typeof(bool))); } },
                { typeof(string), (message, item) => { message.Set((string)Convert.ChangeType(item, typeof(string))); } },
                { typeof(char), (message, item) => { message.Set((char)Convert.ChangeType(item, typeof(char))); } },
                { typeof(byte[]), (message, item) => { message.Set((byte[])Convert.ChangeType(item, typeof(byte[]))); } }
            };
        }

        #region Connection / Disconnection
        /// <summary>
        /// Connect this client to the given NetSquare server IP and port
        /// </summary>
        /// <param name="hostNameOrIpAddress">HostName or IP Adress to connect on</param>
        /// <param name="port">Port to connect on</param>
        public void Connect(string hostNameOrIpAddress, int port, NetSquareProtocoleType protocoleType = NetSquareProtocoleType.TCP_AND_UDP, bool synchronizeUsingUDP = true)
        {
            try
            {
                // if we want to synchronize using UDP, we need to use TCP too
                if (synchronizeUsingUDP)
                    protocoleType = NetSquareProtocoleType.TCP_AND_UDP;
                ProtocoleType = protocoleType;
                WorldsManager.SynchronizeUsingUDP = synchronizeUsingUDP;
                // connect to server
                Port = port;
                IPAdress = hostNameOrIpAddress;
                TcpClient tcpClient = new TcpClient();
                tcpClient.NoDelay = true;
                tcpClient.Connect(hostNameOrIpAddress, port);

                // start routine that will validate server connection
                ThreadPool.QueueUserWorkItem((e) => { ValidateConnection(tcpClient); });
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Connect this client to the given NetSquare server using an explicit synchronization transport.
        /// </summary>
        /// <param name="hostNameOrIpAddress">HostName or IP address to connect on.</param>
        /// <param name="port">Port to connect on.</param>
        /// <param name="protocoleType">Socket protocol to use.</param>
        /// <param name="synchronizationTransport">Transport used for world synchronization frames.</param>
        public void Connect(string hostNameOrIpAddress, int port, NetSquareProtocoleType protocoleType, NetSquareSyncTransport synchronizationTransport)
        {
            Connect(hostNameOrIpAddress, port, protocoleType, synchronizationTransport == NetSquareSyncTransport.UnreliableUdp);
            WorldsManager.SynchronizationTransport = synchronizationTransport;
        }

        /// <summary>
        /// Disconnect this client from server
        /// </summary>
        public void Disconnect()
        {
            DisconnectInternal(true);
        }
        #endregion

        #region Message Logic
        /// <summary>
        /// Validate NetSquare handShake to server. Ensure that we are connected to a netSquare server
        /// </summary>
        private void ValidateConnection(TcpClient tcpClient)
        {
            long timeEnd = DateTime.Now.AddSeconds(30).Ticks;
            int step = 0;
            int key = 0;
            while (tcpClient != null && tcpClient.Connected && DateTime.Now.Ticks < timeEnd)
            {
                // Handle Byte Avaliable
                if (step == 0 && tcpClient.Available >= 8)
                {
                    byte[] array = new byte[8];
                    tcpClient.Client.Receive(array, 0, 8, SocketFlags.None);
                    int rnd1 = BitConverter.ToInt32(array, 0);
                    int rnd2 = BitConverter.ToInt32(array, 4);
                    key = HandShake.GetKey(rnd1, rnd2);
                    byte[] rep = BitConverter.GetBytes(key);
                    tcpClient.Client.Send(rep, 0, rep.Length, SocketFlags.None);
                    step = 1;
                }
                else if (step == 1 && tcpClient.Available >= 3)
                {
                    byte[] array = new byte[3];
                    tcpClient.Client.Receive(array, 0, 3, SocketFlags.None);
                    // let's reply server same ID as validation
                    Client = new ConnectedClient()
                    {
                        ID = UInt24.GetUInt(array)
                    };
                    Client.SetClient(tcpClient.Client, true, ProtocoleType == NetSquareProtocoleType.TCP_AND_UDP);
                    Client.OnMessageReceived += Client_OnMessageReceived;
                    Client.OnDisconected += Client_OnDisconected;
                    Interlocked.Exchange(ref disconnectStarted, 0);
                    isStarted = true;
                    // start processing message loop
                    Thread processingThread = new Thread(ProcessMessagesLoop);
                    processingThread.IsBackground = true;
                    processingThread.Start();
                    OnConnected?.Invoke(ClientID);
                    break;
                }
                Thread.Sleep(1);
            }
            if (Client == null)
                OnConnectionFail?.Invoke();
        }

        /// <summary>
        /// invoked when new message was received from server
        /// </summary>
        /// <param name="message"></param>
        private void Client_OnMessageReceived(NetworkMessage message)
        {
            messagesQueue.Enqueue(message);
            messagesAvailable.Release();
        }

        /// <summary>
        /// Invoked when the connected client socket is disconnected.
        /// </summary>
        /// <param name="clientID">The client ID.</param>
        private void Client_OnDisconected(uint clientID)
        {
            DisconnectInternal(false);
        }

        /// <summary>
        /// Invoked when the server announces it is disconnecting.
        /// </summary>
        /// <param name="message">The message.</param>
        private void ServerDisconnecting(NetworkMessage message)
        {
            DisconnectInternal(false);
        }

        /// <summary>
        /// Disconnect this client.
        /// </summary>
        /// <param name="notifyRemote">If true, send a disconnect notice before closing.</param>
        private void DisconnectInternal(bool notifyRemote)
        {
            if (Interlocked.Exchange(ref disconnectStarted, 1) != 0)
                return;

            StopAutoSyncTime(false);
            CancelTimeSynchronization();

            ConnectedClient client = Client;
            if (notifyRemote)
                TryNotifyServerDisconnecting(client);

            isStarted = false;
            messagesAvailable.Release();
            Client = null;

            if (client != null)
            {
                client.OnMessageReceived -= Client_OnMessageReceived;
                client.OnDisconected -= Client_OnDisconected;
                try { client.TcpSocket.Close(); } catch { }
                try { client.TcpSocket.Dispose(); } catch { }
            }

            OnDisconected?.Invoke();
        }

        /// <summary>
        /// Try to tell the server this client is disconnecting before closing the socket.
        /// </summary>
        /// <param name="client">The connected client.</param>
        private void TryNotifyServerDisconnecting(ConnectedClient client)
        {
            if (client == null || client.TcpSocket == null || !client.TcpSocket.Connected)
                return;

            try
            {
                client.AddTCPMessageAndWait(new NetworkMessage(NetSquareMessageID.Disconnecting, client.ID), DisconnectNoticeTimeoutMs);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
            }
        }

        /// <summary>
        /// Loop that process the received messages
        /// </summary>
        private void ProcessMessagesLoop()
        {
            NetworkMessage message = null;
            while (isStarted)
            {
                try
                {
                    messagesAvailable.Wait(100);
                    while (messagesQueue.TryDequeue(out message))
                    {
                        switch ((NetSquareMessageType)message.MsgType)
                        {
                            // It's a default message, we need to dispatch it to the right action
                            default:
                            case NetSquareMessageType.Default:
                                if (!Dispatcher.DispatchMessage(message))
                                    OnUnregisteredMessageReceived?.Invoke(message);
                                break;

                            // It's a reply message, we need to invoke the callback
                            case NetSquareMessageType.Reply:
                                NetSquareAction callback;
                                if (replyCallBack.TryRemove(message.ReplyID, out callback))
                                {
                                    bool executeInline;
                                    replyCallBackInlineExecution.TryRemove(message.ReplyID, out executeInline);
                                    if (executeInline)
                                        callback?.Invoke(message);
                                    else
                                        Dispatcher.ExecuteinMainThread(callback, message);
                                }
                                break;

                            // It's a Synchronize message, let's invoke WorldsManager Syncronizer to handle it
                            case NetSquareMessageType.SynchronizeMessageCurrentWorld:
                                Dispatcher.ExecuteinMainThread((msg) =>
                                {
                                    WorldsManager.Fire_OnSyncronize(msg);
                                }, message);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnException?.Invoke(ex);
                }
            }
        }

        /// <summary>
        /// Executes the get next reply id operation.
        /// </summary>
        private uint GetNextReplyID()
        {
            lock (replyIDLock)
            {
                do
                {
                    nbReplyAsked++;
                    if (nbReplyAsked == 0 || nbReplyAsked > UInt24.MaxValue)
                        nbReplyAsked = 1;
                }
                while (replyCallBack.ContainsKey(nbReplyAsked));

                return nbReplyAsked;
            }
        }
        #endregion

        #region Sending messages TCP
        /// <summary>
        /// Send a message to server without waiting for response
        /// </summary>
        /// <param name="msg">message to send</param>
        public void SendMessage(NetworkMessage msg)
        {
            msg.ClientID = Client.ID;
            Client.AddTCPMessage(msg);
        }

        /// <summary>
        /// Send an empty message to server without waiting for response
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessage(ushort HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddTCPMessage(msg);
        }

        /// <summary>
        /// Send an empty message to server without waiting for response
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessage(Enum HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddTCPMessage(msg);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="msg">message to send</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(NetworkMessage msg, NetSquareAction callback)
        {
            SendMessageWithReply(msg, callback, false);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(ushort headID, NetSquareAction callback)
        {
            SendMessageWithReply(new NetworkMessage(headID), callback, false);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(Enum headID, NetSquareAction callback)
        {
            SendMessageWithReply(new NetworkMessage(headID), callback, false);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server responds.
        /// </summary>
        private uint SendMessageWithReply(NetworkMessage msg, NetSquareAction callback, bool executeReplyInline)
        {
            uint rplID = GetNextReplyID();
            msg.ReplyTo(rplID);
            replyCallBack[rplID] = callback;
            if (executeReplyInline)
                replyCallBackInlineExecution[rplID] = true;

            try
            {
                SendMessage(msg);
                return rplID;
            }
            catch
            {
                RemoveReplyCallback(rplID);
                throw;
            }
        }

        /// <summary>
        /// Removes a pending reply callback.
        /// </summary>
        private void RemoveReplyCallback(uint replyID)
        {
            NetSquareAction callback;
            bool executeInline;
            replyCallBack.TryRemove(replyID, out callback);
            replyCallBackInlineExecution.TryRemove(replyID, out executeInline);
        }

        /// <summary>
        /// Send a network message to the server
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="items">Items to set into messages, can only be primitives types handeled by NetSquare, a bit slower than creating the network message yourself but faster to write. Only for lazy dev</param>
        public void SendMessage(ushort headID, params object[] items)
        {
            NetworkMessage message = new NetworkMessage(headID, Client.ID);
            foreach (object item in items)
            {
                Type itemType = item.GetType();
                if (typesDic.ContainsKey(itemType))
                    typesDic[itemType].Invoke(message, item);
                else
                    throw new Exception("Item type not handled by NetSquare");
            }
            SendMessage(message);
        }
        #endregion

        #region Sending messages UDP
        /// <summary>
        /// Send a message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="msg">message to send</param>
        public void SendMessageUDP(NetworkMessage msg)
        {
            msg.ClientID = Client.ID;
            Client.AddUnreliableMessage(msg.HeadID, msg.Serialize());
        }

        /// <summary>
        /// Send an empty message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessageUDP(ushort HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddUnreliableMessage(HeadID, msg.Serialize());
        }

        /// <summary>
        /// Send an empty message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessageUDP(Enum HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddUnreliableMessage(msg.HeadID, msg.Serialize());
        }

        /// <summary>
        /// Send a network message to the server
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="items">Items to set into messages, can only be primitives types handeled by NetSquare, a bit slower than creating the network message yourself but faster to write. Only for lazy dev</param>
        public void SendMessageUDP(ushort headID, params object[] items)
        {
            NetworkMessage message = new NetworkMessage(headID, Client.ID);
            foreach (object item in items)
            {
                Type itemType = item.GetType();
                if (typesDic.ContainsKey(itemType))
                    typesDic[itemType].Invoke(message, item);
                else
                    throw new Exception("Item type not handled by NetSquare");
            }
            SendMessageUDP(message);
        }
        #endregion

        #region Time Synchronization
        /// <summary>
        /// Get server time from client time
        /// </summary>
        /// <param name="clientTime"> Client time</param>
        /// <returns> Server time</returns>
        public float GetServerTime(float clientTime)
        {
            UpdateSmoothedServerTimeOffset();
            return clientTime + ServerTimeOffset;
        }

        /// <summary>
        /// Synchronize time with server. The more precision, the more time it will take to synchronize
        /// </summary>
        /// <param name="precision">1 to 10, 1 is the less precise, 10 is the most precise</param>
        /// <param name="timeBetweenSyncs">Time between each sync in milliseconds</param>
        /// <param name="onServerTimeGet">Callback</param>
        public void SyncTime(Func<float> getClientTime, int precision = 5, int timeBetweenSyncs = 1000, Action<float> onServerTimeGet = null, Action<string> onLog = null)
        {
            int generation;
            TryStartTimeSynchronization(getClientTime, precision, timeBetweenSyncs, onServerTimeGet, onLog, true, out generation);
        }

        /// <summary>
        /// Starts automatic server time synchronization.
        /// </summary>
        /// <param name="getClientTime">Monotonic local client time source.</param>
        /// <param name="precision">Samples per synchronization.</param>
        /// <param name="timeBetweenSyncs">Time between samples in milliseconds.</param>
        /// <param name="intervalMs">Time between synchronization rounds in milliseconds.</param>
        /// <param name="onServerTimeGet">Callback invoked when server time is updated.</param>
        /// <param name="onLog">Optional log callback.</param>
        public void StartAutoSyncTime(Func<float> getClientTime, int precision = 3, int timeBetweenSyncs = 50, int intervalMs = 30000, Action<float> onServerTimeGet = null, Action<string> onLog = null)
        {
            if (getClientTime == null)
                throw new ArgumentNullException(nameof(getClientTime));

            precision = Math.Max(1, Math.Min(10, precision));
            timeBetweenSyncs = Math.Max(0, timeBetweenSyncs);
            intervalMs = Math.Max(1000, intervalMs);

            StopAutoSyncTime();

            lock (autoTimeSynchronizationLock)
            {
                AutoTimeSynchronizationIntervalMs = intervalMs;
                autoTimeSynchronizationStopSignal.Reset();
                isAutoSynchronizingTime = true;
                autoTimeSynchronizationThread = new Thread(() =>
                {
                    AutoTimeSynchronizationLoop(getClientTime, precision, timeBetweenSyncs, intervalMs, onServerTimeGet, onLog);
                });
                autoTimeSynchronizationThread.IsBackground = true;
                autoTimeSynchronizationThread.Start();
            }
        }

        /// <summary>
        /// Stops automatic server time synchronization.
        /// </summary>
        public void StopAutoSyncTime()
        {
            StopAutoSyncTime(true);
        }

        /// <summary>
        /// Returns whether the server time synchronization was refreshed within the given age.
        /// </summary>
        /// <param name="maxAgeMs">Maximum synchronization age in milliseconds.</param>
        /// <returns>true when the current server time offset is recent enough.</returns>
        public bool IsServerTimeSynchronizationFresh(int maxAgeMs)
        {
            if (!hasServerTimeOffset)
                return false;

            maxAgeMs = Math.Max(0, maxAgeMs);
            return (DateTime.UtcNow - LastServerTimeSynchronizationUtc).TotalMilliseconds <= maxAgeMs;
        }

        /// <summary>
        /// Starts one server time synchronization round.
        /// </summary>
        private bool TryStartTimeSynchronization(Func<float> getClientTime, int precision, int timeBetweenSyncs, Action<float> onServerTimeGet, Action<string> onLog, bool cancelIfAlreadySynchronizing, out int generation)
        {
            generation = 0;
            if (getClientTime == null)
                throw new ArgumentNullException(nameof(getClientTime));

            precision = Math.Max(1, Math.Min(10, precision));
            timeBetweenSyncs = Math.Max(0, timeBetweenSyncs);

            lock (timeSynchronizationLock)
            {
                if (isSynchronizingTime)
                {
                    if (cancelIfAlreadySynchronizing)
                    {
                        isSynchronizingTime = false;
                        timeSynchronizationGeneration++;
                    }
                    return false;
                }

                isSynchronizingTime = true;
                generation = ++timeSynchronizationGeneration;
            }

            int requestTimeoutMs = Math.Max(100, Math.Min(30000, TimeSynchronizationRequestTimeoutMs));
            int maxAttempts = TimeSynchronizationMaxAttempts > 0
                ? Math.Max(precision, TimeSynchronizationMaxAttempts)
                : Math.Max(precision, precision * 2);

            int syncGeneration = generation;
            Thread syncThread = new Thread(() =>
            {
                List<TimeSynchronizationSample> samples = new List<TimeSynchronizationSample>(precision);
                int attemptsDone = 0;

                try
                {
                    for (int attempt = 0; attempt < maxAttempts && samples.Count < precision; attempt++)
                    {
                        if (!IsTimeSynchronizationActive(syncGeneration))
                            return;

                        attemptsDone++;
                        TimeSynchronizationSample sample;
                        if (TryRequestServerTimeSample(getClientTime, requestTimeoutMs, syncGeneration, onLog, out sample))
                        {
                            samples.Add(sample);
                            onLog?.Invoke($"Time sync sample {samples.Count}/{precision} | Offset : {sample.Offset} | RTT : {(sample.RoundTrip * 1000f):F1} ms");

                            if (samples.Count == 1 && !hasServerTimeOffset)
                            {
                                SetServerTimeOffset(sample.Offset, true);
                                onServerTimeGet?.Invoke(GetServerTime(getClientTime()));
                            }
                        }

                        if (samples.Count < precision && attempt + 1 < maxAttempts && !SleepDuringTimeSynchronization(syncGeneration, timeBetweenSyncs))
                            return;
                    }

                    if (!IsTimeSynchronizationActive(syncGeneration))
                        return;

                    if (samples.Count == 0)
                    {
                        onLog?.Invoke("Time sync failed: no server response received.");
                        return;
                    }

                    float offset = GetFilteredServerTimeOffset(samples);
                    SetServerTimeOffset(offset, false);

                    onLog?.Invoke($"Time sync offset : {offset} | Samples : {samples.Count}/{precision} | Attempts : {attemptsDone}/{maxAttempts}");
                    onLog?.Invoke($"Client time : {getClientTime()} | Server time : {GetServerTime(getClientTime())}");
                    onServerTimeGet?.Invoke(GetServerTime(getClientTime()));
                }
                finally
                {
                    FinishTimeSynchronization(syncGeneration);
                }
            });

            syncThread.IsBackground = true;
            syncThread.Start();
            return true;
        }

        /// <summary>
        /// Runs automatic time synchronization while enabled.
        /// </summary>
        private void AutoTimeSynchronizationLoop(Func<float> getClientTime, int precision, int timeBetweenSyncs, int intervalMs, Action<float> onServerTimeGet, Action<string> onLog)
        {
            try
            {
                while (isAutoSynchronizingTime)
                {
                    if (IsConnected)
                    {
                        int generation;
                        if (TryStartTimeSynchronization(getClientTime, precision, timeBetweenSyncs, onServerTimeGet, onLog, false, out generation))
                            WaitForTimeSynchronizationCompletion(generation);
                    }

                    int waitMs = IsConnected ? intervalMs : 1000;
                    if (autoTimeSynchronizationStopSignal.Wait(waitMs))
                        return;
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
            }
            finally
            {
                lock (autoTimeSynchronizationLock)
                {
                    if (autoTimeSynchronizationThread == Thread.CurrentThread)
                    {
                        isAutoSynchronizingTime = false;
                        autoTimeSynchronizationThread = null;
                    }
                }
            }
        }

        /// <summary>
        /// Stops automatic server time synchronization.
        /// </summary>
        private void StopAutoSyncTime(bool waitForStop)
        {
            Thread threadToWait = null;
            lock (autoTimeSynchronizationLock)
            {
                if (!isAutoSynchronizingTime)
                    return;

                isAutoSynchronizingTime = false;
                autoTimeSynchronizationStopSignal.Set();
                threadToWait = autoTimeSynchronizationThread;
                autoTimeSynchronizationThread = null;
            }

            CancelTimeSynchronization();

            if (waitForStop && threadToWait != null && threadToWait != Thread.CurrentThread && threadToWait.IsAlive)
                threadToWait.Join(Math.Max(1000, TimeSynchronizationRequestTimeoutMs + 250));
        }

        /// <summary>
        /// Waits until one time synchronization generation completes or auto sync stops.
        /// </summary>
        private void WaitForTimeSynchronizationCompletion(int generation)
        {
            while (IsTimeSynchronizationActive(generation) && isAutoSynchronizingTime)
            {
                if (autoTimeSynchronizationStopSignal.Wait(25))
                {
                    CancelTimeSynchronization();
                    return;
                }
            }
        }

        /// <summary>
        /// Cancels the active time synchronization generation.
        /// </summary>
        private void CancelTimeSynchronization()
        {
            lock (timeSynchronizationLock)
            {
                if (isSynchronizingTime)
                {
                    isSynchronizingTime = false;
                    timeSynchronizationGeneration++;
                }
            }
        }

        /// <summary>
        /// Requests one server time sample.
        /// </summary>
        private bool TryRequestServerTimeSample(Func<float> getClientTime, int requestTimeoutMs, int generation, Action<string> onLog, out TimeSynchronizationSample sample)
        {
            sample = new TimeSynchronizationSample();
            if (Client == null || !IsConnected)
            {
                onLog?.Invoke("Time sync skipped: client is not connected.");
                return false;
            }

            using (ManualResetEventSlim received = new ManualResetEventSlim(false))
            {
                TimeSynchronizationSample receivedSample = new TimeSynchronizationSample();
                bool hasSample = false;
                Exception callbackException = null;
                float clientSendTime = getClientTime();
                Stopwatch roundTripWatch = Stopwatch.StartNew();
                uint replyID = 0;

                try
                {
                    NetworkMessage message = new NetworkMessage(NetSquareMessageID.ClientSynchronizeTime, ClientID)
                        .Set(HighPrecisionTimeSynchronizationVersion);
                    replyID = SendMessageWithReply(message, (reply) =>
                    {
                        try
                        {
                            double serverTime;
                            if (!TryReadServerTime(reply, out serverTime))
                            {
                                callbackException = new Exception("Invalid time synchronization reply.");
                                return;
                            }

                            float clientReceiveTime = getClientTime();
                            float measuredRoundTrip = (float)roundTripWatch.Elapsed.TotalSeconds;
                            float clientRoundTrip = clientReceiveTime - clientSendTime;
                            float midpointRoundTrip = clientRoundTrip > 0f ? clientRoundTrip : measuredRoundTrip;

                            receivedSample.Offset = (float)serverTime - (clientSendTime + midpointRoundTrip * 0.5f);
                            receivedSample.RoundTrip = measuredRoundTrip;
                            hasSample = true;
                        }
                        catch (Exception ex)
                        {
                            callbackException = ex;
                        }
                        finally
                        {
                            received.Set();
                        }
                    }, true);
                }
                catch (Exception ex)
                {
                    roundTripWatch.Stop();
                    callbackException = ex;
                    OnException?.Invoke(ex);
                    onLog?.Invoke("Time sync request failed: " + ex.Message);
                    return false;
                }

                Stopwatch waitWatch = Stopwatch.StartNew();
                bool replyReceived = false;
                while (waitWatch.ElapsedMilliseconds < requestTimeoutMs)
                {
                    if (received.Wait(25))
                    {
                        replyReceived = true;
                        break;
                    }

                    if (!IsTimeSynchronizationActive(generation))
                        break;
                }

                if (!replyReceived)
                {
                    roundTripWatch.Stop();
                    RemoveReplyCallback(replyID);
                    if (IsTimeSynchronizationActive(generation))
                        onLog?.Invoke("Time sync request timed out after " + requestTimeoutMs + " ms.");
                    return false;
                }

                roundTripWatch.Stop();
                if (!IsTimeSynchronizationActive(generation))
                    return false;

                if (callbackException != null)
                {
                    OnException?.Invoke(callbackException);
                    onLog?.Invoke("Time sync reply failed: " + callbackException.Message);
                    return false;
                }

                if (!hasSample)
                    return false;

                sample = receivedSample;
                return true;
            }
        }

        /// <summary>
        /// Reads server time from a time synchronization reply.
        /// </summary>
        private static bool TryReadServerTime(NetworkMessage reply, out double serverTime)
        {
            if (reply.Serializer.CanGetDouble())
            {
                serverTime = reply.Serializer.GetDouble();
                return true;
            }

            if (reply.Serializer.CanGetFloat())
            {
                serverTime = reply.Serializer.GetFloat();
                return true;
            }

            serverTime = 0d;
            return false;
        }

        /// <summary>
        /// Gets a stable offset from the lowest-latency samples.
        /// </summary>
        private static float GetFilteredServerTimeOffset(List<TimeSynchronizationSample> samples)
        {
            samples.Sort((a, b) => a.RoundTrip.CompareTo(b.RoundTrip));

            int count = Math.Max(1, (samples.Count + 1) / 2);
            float weightedOffset = 0f;
            float totalWeight = 0f;
            for (int i = 0; i < count; i++)
            {
                float weight = 1f / Math.Max(samples[i].RoundTrip, 0.0001f);
                weightedOffset += samples[i].Offset * weight;
                totalWeight += weight;
            }

            return weightedOffset / totalWeight;
        }

        /// <summary>
        /// Sleeps while allowing synchronization cancellation to be observed quickly.
        /// </summary>
        private bool SleepDuringTimeSynchronization(int generation, int delayMs)
        {
            int remainingMs = delayMs;
            while (remainingMs > 0)
            {
                if (!IsTimeSynchronizationActive(generation))
                    return false;

                int sleepMs = Math.Min(remainingMs, 25);
                Thread.Sleep(sleepMs);
                remainingMs -= sleepMs;
            }

            return IsTimeSynchronizationActive(generation);
        }

        /// <summary>
        /// Checks whether the current synchronization generation is still active.
        /// </summary>
        private bool IsTimeSynchronizationActive(int generation)
        {
            return isSynchronizingTime && generation == timeSynchronizationGeneration;
        }

        /// <summary>
        /// Finishes the active time synchronization generation.
        /// </summary>
        private void FinishTimeSynchronization(int generation)
        {
            lock (timeSynchronizationLock)
            {
                if (generation == timeSynchronizationGeneration)
                    isSynchronizingTime = false;
            }
        }

        /// <summary>
        /// Applies a new server time offset.
        /// </summary>
        /// <param name="offset">Offset to apply.</param>
        /// <param name="immediate">Whether the offset should be applied immediately.</param>
        private void SetServerTimeOffset(float offset, bool immediate)
        {
            TargetServerTimeOffset = offset;
            if (immediate || !SmoothServerTimeOffset || !hasServerTimeOffset)
                ServerTimeOffset = offset;

            DateTime now = DateTime.UtcNow;
            hasServerTimeOffset = true;
            lastServerTimeOffsetUpdateUtc = now;
            LastServerTimeSynchronizationUtc = now;
        }

        /// <summary>
        /// Smoothly moves the current server time offset toward the target offset.
        /// </summary>
        private void UpdateSmoothedServerTimeOffset()
        {
            if (!hasServerTimeOffset || !SmoothServerTimeOffset)
                return;

            DateTime now = DateTime.UtcNow;
            float deltaTime = (float)(now - lastServerTimeOffsetUpdateUtc).TotalSeconds;
            lastServerTimeOffsetUpdateUtc = now;

            if (deltaTime <= 0f)
                return;

            float t = 1f - (float)Math.Exp(-ServerTimeOffsetSmoothingSpeed * deltaTime);
            ServerTimeOffset += (TargetServerTimeOffset - ServerTimeOffset) * t;
            if (Math.Abs(TargetServerTimeOffset - ServerTimeOffset) < 0.0001f)
                ServerTimeOffset = TargetServerTimeOffset;
        }
        #endregion

        #region Public Utils
        /// <summary>
        /// Replace the client ID with a new one
        /// </summary>
        /// <param name="newID"> New ID to set</param>
        public void ReplaceClientID(uint newID)
        {
            Client.ID = newID;
            Client.UDP?.SendRegistration();
        }
        #endregion
    }
}
