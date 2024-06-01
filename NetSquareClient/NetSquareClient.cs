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
    public class NetSquareClient
    {
        #region Events
        public event Action OnDisconected;
        public event Action<uint> OnConnected;
        public event Action OnConnectionFail;
        public event Action<NetworkMessage> OnUnregisteredMessageReceived;
        public event Action<Exception> OnException;
        #endregion

        #region Variables
        public NetSquareDispatcher Dispatcher;
        public WorldsManager WorldsManager { get; private set; }
        public ConnectedClient Client { get; private set; }
        public int Port { get; private set; }
        public string IPAdress { get; private set; }
        public uint ClientID { get { return Client != null ? Client.ID : 0; } }
        public bool IsConnected { get { return Client?.TcpSocket?.Connected ?? false; } }
        public int NbSendingMessages { get { return Client != null ? Client.NbMessagesToSend : 0; } }
        public int NbProcessingMessages { get { return messagesQueue.Count; } }
        public NetSquareProtocoleType ProtocoleType;
        public float Time { get { return (float)(DateTime.Now.Ticks - serverTimeOffset) / (float)TimeSpan.TicksPerSecond; } }
        public bool IsTimeSynchonized { get { return serverTimeOffset != 0f; } }
        private bool isSynchronizingTime = false;
        public long serverTimeOffset;
        private uint nbReplyAsked = 1;
        private bool isStarted;
        private ConcurrentQueue<NetworkMessage> messagesQueue = new ConcurrentQueue<NetworkMessage>();
        private Dictionary<uint, NetSquareAction> replyCallBack = new Dictionary<uint, NetSquareAction>();
        private static Dictionary<Type, Action<NetworkMessage, object>> typesDic;
        #endregion

        /// <summary>
        /// Instantiate a new NetSquare client
        /// </summary>
        public NetSquareClient(bool autoBindNetsquareActions = true)
        {
            Dispatcher = new NetSquareDispatcher();
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
        /// Disconnect this client from server
        /// </summary>
        public void Disconnect()
        {
            isStarted = false;
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
                                if (replyCallBack.ContainsKey(message.ReplyID))
                                {
                                    Dispatcher.ExecuteinMainThread(replyCallBack[message.ReplyID], message);
                                    replyCallBack.Remove(message.ReplyID);
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
                Thread.Sleep(1);
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
            lock (replyCallBack)
            {
                msg.ReplyTo(nbReplyAsked);
                nbReplyAsked++;
                uint rplID = msg.ReplyID;
                replyCallBack.Add(rplID, callback);
            }
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
            lock (replyCallBack)
            {
                msg.ReplyTo(nbReplyAsked);
                nbReplyAsked++;
                uint rplID = msg.ReplyID;
                replyCallBack.Add(rplID, callback);
            }
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
            lock (replyCallBack)
            {
                msg.ReplyTo(nbReplyAsked);
                nbReplyAsked++;
                uint rplID = msg.ReplyID;
                replyCallBack.Add(rplID, callback);
            }
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
        /// Synchronize time with server. The more precision, the more time it will take to synchronize
        /// </summary>
        /// <param name="precision">1 to 10, 1 is the less precise, 10 is the most precise</param>
        /// <param name="timeBetweenSyncs">Time between each sync in milliseconds</param>
        /// <param name="onServerTimeGet">Callback</param>
        public void SyncTime(int precision, int timeBetweenSyncs = 1000, Action<float> onServerTimeGet = null)
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
            long[] clientTimeOffsets = new long[precision];

            // reset server time offset
            serverTimeOffset = 0;

            // start sync thread
            Thread syncThread = new Thread(() =>
            {
                // iterate precision times
                for (int i = 0; i < precision; i++)
                {
                    DateTime sendTime = DateTime.Now;
                    // send sync message
                    SendMessage(new NetworkMessage(NetSquareMessageID.ClientSynchronizeTime, ClientID), (reply) =>
                    {
                        // get receive time
                        DateTime receiveTime = DateTime.Now;
                        TimeSpan timeDifference = receiveTime - sendTime;
                        float pingDuration = (float)timeDifference.TotalSeconds / 2;

                        // get server time
                        float serverTime = reply.Serializer.GetFloat();

                        // get server client time offset
                        long timeOffset = receiveTime.Ticks - (long)(serverTime * TimeSpan.TicksPerSecond) + (long)(pingDuration * TimeSpan.TicksPerSecond);
                        clientTimeOffsets[i] = timeOffset;

                        // set time if first time
                        if (i == 0)
                        {
                            serverTimeOffset = clientTimeOffsets[0];
                            // invoke callback
                            onServerTimeGet?.Invoke(Time);
                        }
                    });

                    // wait for next sync
                    Thread.Sleep(timeBetweenSyncs);
                }

                // wait for all time to be received
                while (clientTimeOffsets[precision - 1] == 0)
                {
                    Thread.Sleep(10);
                }

                // calculate average time
                long avgTime = 0;
                for (int j = 0; j < precision; j++)
                    avgTime += clientTimeOffsets[j];
                avgTime /= precision;
                serverTimeOffset = avgTime;

                // invoke callback
                onServerTimeGet?.Invoke(Time);

                // stop synchronizing
                isSynchronizingTime = false;
            });

            // start sync thread
            syncThread.IsBackground = true;
            syncThread.Start();
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