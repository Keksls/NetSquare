using NetSquare.Core;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareClient
{
    public class NetSquare_Client
    {
        public event Action Disconected;
        public event Action<uint> Connected;
        public event Action ConnectionFail;
        public event Action<NetworkMessage> UnregisteredMessageReceived;
        public uint ClientID { get; private set; }
        public NetSquareDispatcher Dispatcher;
        private TcpClient TcpClient { get; set; }
        private int NbReplyAsked = 0;
        private bool connected = false;
        private bool QueueStop { get; set; }
        private static readonly Dictionary<int, Action<NetworkMessage>> ReplyCallBack = new Dictionary<int, Action<NetworkMessage>>();

        public NetSquare_Client()
        {
            ClientID = 0;
            Dispatcher = new NetSquareDispatcher();
            Dispatcher.AutoBindHeadActionsFromAttributes();
        }

        public void Connect(string hostNameOrIpAddress, int port)
        {
            TcpClient = new TcpClient();
            TcpClient.Connect(hostNameOrIpAddress, port);

            // start routine that will validate server connection
            Thread runLoopThread = new Thread(ValidateConnection);
            runLoopThread.Start();
        }

        public void Disconnect()
        {
            if (TcpClient == null)
                return;
            TcpClient.Close();
            TcpClient.Dispose();
            TcpClient = null;
        }

        void ValidateConnection()
        {
            long timeEnd = DateTime.Now.AddSeconds(30).Ticks;
            int step = 0;
            int key = 0;
            while (TcpClient != null && TcpClient.Connected && DateTime.Now.Ticks < timeEnd)
            {
                // Handle Byte Avaliable
                if (step == 0 && TcpClient.Available >= 8)
                {
                    byte[] array = new byte[8];
                    TcpClient.Client.Receive(array, 0, 8, SocketFlags.None);
                    int rnd1 = BitConverter.ToInt32(array, 0);
                    int rnd2 = BitConverter.ToInt32(array, 4);
                    key = HandShake.GetKey(rnd1, rnd2);
                    byte[] rep = BitConverter.GetBytes(key);
                    TcpClient.GetStream().Write(rep, 0, rep.Length);
                    step = 1;
                }
                else if (step == 1 && TcpClient.Available >= 4)
                {
                    byte[] array = new byte[4];
                    TcpClient.Client.Receive(array, 0, 4, SocketFlags.None);
                    uint clientID = BitConverter.ToUInt32(array, 0);
                    // let's reply server same ID as validation
                    ClientID = clientID;
                    // start main receiving message loop
                    Thread runLoopThread = new Thread(RunLoopStep);
                    runLoopThread.Start();
                    Connected?.Invoke(clientID);
                    break;
                }
            }
            if (ClientID == 0)
                ConnectionFail?.Invoke();
        }

        void RunLoopStep()
        {
            byte[] bytesReceived = new byte[0];
            int currentLenght = -1;
            int nbBytesReceived = 0;
            while (!QueueStop)
            {
                try
                {
                    // Handle Disconenction
                    if (TcpClient == null || !TcpClient.Connected)
                    {
                        if (Disconected != null && connected)
                            Disconected?.Invoke();
                        return;
                    }
                    connected = TcpClient.Connected;

                    // Handle message lenght
                    if (TcpClient != null && currentLenght == -1 && TcpClient.Available >= 4)
                    {
                        byte[] lenthArray = new byte[4];
                        TcpClient.Client.Receive(lenthArray, 0, 4, SocketFlags.None);
                        currentLenght = BitConverter.ToInt32(lenthArray, 0);
                        bytesReceived = new byte[currentLenght];
                        nbBytesReceived = 0;
                    }

                    // handle receive message data
                    while (currentLenght != -1 && TcpClient != null && TcpClient.Available > 0 && TcpClient.Connected)
                    {
                        TcpClient.Client.Receive(bytesReceived, nbBytesReceived, 1, SocketFlags.None);
                        nbBytesReceived++;

                        // all message recieved
                        if (nbBytesReceived == currentLenght)
                        {
                            currentLenght = -1;
                            ProcessMessages(new NetworkMessage(bytesReceived));
                        }
                    }
                }
                catch (Exception ex)
                {
                    
                }
                Thread.Sleep(1);
            }
        }

        public void ProcessMessages(NetworkMessage message)
        {
            if (message.ReplyID != 0 && ReplyCallBack.ContainsKey(message.ReplyID))
            {
                ReplyCallBack[message.ReplyID](message);
                ReplyCallBack.Remove(message.ReplyID);
            }
            else
            {
                if (!Dispatcher.DispatchMessage(message))
                    UnregisteredMessageReceived?.Invoke(message);
            }
        }

        /// <summary>
        /// Send a message to server without waiting for response
        /// </summary>
        /// <param name="msg">message to send</param>
        public void SendMessage(NetworkMessage msg)
        {
            msg.ClientID = ClientID;
            byte[] data = msg.Serialize();
            TcpClient.GetStream().Write(data, 0, data.Length);
        }

        /// <summary>
        /// Send an empty message to server without waiting for response
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessage(ushort HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = ClientID;
            byte[] data = msg.Serialize();
            TcpClient.GetStream().Write(data, 0, data.Length);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="msg">message to send</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(NetworkMessage msg, Action<NetworkMessage> callback)
        {
            msg.ReplyTo(NbReplyAsked);
            ReplyCallBack.Add(msg.ReplyID, callback);
            NbReplyAsked++;
            SendMessage(msg);
        }
    }
}