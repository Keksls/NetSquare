using System;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace NetSquare.Core
{
    public class ConnectedClient
    {
        // events
        public event Action<uint> OnDisconected;
        // statistics
        public int NbMessagesToSend { get { return SendingQueue.Count + UDP.NbSendingMessages; } }
        private int nbMessagesSended;
        public int NbMessagesSended { get { return nbMessagesSended + UDP.NbMessagesSended; } }
        internal long sendedBytes = 0;
        internal long receivedBytes = 0;
        public long SendedBytes { get { return sendedBytes + UDP.sendedBytes; } set { sendedBytes = value; UDP.sendedBytes = value; } }
        public long ReceivedBytes { get { return receivedBytes + UDP.receivedBytes; } set { receivedBytes = value; UDP.receivedBytes = value; } }
        public long NbMessagesReceived { get; internal set; }
        // properties
        public uint ID { get; set; }
        public Socket TcpSocket { get; private set; }
        public event Action<NetworkMessage> OnMessageReceived;
        private ConcurrentQueue<byte[]> SendingQueue;
        private byte[] receivingMessageBuffer;
        private byte[] receivingLenghtMessageBuffer;
        private byte[] currentSendingTCPMessage;
        private NetworkMessage receivingTCPMessage;
        private bool isSendingTCPMessage = false;
        public UDPConnection UDP;
        private SocketAsyncEventArgs receivingArgs;
        private SocketAsyncEventArgs receivingLenghtArgs;

        public ConnectedClient()
        {
            SendingQueue = new ConcurrentQueue<byte[]>();
            receivingMessageBuffer = new byte[11];
            receivingLenghtMessageBuffer = new byte[2];
        }

        public bool IsConnected()
        {
            return !((TcpSocket.Poll(1000, SelectMode.SelectRead) && TcpSocket.Available == 0) || !TcpSocket.Connected);
        }

        /// <summary>
        /// enqueue a TCP message to send
        /// </summary>
        /// <param name="msg">message to send</param>
        public void AddTCPMessage(NetworkMessage msg)
        {
            AddTCPMessage(msg.Serialize());
        }

        /// <summary>
        /// enqueue a TCP message to send
        /// </summary>
        /// <param name="msg">message to send</param>
        public void AddTCPMessage(byte[] msg)
        {
            if (isSendingTCPMessage)
                SendingQueue.Enqueue(msg);
            else
                SendMessage(msg);
        }

        /// <summary>
        /// enqueue an UDP message to send
        /// </summary>
        /// <param name="msg">message to send</param>
        public void AddUDPMessage(NetworkMessage msg)
        {
            UDP.SendMessage(msg);
        }

        /// <summary>
        /// enqueue an UDP message to send
        /// </summary>
        /// <param name="headID">headID of the message to send</param>
        /// <param name="msg">message to send</param>
        public void AddUDPMessage(ushort headID, byte[] msg)
        {
            UDP.SendMessage(headID, msg);
        }

        /// <summary>
        /// event fiered when a message juste received
        /// </summary>
        /// <param name="message">message received</param>
        internal void Fire_OnMessageReceived(NetworkMessage message)
        {
            OnMessageReceived?.Invoke(message);
        }

        /// <summary>
        /// set tcp client and start UDP if necessary (used by NetSquare, don't use it yourself)
        /// </summary>
        /// <param name="tcpClient">TCP client</param>
        /// <param name="isClient">if true, invoked by netsquareClient, else by netSquare setver</param>
        public void SetClient(Socket tcpClient, bool isClient)
        {
            TcpSocket = tcpClient;
            UDP = new UDPConnection();
            if (isClient)
                UDP.CreateClientConnection(this, tcpClient);
            else
                UDP.CreateServerConnection(this, tcpClient);
            nbMessagesSended = 0;

            receivingArgs = new SocketAsyncEventArgs();
            receivingArgs.RemoteEndPoint = TcpSocket.RemoteEndPoint;
            receivingArgs.UserToken = TcpSocket;
            receivingArgs.Completed += MessageDataReceived;

            receivingLenghtArgs = new SocketAsyncEventArgs();
            receivingArgs.RemoteEndPoint = TcpSocket.RemoteEndPoint;
            receivingArgs.UserToken = TcpSocket;
            receivingLenghtArgs.Completed += MessageLenghtReceived;
            receivingLenghtArgs.SetBuffer(receivingLenghtMessageBuffer, 0, 2);

            StartReceivingMessageLenght();
        }

        private void MessageLenghtReceived(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                ushort msgLenght = BitConverter.ToUInt16(receivingLenghtMessageBuffer, 0);
                if (msgLenght < 10)
                {
                    // if sync, check if don't receive anything and is socket is disconnected => check if 0 is because if not, don't need to check connection, and check connection is slow
                    if (receivingLenghtMessageBuffer[0] == 0 && receivingLenghtMessageBuffer[1] == 0 && !IsConnected())
                        OnDisconected?.Invoke(ID);
                    else
                        StartReceivingMessageLenght();
                    return;
                }

                // no encryption, keep lenght into message | EDIT : ???
                receivingMessageBuffer = new byte[msgLenght];
                receivingMessageBuffer[0] = (byte)msgLenght;
                receivingMessageBuffer[1] = (byte)(msgLenght >> 8);
                receivingArgs.SetBuffer(receivingMessageBuffer, 2, msgLenght - 2);
                TcpSocket.ReceiveAsync(receivingArgs);
            }
            catch (Exception ex)
            {
                if (!(ex is SocketException))
                    Console.WriteLine(ex.ToString());
                // client disconnected
                OnDisconected?.Invoke(ID);
            }
        }

        private void MessageDataReceived(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                NbMessagesReceived++;
                receivedBytes += receivingMessageBuffer.Length;
                receivingTCPMessage = new NetworkMessage();
                receivingTCPMessage.Client = this;
                receivingTCPMessage.SetData(receivingMessageBuffer);
                OnMessageReceived?.Invoke(receivingTCPMessage);
                receivingTCPMessage = null;
                StartReceivingMessageLenght();
            }
            catch (Exception ex)
            {
                if (!(ex is SocketException))
                    Console.WriteLine(ex.ToString());
                // client disconnected
                OnDisconected?.Invoke(ID);
            }
        }

        #region TCP
        // ==================================== Send
        private void SendMessage(byte[] message)
        {
            isSendingTCPMessage = true;
            try
            {
                sendedBytes += message.Length;
                TcpSocket.BeginSend(message, 0, message.Length, SocketFlags.None, OnMessageSended, null);
            }
            catch (Exception ex)
            {
                if (!(ex is SocketException))
                    Console.WriteLine(ex.ToString());
                // client disconnected
                OnDisconected?.Invoke(ID);
            }
        }

        private void OnMessageSended(IAsyncResult res)
        {
            nbMessagesSended++;
            if (SendingQueue.Count > 0)
            {
                while (!SendingQueue.TryDequeue(out currentSendingTCPMessage))
                    continue;
                SendMessage(currentSendingTCPMessage);
            }
            else
                isSendingTCPMessage = false;
        }

        // ====================================== Receive
        private void StartReceivingMessageLenght()
        {
            try
            {
                receivingLenghtArgs.SetBuffer(receivingLenghtMessageBuffer, 0, 2);
                if (!TcpSocket.ReceiveAsync(receivingLenghtArgs)) // start receiving message into buffer, check if sync or async
                    MessageLenghtReceived(this, receivingArgs);
            }
            catch (Exception ex)
            {
                if (!(ex is SocketException))
                    Console.WriteLine(ex.ToString());
                OnDisconected?.Invoke(ID);
            }
        }
        #endregion
    }
}