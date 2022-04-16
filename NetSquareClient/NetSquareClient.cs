using NetSquare.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
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
        public int Port { get; private set; }
        public string IPAdress { get; private set; }
        public uint ClientID { get { return Client != null ? Client.ID : 0; } }
        public bool IsConnected { get { return Client.TcpSocket.Connected; } }
        public int NbSendingMessages { get { return Client != null ? Client.NbMessagesToSend : 0; } }
        public int NbProcessingMessages { get { return messagesQueue.Count; } }
        public eProtocoleType ProtocoleType;
        private int nbReplyAsked = 1;
        private bool isStarted { get; set; }
        private ConcurrentQueue<NetworkMessage> messagesQueue = new ConcurrentQueue<NetworkMessage>();
        private Dictionary<int, NetSquareAction> replyCallBack = new Dictionary<int, NetSquareAction>();
        private ConcurrentQueue<byte[]> udpSendingQueue = new ConcurrentQueue<byte[]>();
        private byte[] currentSendingUDPMessage;
        private NetworkMessage receivingUDPMessage;
        private bool isSendingMessage = false;
        private UdpClient UdpClient;
        private IPEndPoint serverEndPoint;
        #endregion

        /// <summary>
        /// Instantiate a new NetSquare client
        /// </summary>
        public NetSquare_Client(eProtocoleType protocoleType = eProtocoleType.TCP_AND_UDP)
        {
            ProtocoleType = protocoleType;
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
                Port = port;
                IPAdress = hostNameOrIpAddress;
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
                else if (step == 1 && tcpClient.Available >= 4)
                {
                    byte[] array = new byte[4];
                    tcpClient.Client.Receive(array, 0, 4, SocketFlags.None);
                    uint clientID = BitConverter.ToUInt32(array, 0);
                    // let's reply server same ID as validation
                    Client = new ConnectedClient()
                    {
                        ID = clientID
                    };
                    Client.SetClient(tcpClient.Client);
                    // start udp client
                    if (ProtocoleType == eProtocoleType.TCP_AND_UDP)
                    {
                        UdpClient = new UdpClient();
                        serverEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
                        UdpClient.Connect(IPAdress, Port + 1);
                        StartReceiveUDPMessage();
                    }
                    Client.OnMessageReceived += Client_OnMessageReceived;
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

        #region Sending messages UDP
        /// <summary>
        /// Send a message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="msg">message to send</param>
        public void SendMessageUDP(NetworkMessage msg)
        {
            msg.ClientID = Client.ID;
            SendOrEnqueueUDPMessage(msg.Serialize());
        }

        /// <summary>
        /// Send an empty message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessageUDP(ushort HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            SendOrEnqueueUDPMessage(msg.Serialize());
        }

        /// <summary>
        /// Send a message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="msg">message to send</param>
        private void SendOrEnqueueUDPMessage(byte[] msg)
        {
            if (isSendingMessage || udpSendingQueue.Count > 0)
                udpSendingQueue.Enqueue(msg);
            else
                SendUDPMessage(msg);
        }
        #endregion

        #region UDP
        private void SendUDPMessage(byte[] message)
        {
            isSendingMessage = true;
            UdpClient.BeginSend(message, message.Length, UDPMessageSended, UdpClient);
        }

        private void UDPMessageSended(IAsyncResult res)
        {
            UdpClient.EndSend(res);

            if (udpSendingQueue.Count > 0)
            {
                while (!udpSendingQueue.TryDequeue(out currentSendingUDPMessage))
                    continue;
                SendUDPMessage(currentSendingUDPMessage);
            }
            else
                isSendingMessage = false;
        }
        public void StartReceiveUDPMessage()
        {
            UdpClient.BeginReceive(UDPMessageDataReceived, UdpClient);
        }

        private void UDPMessageDataReceived(IAsyncResult res)
        {
            try
            {
                byte[] datagram = UdpClient.EndReceive(res, ref serverEndPoint);
                receivingUDPMessage = new NetworkMessage();
                if (receivingUDPMessage.SafeSetDatagram(datagram))
                    Client_OnMessageReceived(receivingUDPMessage);
                receivingUDPMessage = null;
                StartReceiveUDPMessage();
            }
            catch (SocketException)
            {
                // client disconnected
            }
        }
        #endregion
    }
}