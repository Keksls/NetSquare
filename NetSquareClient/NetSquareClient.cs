using NetSquare.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareClient
{
    public class NetSquare_Client
    {
        #region Events
        public event Action OnDisconected;
        public event Action<uint> OnConnected;
        public event Action OnConnectionFail;
        public event Action<NetworkMessage> OnUnregisteredMessageReceived;
        #endregion

        #region Variables
        public NetSquareDispatcher Dispatcher;
        public WorldsManager WorldsManager { get; private set; }
        public ConnectedClient Client { get; private set; }
        public uint ClientID { get { return Client != null ? Client.ID : 0; } }
        public bool IsConnected { get { return Client.Socket.Connected; } }
        public int NbSendingMessages { get { return Client != null ? Client.NbMessagesToSend : 0; } }
        public int NbProcessingMessages { get { return messagesQueue.Count; } }
        private int nbReplyAsked = 1;
        private bool isStarted { get; set; }
        private ConcurrentQueue<NetworkMessage> messagesQueue = new ConcurrentQueue<NetworkMessage>();
        private Dictionary<int, NetSquareAction> replyCallBack = new Dictionary<int, NetSquareAction>();
        #endregion

        /// <summary>
        /// Instantiate a new NetSquare client
        /// </summary>
        public NetSquare_Client()
        {
            Dispatcher = new NetSquareDispatcher();
            Dispatcher.AutoBindHeadActionsFromAttributes();
            WorldsManager = new WorldsManager(this);
        }

        #region Connection / Disconnection
        /// <summary>
        /// Connect this client to the given NetSquare server IP and port
        /// </summary>
        /// <param name="hostNameOrIpAddress">HostName or IP Adress to connect on</param>
        /// <param name="port">Port to connect on</param>
        public void Connect(string hostNameOrIpAddress, int port)
        {
            try
            {
                TcpClient tcpClient = new TcpClient();
                tcpClient.Connect(hostNameOrIpAddress, port);

                // start routine that will validate server connection
                Thread runLoopThread = new Thread(() => { ValidateConnection(tcpClient); });
                runLoopThread.Start();
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
            Client.Socket.Close();
            Client.Socket.Dispose();
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
                else if (step == 1 && tcpClient.Available >= 4)
                {
                    byte[] array = new byte[4];
                    tcpClient.Client.Receive(array, 0, 4, SocketFlags.None);
                    uint clientID = BitConverter.ToUInt32(array, 0);
                    // let's reply server same ID as validation
                    Client = new ConnectedClient()
                    {
                        ID = clientID,
                        Socket = tcpClient.Client
                    };
                    Client.OnMessageReceived += Client_OnMessageReceived;
                    // start receiving message loop
                    Thread runLoopThread = new Thread(ReceivingSendingLoop);
                    runLoopThread.IsBackground = true;
                    runLoopThread.Start();
                    // start processing message loop
                    Thread processingThread = new Thread(ProcessMessagesLoop);
                    processingThread.IsBackground = true;
                    processingThread.Start();
                    OnConnected?.Invoke(clientID);
                    break;
                }
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
        /// Loop that receive and send messages from/to server
        /// </summary>
        private void ReceivingSendingLoop()
        {
            isStarted = true;
            while (isStarted)
            {
                try
                {
                    // Handle Disconnect
                    if (Client == null || !Client.Socket.Connected)
                    {
                        Disconnect();
                        return;
                    }

                    Client.ReceiveMessage();
                    Client.ProcessSendingQueue();
                }
                catch { }
                Thread.Sleep(1);
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
                    while (messagesQueue.TryDequeue(out message))
                    {
                        // reply message
                        if (replyCallBack.ContainsKey(message.TypeID))
                        {
                            Dispatcher.ExecuteinMainThread(replyCallBack[message.TypeID], message);
                            replyCallBack.Remove(message.TypeID);
                        }
                        // sync message
                        else if (message.TypeID == 2)
                        {
                            Dispatcher.ExecuteinMainThread((msg) =>
                            {
                                WorldsManager.Fire_OnSyncronize(msg);
                            }, message);
                        }
                        // default message, let's use dispatcher to handle it
                        else
                        {
                            if (!Dispatcher.DispatchMessage(message))
                                OnUnregisteredMessageReceived?.Invoke(message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        // catch arror if client not started in console env
                        Console.WriteLine(ex.ToString());
                    }
                    catch { }
                }
                Thread.Sleep(1);
            }
        }
        #endregion

        #region Sending messages
        /// <summary>
        /// Send a message to server without waiting for response
        /// </summary>
        /// <param name="msg">message to send</param>
        public void SendMessage(NetworkMessage msg)
        {
            msg.ClientID = Client.ID;
            Client.AddMessage(msg);
        }

        /// <summary>
        /// Send an empty message to server without waiting for response
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessage(ushort HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddMessage(msg);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="msg">message to send</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(NetworkMessage msg, NetSquareAction callback)
        {
            msg.ReplyTo(nbReplyAsked);
            nbReplyAsked++;
            replyCallBack.Add(msg.TypeID, callback);
            SendMessage(msg);
        }
        #endregion
    }
}