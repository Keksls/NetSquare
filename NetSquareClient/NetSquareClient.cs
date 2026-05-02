using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        /// Stores the is synchronizing time value.
        /// </summary>
        private bool isSynchronizingTime = false;
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
        /// Stores whether server time was synchronized at least once.
        /// </summary>
        private bool hasServerTimeOffset;
        /// <summary>
        /// Stores the last server time offset update timestamp.
        /// </summary>
        private DateTime lastServerTimeOffsetUpdateUtc;
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
            lastServerTimeOffsetUpdateUtc = DateTime.UtcNow;
            if (autoBindNetsquareActions)
                Dispatcher.AutoBindHeadActionsFromAttributes();
            WorldsManager = new WorldsManager(this);

            // initiate Type Dictionnary
            typesDic = new Dictionary<Type, Action<NetworkMessage, object>>
            {
                { typeof(short), (message, item) => { message.Set((short)Convert.ChangeType(item, typeof(short))); } },
                { typeof(int), (message, item) => { message.Set((int)Convert.ChangeType(item, typeof(int))); } },
                { typeof(long), (message, item) => { message.Set((long)Convert.ChangeType(item, typeof(long))); } },
                { typeof(float), (message, item) => { message.Set((float)Convert.ChangeType(item, typeof(float))); } },
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
            isStarted = false;
            messagesAvailable.Release();
            OnDisconected?.Invoke();
            if (Client == null)
                return;
            Client.TcpSocket.Close();
            Client.TcpSocket.Dispose();
            Client = null;
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
        /// Loop that process the received messages
        /// </summary>
        private void ProcessMessagesLoop()
        {
            NetworkMessage message = null;
            isStarted = true;
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
            uint rplID = GetNextReplyID();
            msg.ReplyTo(rplID);
            replyCallBack[rplID] = callback;
            SendMessage(msg);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(ushort headID, NetSquareAction callback)
        {
            NetworkMessage msg = new NetworkMessage(headID);
            uint rplID = GetNextReplyID();
            msg.ReplyTo(rplID);
            replyCallBack[rplID] = callback;
            SendMessage(msg);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(Enum headID, NetSquareAction callback)
        {
            NetworkMessage msg = new NetworkMessage(headID);
            uint rplID = GetNextReplyID();
            msg.ReplyTo(rplID);
            replyCallBack[rplID] = callback;
            SendMessage(msg);
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
            Client.AddUDPMessage(msg.HeadID, msg.Serialize());
        }

        /// <summary>
        /// Send an empty message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessageUDP(ushort HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddUDPMessage(HeadID, msg.Serialize());
        }

        /// <summary>
        /// Send an empty message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessageUDP(Enum HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddUDPMessage(msg.HeadID, msg.Serialize());
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
            // clamp precision between 1 and 10
            precision = Math.Max(1, Math.Min(10, precision));

            // if already synchronizing, stop it
            if (isSynchronizingTime)
            {
                isSynchronizingTime = false;
                return;
            }
            isSynchronizingTime = true;

            // create array to store received times
            float[] clientTimeOffsets = new float[precision];
            bool[] receivedOffsets = new bool[precision];
            DateTime[] sendTimes = new DateTime[precision];

            // reset server time offset
            ServerTimeOffset = 0;
            TargetServerTimeOffset = 0;
            hasServerTimeOffset = false;

            // start sync thread
            Thread syncThread = new Thread(() =>
            {
                // iterate precision times
                for (int i = 0; i < precision; i++)
                {
                    int index = i;
                    sendTimes[index] = DateTime.Now;
                    // send sync message
                    SendMessage(new NetworkMessage(NetSquareMessageID.ClientSynchronizeTime, ClientID), (reply) =>
                    {
                        // get receive time
                        DateTime receiveTime = DateTime.Now;
                        float pingDuration = (float)(receiveTime - sendTimes[index]).TotalSeconds / 2f;

                        // get server time
                        float serverTime = reply.Serializer.GetFloat() + pingDuration;

                        // get client time
                        float clientTime = getClientTime();

                        // get client server time offset
                        float timeOffset = serverTime - clientTime;

                        // store time offset
                        clientTimeOffsets[index] = timeOffset;
                        receivedOffsets[index] = true;

                        // log everything
                        onLog?.Invoke($"Client time : {clientTime} | Server time : {serverTime} | Time offset : {timeOffset} | Ping : {pingDuration}");

                        // set time if first time
                        if (index == 0)
                        {
                            SetServerTimeOffset(clientTimeOffsets[0], true);
                            // invoke callback
                            onServerTimeGet?.Invoke(GetServerTime(getClientTime.Invoke()));
                        }
                    });

                    // wait for next sync
                    Thread.Sleep(timeBetweenSyncs);
                }

                // wait for all time to be received
                while (receivedOffsets.Any(e => !e))
                {
                    Thread.Sleep(10);
                }

                // calculate average time
                float avgTime = 0;
                for (int j = 0; j < precision; j++)
                    avgTime += clientTimeOffsets[j];
                onLog?.Invoke($"Cumul time offset : {avgTime}");
                avgTime /= precision;
                SetServerTimeOffset(avgTime, false);

                // log all clientTimeOffsets
                onLog?.Invoke($"Time offsets : {string.Join(" | ", clientTimeOffsets)}");

                // log average time
                onLog?.Invoke($"Average time offset : {avgTime}");
                // log client time
                onLog?.Invoke($"Client time : {getClientTime()} | Server time : {GetServerTime(getClientTime())}");

                // invoke callback
                onServerTimeGet?.Invoke(GetServerTime(getClientTime.Invoke()));

                // stop synchronizing
                isSynchronizingTime = false;
            });

            // start sync thread
            syncThread.IsBackground = true;
            syncThread.Start();
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

            hasServerTimeOffset = true;
            lastServerTimeOffsetUpdateUtc = DateTime.UtcNow;
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
        }
        #endregion
    }
}
