using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;

namespace NetSquare.Core
{
    /// <summary>
    /// Represents the connected client component.
    /// </summary>
    public class ConnectedClient
    {
        /// <summary>
        /// Defines the min tcp message size constant.
        /// </summary>
        public const int MinTcpMessageSize = 10;
        /// <summary>
        /// Stores the max tcp message size value.
        /// </summary>
        public static int MaxTcpMessageSize = 16 * 1024 * 1024;
        /// <summary>
        /// Stores the max tcp queued messages value.
        /// </summary>
        public static int MaxTcpQueuedMessages = 65536;
        /// <summary>
        /// Stores the max tcp queued bytes value.
        /// </summary>
        public static long MaxTcpQueuedBytes = 64L * 1024L * 1024L;

        // events
        /// <summary>
        /// Occurs when disconected is raised.
        /// </summary>
        public event Action<uint> OnDisconected;
        /// <summary>
        /// Occurs when message received is raised.
        /// </summary>
        public event Action<NetworkMessage> OnMessageReceived;
        /// <summary>
        /// Occurs when message send is raised.
        /// </summary>
        public event Action<byte[]> OnMessageSend;
        /// <summary>
        /// Occurs when exception is raised.
        /// </summary>
        public event Action<Exception> OnException;
        // statistics
        /// <summary>
        /// Gets or sets the nb messages to send value.
        /// </summary>
        public int NbMessagesToSend { get { return queuedTcpMessages + (currentSendingTCPMessage != null ? 1 : 0) + (UDP?.NbSendingMessages ?? 0); } }
        /// <summary>
        /// Gets or sets the nb tcp messages to send value.
        /// </summary>
        public int NbTCPMessagesToSend { get { return Volatile.Read(ref queuedTcpMessages) + (currentSendingTCPMessage != null ? 1 : 0); } }
        /// <summary>
        /// Stores the nb messages sended value.
        /// </summary>
        private int nbMessagesSended;
        /// <summary>
        /// Gets or sets the nb messages sended value.
        /// </summary>
        public int NbMessagesSended { get { return nbMessagesSended + (UDP?.NbMessagesSended ?? 0); } }
        /// <summary>
        /// Stores the nb messages dropped value.
        /// </summary>
        private long nbMessagesDropped;
        /// <summary>
        /// Gets or sets the nb messages dropped value.
        /// </summary>
        public long NbMessagesDropped { get { return Interlocked.Read(ref nbMessagesDropped) + (UDP?.NbMessagesDropped ?? 0); } }
        /// <summary>
        /// Stores the sended bytes value.
        /// </summary>
        internal long sendedBytes = 0;
        /// <summary>
        /// Stores the received bytes value.
        /// </summary>
        internal long receivedBytes = 0;
        /// <summary>
        /// Gets or sets the sended bytes value.
        /// </summary>
        public long SendedBytes { get { return sendedBytes + (UDP?.sendedBytes ?? 0); } set { sendedBytes = value; if (UDP != null) UDP.sendedBytes = value; } }
        /// <summary>
        /// Gets or sets the received bytes value.
        /// </summary>
        public long ReceivedBytes { get { return receivedBytes + (UDP?.receivedBytes ?? 0); } set { receivedBytes = value; if (UDP != null) UDP.receivedBytes = value; } }
        /// <summary>
        /// Gets or sets the nb messages received value.
        /// </summary>
        public long NbMessagesReceived { get; internal set; }
        // properties
        /// <summary>
        /// Gets or sets the id value.
        /// </summary>
        public uint ID { get; set; }
        /// <summary>
        /// Gets or sets the tcp socket value.
        /// </summary>
        public Socket TcpSocket { get; private set; }
        /// <summary>
        /// Gets or sets the udp enabled value.
        /// </summary>
        public bool UDPEnabled { get; set; }
        /// <summary>
        /// Stores the sending queue value.
        /// </summary>
        private ConcurrentQueue<PooledByteBuffer> SendingQueue;
        /// <summary>
        /// Stores the queued tcp messages value.
        /// </summary>
        private int queuedTcpMessages;
        /// <summary>
        /// Stores the queued tcp bytes value.
        /// </summary>
        private long queuedTcpBytes;
        /// <summary>
        /// Stores the receiving message lenght value.
        /// </summary>
        private int receivingMessageLenght;
        /// <summary>
        /// Stores the receiving message received value.
        /// </summary>
        private int receivingMessageReceived;
        /// <summary>
        /// Stores the receiving message buffer value.
        /// </summary>
        private byte[] receivingMessageBuffer;
        /// <summary>
        /// Stores the receiving lenght message buffer value.
        /// </summary>
        private byte[] receivingLenghtMessageBuffer;
        /// <summary>
        /// Stores the connection probe buffer value.
        /// </summary>
        private readonly byte[] connectionProbeBuffer = new byte[1];
        /// <summary>
        /// Stores the current sending tcp message value.
        /// </summary>
        private PooledByteBuffer currentSendingTCPMessage;
        /// <summary>
        /// Stores the receiving tcp message value.
        /// </summary>
        private NetworkMessage receivingTCPMessage;
        /// <summary>
        /// Stores the is sending tcp message value.
        /// </summary>
        private int isSendingTCPMessage = 0;
        /// <summary>
        /// Stores the udp value.
        /// </summary>
        public UDPConnection UDP;
        /// <summary>
        /// Stores the receiving args value.
        /// </summary>
        private SocketAsyncEventArgs receivingArgs;
        /// <summary>
        /// Stores the receiving lenght args value.
        /// </summary>
        private SocketAsyncEventArgs receivingLenghtArgs;

        /// <summary>
        /// Initializes a new instance of the connected client class.
        /// </summary>
        public ConnectedClient()
        {
            SendingQueue = new ConcurrentQueue<PooledByteBuffer>();
            receivingMessageBuffer = new byte[12];
            receivingLenghtMessageBuffer = new byte[4];
        }

        #region Utils
        /// <summary>
        /// check if the client is connected
        /// </summary>
        /// <returns> true if the client is connected, else false</returns>
        public bool IsConnected()
        {
            Socket socket = TcpSocket;
            if (socket == null)
                return false;

            try
            {
                if (!socket.Connected)
                    return false;

                if (socket.Poll(0, SelectMode.SelectRead))
                {
                    if (socket.Receive(connectionProbeBuffer, SocketFlags.Peek) == 0)
                        return false;
                }

                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            //return !((TcpSocket.Poll(1000, SelectMode.SelectRead) && TcpSocket.Available == 0)/* || !TcpSocket.Connected*/);
        }

        /// <summary>
        /// enqueue a TCP message to send
        /// </summary>
        /// <param name="msg">message to send</param>
        public void AddTCPMessage(NetworkMessage msg)
        {
            AddTCPMessage(msg.SerializePooled());
        }

        /// <summary>
        /// Enqueue a TCP message and wait until pending TCP messages are sent.
        /// </summary>
        /// <param name="msg">message to send</param>
        /// <param name="timeoutMs">maximum wait time in milliseconds</param>
        /// <returns>true if the TCP queue was drained before the timeout</returns>
        public bool AddTCPMessageAndWait(NetworkMessage msg, int timeoutMs)
        {
            AddTCPMessage(msg);
            return WaitForPendingTCPMessages(timeoutMs);
        }

        /// <summary>
        /// Wait until pending TCP messages are sent.
        /// </summary>
        /// <param name="timeoutMs">maximum wait time in milliseconds</param>
        /// <returns>true if the TCP queue was drained before the timeout</returns>
        public bool WaitForPendingTCPMessages(int timeoutMs)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (HasPendingTCPMessages())
            {
                if (timeoutMs >= 0 && stopwatch.ElapsedMilliseconds >= timeoutMs)
                    return false;

                Thread.Sleep(1);
            }
            return true;
        }

        /// <summary>
        /// Check if the TCP send pump still has work to finish.
        /// </summary>
        /// <returns>true if TCP messages are pending</returns>
        private bool HasPendingTCPMessages()
        {
            return NbTCPMessagesToSend > 0 || Volatile.Read(ref isSendingTCPMessage) != 0;
        }

        /// <summary>
        /// enqueue a TCP message to send
        /// </summary>
        /// <param name="msg">message to send</param>
        public void AddTCPMessage(byte[] msg)
        {
            if (msg == null || msg.Length == 0)
                return;

            AddTCPMessage(PooledByteBuffer.Wrap(msg));
        }

        /// <summary>
        /// Executes the add tcp message operation.
        /// </summary>
        private void AddTCPMessage(PooledByteBuffer msg)
        {
            if (msg == null || msg.Buffer == null || msg.Length == 0)
                return;

            int queuedMessages = Interlocked.Increment(ref queuedTcpMessages);
            long queuedBytes = Interlocked.Add(ref queuedTcpBytes, msg.Length);
            if (queuedMessages > MaxTcpQueuedMessages || queuedBytes > MaxTcpQueuedBytes)
            {
                Interlocked.Decrement(ref queuedTcpMessages);
                Interlocked.Add(ref queuedTcpBytes, -msg.Length);
                Interlocked.Increment(ref nbMessagesDropped);
                msg.Dispose();
                OnException?.Invoke(new InvalidOperationException("TCP send queue overflow for client " + ID));
                try { TcpSocket?.Close(); } catch { }
                OnDisconected?.Invoke(ID);
                return;
            }

            SendingQueue.Enqueue(msg);
            TryStartSending();
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
        /// <param name="isClient">if true, invoked by NetSquare.Client, else by netSquare setver</param>
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

            receivingArgs = new SocketAsyncEventArgs();
            receivingArgs.RemoteEndPoint = TcpSocket.RemoteEndPoint;
            receivingArgs.UserToken = TcpSocket;
            receivingArgs.Completed += MessageDataReceived;

            receivingLenghtArgs = new SocketAsyncEventArgs();
            receivingLenghtArgs.RemoteEndPoint = TcpSocket.RemoteEndPoint;
            receivingLenghtArgs.UserToken = TcpSocket;
            receivingLenghtArgs.Completed += MessageLenghtReceived;
            receivingLenghtArgs.SetBuffer(receivingLenghtMessageBuffer, 0, 4);

            StartReceivingMessageLenght();
            //StartR();
        }
        #endregion

        #region TCP
        // ==================================== Send
        /// <summary>
        /// Executes the try start sending operation.
        /// </summary>
        private void TryStartSending()
        {
            if (Interlocked.CompareExchange(ref isSendingTCPMessage, 1, 0) != 0)
                return;

            ThreadPool.QueueUserWorkItem(_ => SendQueuedMessagesLoop());
        }

        /// <summary>
        /// Executes the send queued messages loop operation.
        /// </summary>
        private void SendQueuedMessagesLoop()
        {
            try
            {
                while (true)
                {
                    PooledByteBuffer nextMessage;
                    if (!SendingQueue.TryDequeue(out nextMessage))
                    {
                        Interlocked.Exchange(ref isSendingTCPMessage, 0);
                        if (SendingQueue.IsEmpty || Interlocked.CompareExchange(ref isSendingTCPMessage, 1, 0) != 0)
                            return;

                        continue;
                    }

                    Interlocked.Decrement(ref queuedTcpMessages);
                    Interlocked.Add(ref queuedTcpBytes, -nextMessage.Length);
                    SendQueuedMessage(nextMessage);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref isSendingTCPMessage, 0);
                DisposeCurrentSendingMessage();
                DrainSendingQueue();
                OnException?.Invoke(ex);
                if (ex is SocketException || ex is ObjectDisposedException)
                    OnDisconected?.Invoke(ID);
            }
        }

        /// <summary>
        /// Executes the send queued message operation.
        /// </summary>
        private void SendQueuedMessage(PooledByteBuffer message)
        {
            currentSendingTCPMessage = message;
            sendedBytes += message.Length;

            Action<byte[]> onMessageSend = OnMessageSend;
            if (onMessageSend != null)
            {
                byte[] sentData = message.Buffer;
                if (message.Length != sentData.Length)
                {
                    sentData = new byte[message.Length];
                    Buffer.BlockCopy(message.Buffer, 0, sentData, 0, message.Length);
                }
                try { onMessageSend(sentData); }
                catch (Exception ex) { OnException?.Invoke(ex); }
            }

            int offset = 0;
            while (offset < message.Length)
            {
                int sent = TcpSocket.Send(message.Buffer, offset, message.Length - offset, SocketFlags.None);
                if (sent <= 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                offset += sent;
            }

            nbMessagesSended++;
            DisposeCurrentSendingMessage();
        }

        /// <summary>
        /// Executes the dispose current sending message operation.
        /// </summary>
        private void DisposeCurrentSendingMessage()
        {
            PooledByteBuffer message = currentSendingTCPMessage;
            currentSendingTCPMessage = null;
            message?.Dispose();
        }

        /// <summary>
        /// Executes the drain sending queue operation.
        /// </summary>
        private void DrainSendingQueue()
        {
            PooledByteBuffer queuedMessage;
            while (SendingQueue.TryDequeue(out queuedMessage))
            {
                Interlocked.Decrement(ref queuedTcpMessages);
                Interlocked.Add(ref queuedTcpBytes, -queuedMessage.Length);
                queuedMessage.Dispose();
            }
        }

        // ====================================== Receive
        //private readonly object bufferLock = new object();
        //private const int MaxBufferSize = 65536; // Adjust buffer size as needed
        //private byte[] receiveBuffer = new byte[MaxBufferSize];
        //private int receiveBufferLength = 0;

        /*  public void StartR()
          {
              SendingQueue = new ConcurrentQueue<byte[]>();

              // Start a separate thread for processing received data
              Thread receiveThread = new Thread(ProcessReceivedData);
              receiveThread.IsBackground = true;
              receiveThread.Start();
              StartReceivingData();
          }

          // Method to process received data asynchronously
          /// <summary>
          /// Executes the process received data operation.
          /// </summary>
          private void ProcessReceivedData()
          {
              while (true)
              {
                  lock (bufferLock)
                  {
                      // Check if there's enough data in the buffer to process a message
                      while (receiveBufferLength >= 4)
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
                      // Wait for more data to arrive
                      Monitor.Wait(bufferLock);
                  }
              }
          }

          /// <summary>
          /// Executes the start receiving data operation.
          /// </summary>
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
                      if (TcpSocket.Connected)
                      {
                          // If the receive operation completed synchronously, handle it immediately
                          MessageDataReceived(this, receivingArgs);
                      }
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

          /// <summary>
          /// Executes the message data received operation.
          /// </summary>
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
          }*/

        /// <summary>
        /// Executes the start receiving message lenght operation.
        /// </summary>
        private void StartReceivingMessageLenght()
        {
            try
            {
                receivingMessageReceived = 0;
                receivingLenghtArgs.SetBuffer(receivingLenghtMessageBuffer, 0, receivingLenghtMessageBuffer.Length);
                if (!TcpSocket.ReceiveAsync(receivingLenghtArgs)) // start receiving message into buffer, check if sync or async
                    QueueMessageLenghtReceived();
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                // client disconnected
                if (ex is SocketException || ex is ObjectDisposedException)
                    OnDisconected?.Invoke(ID);
            }
        }

        /// <summary>
        /// Executes the message lenght received operation.
        /// </summary>
        private void MessageLenghtReceived(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
                {
                    OnDisconected?.Invoke(ID);
                    return;
                }

                receivingMessageReceived += e.BytesTransferred;
                if (receivingMessageReceived < receivingLenghtMessageBuffer.Length)
                {
                    receivingLenghtArgs.SetBuffer(receivingLenghtMessageBuffer, receivingMessageReceived, receivingLenghtMessageBuffer.Length - receivingMessageReceived);
                    if (!TcpSocket.ReceiveAsync(receivingLenghtArgs))
                        QueueMessageLenghtReceived();
                    return;
                }

                receivingMessageLenght = BitConverter.ToInt32(receivingLenghtMessageBuffer, 0);
                if (receivingMessageLenght < MinTcpMessageSize || receivingMessageLenght > MaxTcpMessageSize)
                {
                    OnException?.Invoke(new Exception("Invalid TCP message length " + receivingMessageLenght + " from client " + ID));
                    OnDisconected?.Invoke(ID);
                    return;
                }

                // Keep the 4-byte frame length inside the message buffer.
                receivingMessageBuffer = new byte[receivingMessageLenght];
                receivingMessageBuffer[0] = receivingLenghtMessageBuffer[0];
                receivingMessageBuffer[1] = receivingLenghtMessageBuffer[1];
                receivingMessageBuffer[2] = receivingLenghtMessageBuffer[2];
                receivingMessageBuffer[3] = receivingLenghtMessageBuffer[3];
                receivingMessageReceived = 4;
                receivingArgs.SetBuffer(receivingMessageBuffer, 4, receivingMessageLenght - 4);
                if (!TcpSocket.ReceiveAsync(receivingArgs))
                    QueueMessageDataReceived();
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                // client disconnected
                if (ex is SocketException || ex is ObjectDisposedException)
                    OnDisconected?.Invoke(ID);
            }
        }

        /// <summary>
        /// Executes the message data received operation.
        /// </summary>
        private void MessageDataReceived(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
                {
                    OnDisconected?.Invoke(ID);
                    return;
                }

                // message not fully received
                receivingMessageReceived += e.BytesTransferred;
                // OnException?.Invoke(new Exception("this block : " + e.BytesTransferred + " , total : " + receivingMessageReceived + " , expected : " + (receivingMessageBuffer.Length - 4) + " | " + receivingMessageLenght));
                if (receivingMessageBuffer.Length > receivingMessageReceived)
                {
                    // OnException?.Invoke(new Exception("inconsistent message block : " + receivingMessageReceived + " (" + e.BytesTransferred + ") / " + receivingMessageBuffer.Length));
                    receivingArgs.SetBuffer(receivingMessageBuffer, receivingMessageReceived, receivingMessageBuffer.Length - receivingMessageReceived);
                    if (!TcpSocket.ReceiveAsync(receivingArgs))
                        QueueMessageDataReceived();
                }
                // message fully received
                else
                {
                    receivingMessageReceived = 0;
                    NbMessagesReceived++;
                    receivedBytes += receivingMessageBuffer.Length;
                    receivingTCPMessage = new NetworkMessage(receivingMessageBuffer);
                    receivingTCPMessage.Client = this;
                    NetworkMessage receivedMessage = receivingTCPMessage;
                    receivingTCPMessage = null;
                    StartReceivingMessageLenght();
                    try
                    {
                        OnMessageReceived?.Invoke(receivedMessage);
                    }
                    catch (Exception ex)
                    {
                        OnException?.Invoke(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                // client disconnected
                OnDisconected?.Invoke(ID);
            }
        }

        /// <summary>
        /// Executes the queue message lenght received operation.
        /// </summary>
        private void QueueMessageLenghtReceived()
        {
            ThreadPool.QueueUserWorkItem(_ => MessageLenghtReceived(this, receivingLenghtArgs));
        }

        /// <summary>
        /// Executes the queue message data received operation.
        /// </summary>
        private void QueueMessageDataReceived()
        {
            ThreadPool.QueueUserWorkItem(_ => MessageDataReceived(this, receivingArgs));
        }
        #endregion
    }
}
