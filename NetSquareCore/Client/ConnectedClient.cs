using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;

namespace NetSquare.Core
{
    public class ConnectedClient
    {
        // events
        public event Action<uint> OnDisconected;
        public event Action<NetworkMessage> OnMessageReceived;
        public event Action<byte[]> OnMessageSend;
        public event Action<Exception> OnException;
        // statistics
        public int NbMessagesToSend { get { return SendingQueue.Count + UDP?.NbSendingMessages ?? 0; } }
        private int nbMessagesSended;
        public int NbMessagesSended { get { return nbMessagesSended + UDP?.NbMessagesSended ?? 0; } }
        internal long sendedBytes = 0;
        internal long receivedBytes = 0;
        public long SendedBytes { get { return sendedBytes + UDP?.sendedBytes ?? 0; } set { sendedBytes = value; if (UDP != null) UDP.sendedBytes = value; } }
        public long ReceivedBytes { get { return receivedBytes + UDP?.receivedBytes ?? 0; } set { receivedBytes = value; if (UDP != null) UDP.receivedBytes = value; } }
        public long NbMessagesReceived { get; internal set; }
        // properties
        public uint ID { get; set; }
        public Socket TcpSocket { get; private set; }
        public bool UDPEnabled { get; set; }
        private ConcurrentQueue<byte[]> SendingQueue;
        private int receivingMessageLenght;
        private int receivingMessageReceived;
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
            receivingMessageBuffer = new byte[12];
            receivingLenghtMessageBuffer = new byte[4];
        }

        #region Utils
        public bool IsConnected()
        {
            if (TcpSocket.Poll(0, SelectMode.SelectRead))
            {
                byte[] buff = new byte[1];
                if (TcpSocket.Receive(buff, SocketFlags.Peek) == 0)
                    return false;
            }
            return true;
            //return !((TcpSocket.Poll(1000, SelectMode.SelectRead) && TcpSocket.Available == 0)/* || !TcpSocket.Connected*/);
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
            UDP?.SendMessage(msg);
        }

        /// <summary>
        /// enqueue an UDP message to send
        /// </summary>
        /// <param name="headID">headID of the message to send</param>
        /// <param name="msg">message to send</param>
        public void AddUDPMessage(ushort headID, byte[] msg)
        {
            UDP?.SendMessage(headID, msg);
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
        /// <param name="enableUDP">if true, netsquare will enable UDP for this connection</param>
        public void SetClient(Socket tcpClient, bool isClient, bool enableUDP)
        {
            TcpSocket = tcpClient;
            if (enableUDP)
            {
                UDP = new UDPConnection();
                if (isClient)
                    UDP.CreateClientConnection(this, tcpClient);
                else
                    UDP.CreateServerConnection(this, tcpClient);
                UDPEnabled = true;
            }

            nbMessagesSended = 0;

            //receivingArgs = new SocketAsyncEventArgs();
            //receivingArgs.RemoteEndPoint = TcpSocket.RemoteEndPoint;
            //receivingArgs.UserToken = TcpSocket;
            //receivingArgs.Completed += MessageDataReceived;

            //receivingLenghtArgs = new SocketAsyncEventArgs();
            //receivingArgs.RemoteEndPoint = TcpSocket.RemoteEndPoint;
            //receivingArgs.UserToken = TcpSocket;
            //receivingLenghtArgs.Completed += MessageLenghtReceived;
            //receivingLenghtArgs.SetBuffer(receivingLenghtMessageBuffer, 0, 4);

            //StartReceivingMessageLenght();
            StartR();
        }
        #endregion

        #region TCP
        // ==================================== Send
        private void SendMessage(byte[] message)
        {
            isSendingTCPMessage = true;
            try
            {
                sendedBytes += message.Length;
                OnMessageSend?.Invoke(message);
                TcpSocket.BeginSend(message, 0, message.Length, SocketFlags.None, OnMessageSended, null);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                // client disconnected
                if (ex is SocketException)
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
        private readonly object bufferLock = new object();
        private const int MaxBufferSize = 65536; // Adjust buffer size as needed
        private byte[] receiveBuffer = new byte[MaxBufferSize];
        private int receiveBufferLength = 0;

        public void StartR()
        {
            SendingQueue = new ConcurrentQueue<byte[]>();

            // Start a separate thread for processing received data
            Thread receiveThread = new Thread(ProcessReceivedData);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            StartReceivingData();
        }

        // Method to process received data asynchronously
        private void ProcessReceivedData()
        {
            while (true)
            {
                lock (bufferLock)
                {
                    // Check if there's enough data in the buffer to process a message
                    if (receiveBufferLength >= 4)
                    {
                        // Extract message length
                        int messageLength = BitConverter.ToInt32(receiveBuffer, 0);

                        // Check if we have received the entire message
                        if (receiveBufferLength >= messageLength)
                        {
                            // Construct message from buffer
                            byte[] messageData = new byte[messageLength];
                            Array.Copy(receiveBuffer, 0, messageData, 0, messageLength);

                            // Process message (e.g., raise event)
                            receivingMessageReceived = 0;
                            NbMessagesReceived++;
                            receivedBytes += messageLength;
                            receivingTCPMessage = new NetworkMessage();
                            receivingTCPMessage.Client = this;
                            receivingTCPMessage.SetData(messageData);
                            OnMessageReceived?.Invoke(receivingTCPMessage);
                            receivingTCPMessage = null;
                            // Remove processed message from buffer
                            Array.Copy(receiveBuffer, messageLength, receiveBuffer, 0, receiveBufferLength - messageLength);
                            receiveBufferLength -= (messageLength);
                        }
                        else
                        {
                            // Wait for more data to arrive
                            Monitor.Wait(bufferLock);
                        }
                    }
                    else
                    {
                        // Wait for more data to arrive
                        Monitor.Wait(bufferLock);
                    }
                }
            }
        }

        private void StartReceivingData()
        {
            try
            {
                SocketAsyncEventArgs receivingArgs = new SocketAsyncEventArgs();
                receivingArgs.RemoteEndPoint = TcpSocket.RemoteEndPoint;
                receivingArgs.UserToken = TcpSocket;
                receivingArgs.SetBuffer(receiveBuffer, receiveBufferLength, receiveBuffer.Length - receiveBufferLength);
                receivingArgs.Completed += MessageDataReceived; // Wire up the event handler

                if (!TcpSocket.ReceiveAsync(receivingArgs))
                {
                    // If the receive operation completed synchronously, handle it immediately
                    MessageDataReceived(this, receivingArgs);
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                if (ex is SocketException)
                {
                    OnDisconected?.Invoke(ID);
                }
            }
        }

        private void MessageDataReceived(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                lock (bufferLock)
                {
                    // Copy received data to the receive buffer
                    int bytesReceived = e.BytesTransferred;
                    if (bytesReceived > 0)
                    {
                        Array.Copy(e.Buffer, e.Offset, receiveBuffer, receiveBufferLength, bytesReceived);
                        receiveBufferLength += bytesReceived;

                        // Notify the processing thread that new data is available
                        Monitor.Pulse(bufferLock);
                    }
                }

                // Continue receiving data
                StartReceivingData();
            }
            catch (Exception ex)
            {
                // Handle exceptions
                OnException?.Invoke(ex);
                if (ex is SocketException)
                {
                    OnDisconected?.Invoke(ID);
                }
            }
        }

        /*
        private void StartReceivingMessageLenght()
        {
            try
            {
                receivingLenghtArgs.SetBuffer(receivingLenghtMessageBuffer, 0, 4);
                if (!TcpSocket.ReceiveAsync(receivingLenghtArgs)) // start receiving message into buffer, check if sync or async
                    MessageLenghtReceived(this, receivingLenghtArgs);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                // client disconnected
                if (ex is SocketException)
                    OnDisconected?.Invoke(ID);
            }
        }

        private void MessageLenghtReceived(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                receivingMessageLenght = BitConverter.ToInt32(receivingLenghtMessageBuffer, 0);
                if (receivingMessageLenght < 12)
                {
                    // if sync, check if don't receive anything and if socket is disconnected => check if 0 is because if not, don't need to check connection, and check connection is slow
                    if (receivingMessageLenght == 0 && !IsConnected())
                    {
                        OnDisconected?.Invoke(ID);
                        OnException?.Invoke(new Exception("msg lenght = " + receivingMessageLenght + ". disconnected"));
                    }
                    else
                    {
                        StartReceivingMessageLenght();
                        OnException?.Invoke(new Exception("Unexpected case just happen. msg lenght = " + receivingMessageLenght + " and client is still connected (client : " + ID + ")"));
                    }
                    return;
                }

                // no encryption, keep lenght into message
                receivingMessageBuffer = new byte[receivingMessageLenght];
                receivingMessageBuffer[0] = receivingLenghtMessageBuffer[0];
                receivingMessageBuffer[1] = receivingLenghtMessageBuffer[1];
                receivingMessageBuffer[2] = receivingLenghtMessageBuffer[2];
                receivingMessageBuffer[3] = receivingLenghtMessageBuffer[3];
                receivingMessageReceived = 4;
                receivingArgs.SetBuffer(receivingMessageBuffer, 4, receivingMessageLenght - 4);
                if (!TcpSocket.ReceiveAsync(receivingArgs))
                    MessageDataReceived(this, receivingArgs);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                // client disconnected
                if (ex is SocketException)
                    OnDisconected?.Invoke(ID);
            }
        }

        private void MessageDataReceived(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                // message not fully received
                receivingMessageReceived += e.BytesTransferred;
                // OnException?.Invoke(new Exception("this block : " + e.BytesTransferred + " , total : " + receivingMessageReceived + " , expected : " + (receivingMessageBuffer.Length - 4) + " | " + receivingMessageLenght));
                if (receivingMessageBuffer.Length > receivingMessageReceived)
                {
                    // we don't received anything, assume we are disconnected
                    if (receivingMessageReceived == 4)
                    {
                        OnDisconected?.Invoke(ID);
                        return;
                    }

                    // OnException?.Invoke(new Exception("inconsistent message block : " + receivingMessageReceived + " (" + e.BytesTransferred + ") / " + receivingMessageBuffer.Length));
                    receivingArgs.SetBuffer(receivingMessageBuffer, receivingMessageReceived, receivingMessageBuffer.Length - receivingMessageReceived);
                    if (!TcpSocket.ReceiveAsync(receivingArgs))
                        MessageDataReceived(this, receivingArgs);
                }
                // message fully received
                else
                {
                    receivingMessageReceived = 0;
                    NbMessagesReceived++;
                    receivedBytes += receivingMessageBuffer.Length;
                    receivingTCPMessage = new NetworkMessage();
                    receivingTCPMessage.Client = this;
                    receivingTCPMessage.SetData(receivingMessageBuffer);
                    OnMessageReceived?.Invoke(receivingTCPMessage);
                    receivingTCPMessage = null;
                    StartReceivingMessageLenght();
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                // client disconnected
                if (ex is SocketException)
                    OnDisconected?.Invoke(ID);
            }
        }*/
        #endregion
    }
}