using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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
        public const int MinTcpMessageSize = 12;
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
        /// <summary>
        /// Stores the maximum time a producer may wait for TCP queue capacity.
        /// </summary>
        public static int TcpBackpressureTimeoutMilliseconds = 5000;
        /// <summary>
        /// Stores the heartbeat timeout in milliseconds. Use 0 to disable heartbeat timeout checks.
        /// </summary>
        public static int HeartbeatTimeoutMs = 30000;

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
        public long SendedBytes { get { return Interlocked.Read(ref sendedBytes) + (UDP != null ? Interlocked.Read(ref UDP.sendedBytes) : 0); } set { Interlocked.Exchange(ref sendedBytes, value); if (UDP != null) Interlocked.Exchange(ref UDP.sendedBytes, value); } }
        /// <summary>
        /// Gets or sets the received bytes value.
        /// </summary>
        public long ReceivedBytes { get { return Interlocked.Read(ref receivedBytes) + (UDP != null ? Interlocked.Read(ref UDP.receivedBytes) : 0); } set { Interlocked.Exchange(ref receivedBytes, value); if (UDP != null) Interlocked.Exchange(ref UDP.receivedBytes, value); } }

        /// <summary>
        /// Atomically takes and resets bytes sent by both TCP and UDP transports.
        /// </summary>
        /// <returns>Bytes sent since the previous take.</returns>
        public long TakeSendedBytes()
        {
            long bytes = Interlocked.Exchange(ref sendedBytes, 0);
            if (UDP != null)
                bytes += Interlocked.Exchange(ref UDP.sendedBytes, 0);
            return bytes;
        }

        /// <summary>
        /// Atomically takes and resets bytes received by both TCP and UDP transports.
        /// </summary>
        /// <returns>Bytes received since the previous take.</returns>
        public long TakeReceivedBytes()
        {
            long bytes = Interlocked.Exchange(ref receivedBytes, 0);
            if (UDP != null)
                bytes += Interlocked.Exchange(ref UDP.receivedBytes, 0);
            return bytes;
        }
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
        /// Stores the established TLS stream when encrypted TCP transport is enabled.
        /// </summary>
        private Stream tcpTransportStream;
        /// <summary>
        /// Gets or sets the last measured TCP ping in milliseconds.
        /// </summary>
        public ushort Ping { get; set; }
        /// <summary>
        /// Gets when a full TCP message was last received from this peer.
        /// </summary>
        public DateTime LastMessageReceivedUtc { get; private set; }
        /// <summary>
        /// Gets whether this peer exceeded the heartbeat timeout.
        /// </summary>
        public bool HasHeartbeatTimedOut
        {
            get
            {
                int timeoutMs = HeartbeatTimeoutMs;
                return timeoutMs > 0 && (DateTime.UtcNow - LastMessageReceivedUtc).TotalMilliseconds > timeoutMs;
            }
        }
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
        /// Stores the reusable asynchronous TCP send arguments.
        /// </summary>
        private SocketAsyncEventArgs sendingArgs;
        /// <summary>
        /// Stores the offset already sent from the active TCP message.
        /// </summary>
        private int currentSendingOffset;
        /// <summary>
        /// Coordinates producers waiting for bounded TCP queue capacity.
        /// </summary>
        private readonly object tcpQueueBackpressureLock = new object();
        /// <summary>
        /// Counts producers currently blocked by TCP backpressure.
        /// </summary>
        private int waitingTcpProducers;
        /// <summary>
        /// Stores whether the TCP transport is closed for new producers.
        /// </summary>
        private int tcpTransportClosed = 1;

        /// <summary>
        /// Initializes a new instance of the connected client class.
        /// </summary>
        public ConnectedClient()
        {
            SendingQueue = new ConcurrentQueue<PooledByteBuffer>();
            receivingMessageBuffer = new byte[12];
            receivingLenghtMessageBuffer = new byte[4];
            LastMessageReceivedUtc = DateTime.UtcNow;
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
        /// Closes the TLS layer and its underlying TCP socket.
        /// </summary>
        public void CloseTcpTransport()
        {
            TryCloseTcpTransport();
        }

        /// <summary>
        /// Atomically closes the TCP transport once and wakes every blocked producer.
        /// </summary>
        /// <returns>True only for the caller that performed the closure.</returns>
        private bool TryCloseTcpTransport()
        {
            if (Interlocked.Exchange(ref tcpTransportClosed, 1) != 0)
                return false;

            lock (tcpQueueBackpressureLock)
                Monitor.PulseAll(tcpQueueBackpressureLock);

            // Close UDP first so receive callbacks and authentication state cannot outlive the TCP session.
            try { UDP?.Close(); } catch { }
            // Dispose TLS next so it can release its cryptographic and buffering state.
            try { tcpTransportStream?.Dispose(); } catch { }
            tcpTransportStream = null;
            try { TcpSocket?.Shutdown(SocketShutdown.Both); } catch { }
            try { TcpSocket?.Close(); } catch { }
            try { TcpSocket?.Dispose(); } catch { }
            DrainSendingQueue();
            return true;
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

            if (!TryReserveTcpQueueCapacity(msg.Length))
            {
                msg.Dispose();
                return;
            }

            if (Volatile.Read(ref tcpTransportClosed) != 0)
            {
                ReleaseTcpQueueCapacity(msg.Length);
                msg.Dispose();
                return;
            }

            SendingQueue.Enqueue(msg);
            if (Volatile.Read(ref tcpTransportClosed) != 0)
            {
                DrainSendingQueue();
                return;
            }

            TryStartSending();
        }


        /// <summary>
        /// Reserves one bounded TCP queue slot, blocking only while the configured limits are full.
        /// </summary>
        /// <param name="messageLength">Serialized message length.</param>
        /// <returns>True when capacity was reserved, or false after transport closure.</returns>
        private bool TryReserveTcpQueueCapacity(int messageLength)
        {
            int maxMessages = Math.Max(1, MaxTcpQueuedMessages);
            long maxBytes = Math.Max(1L, MaxTcpQueuedBytes);
            if (messageLength > maxBytes)
            {
                OnException?.Invoke(new InvalidOperationException(
                    "A TCP message exceeds the complete send queue byte capacity for client " + ID + "."));
                return false;
            }

            while (Volatile.Read(ref tcpTransportClosed) == 0)
            {
                int queuedMessages = Interlocked.Increment(ref queuedTcpMessages);
                long queuedBytes = Interlocked.Add(ref queuedTcpBytes, messageLength);
                if (queuedMessages <= maxMessages && queuedBytes <= maxBytes)
                    return true;

                ReleaseTcpQueueCapacity(messageLength);
                bool backpressureTimedOut = false;
                lock (tcpQueueBackpressureLock)
                {
                    if (Volatile.Read(ref tcpTransportClosed) != 0)
                        return false;

                    Interlocked.Increment(ref waitingTcpProducers);
                    try
                    {
                        int timeoutMilliseconds = Math.Max(1, TcpBackpressureTimeoutMilliseconds);
                        Stopwatch waitDuration = Stopwatch.StartNew();
                        while (Volatile.Read(ref tcpTransportClosed) == 0 &&
                            !HasTcpQueueCapacity(messageLength, maxMessages, maxBytes))
                        {
                            int remainingMilliseconds = timeoutMilliseconds - (int)waitDuration.ElapsedMilliseconds;
                            if (remainingMilliseconds <= 0 ||
                                !Monitor.Wait(tcpQueueBackpressureLock, remainingMilliseconds))
                            {
                                backpressureTimedOut = !HasTcpQueueCapacity(
                                    messageLength,
                                    maxMessages,
                                    maxBytes);
                                break;
                            }
                        }
                    }
                    finally
                    {
                        Interlocked.Decrement(ref waitingTcpProducers);
                    }
                }

                if (backpressureTimedOut)
                {
                    HandleTcpBackpressureTimeout();
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Closes a connection whose TCP consumer remained saturated beyond the configured timeout.
        /// </summary>
        private void HandleTcpBackpressureTimeout()
        {
            if (!TryCloseTcpTransport())
                return;

            Interlocked.Increment(ref nbMessagesDropped);
            TimeoutException exception = new TimeoutException(
                "TCP send queue remained saturated for client " + ID + ".");
            try { OnException?.Invoke(exception); }
            finally { OnDisconected?.Invoke(ID); }
        }

        /// <summary>
        /// Returns whether the bounded TCP queue can accept one serialized message.
        /// </summary>
        /// <param name="messageLength">Serialized message length.</param>
        /// <param name="maxMessages">Maximum queued message count.</param>
        /// <param name="maxBytes">Maximum queued byte count.</param>
        /// <returns>True when both limits have capacity.</returns>
        private bool HasTcpQueueCapacity(int messageLength, int maxMessages, long maxBytes)
        {
            return Volatile.Read(ref queuedTcpMessages) < maxMessages &&
                Interlocked.Read(ref queuedTcpBytes) <= maxBytes - messageLength;
        }

        /// <summary>
        /// Releases one TCP queue reservation and wakes blocked producers when necessary.
        /// </summary>
        /// <param name="messageLength">Released serialized message length.</param>
        private void ReleaseTcpQueueCapacity(int messageLength)
        {
            Interlocked.Decrement(ref queuedTcpMessages);
            Interlocked.Add(ref queuedTcpBytes, -messageLength);
            if (Volatile.Read(ref waitingTcpProducers) == 0)
                return;

            lock (tcpQueueBackpressureLock)
                Monitor.PulseAll(tcpQueueBackpressureLock);
        }
        /// <summary>
        /// enqueue an UDP message to send
        /// </summary>
        /// <param name="msg">message to send</param>
        public void AddUnreliableMessage(NetworkMessage msg)
        {
            UDP?.SendMessage(msg);
        }

        /// <summary>
        /// enqueue an UDP message to send
        /// </summary>
        /// <param name="headID">headID of the message to send</param>
        /// <param name="msg">message to send</param>
        public void AddUnreliableMessage(ushort headID, byte[] msg)
        {
            UDP?.SendMessage(headID, msg);
        }

        /// <summary>
        /// Refreshes the last message received timestamp.
        /// </summary>
        public void MarkMessageReceived()
        {
            LastMessageReceivedUtc = DateTime.UtcNow;
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
        /// <param name="udpSessionKey">Handshake key used to authenticate UDP datagrams.</param>
        /// <param name="tcpStream">Established TLS stream, or null for the optimized raw socket transport.</param>
        public void SetClient(Socket tcpClient, bool isClient, bool enableUDP, byte[] udpSessionKey = null, Stream tcpStream = null)
        {
            TcpSocket = tcpClient;
            Volatile.Write(ref tcpTransportClosed, 0);
            // Start message transports only after the handshake selected their negotiated settings.
            tcpTransportStream = tcpStream;
            MarkMessageReceived();
            if (enableUDP)
            {
                UDP = new UDPConnection();
                UDP.SetAuthenticationKey(udpSessionKey);
                if (isClient)
                    UDP.CreateClientConnection(this, tcpClient);
                else
                    UDP.CreateServerConnection(this, tcpClient);
                UDPEnabled = true;
            }

            nbMessagesSended = 0;

            if (tcpTransportStream != null)
            {
                // TLS frames must be read through SslStream because their socket bytes remain encrypted.
                StartReceivingStreamMessages();
                return;
            }

            sendingArgs = new SocketAsyncEventArgs();
            sendingArgs.UserToken = TcpSocket;
            sendingArgs.Completed += TcpMessageSent;

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
        /// Starts the single TCP send pump for this connection.
        /// </summary>
        private void TryStartSending()
        {
            if (Volatile.Read(ref tcpTransportClosed) != 0 ||
                Interlocked.CompareExchange(ref isSendingTCPMessage, 1, 0) != 0)
                return;

            if (tcpTransportStream != null)
            {
                SendQueuedStreamMessagesLoop();
                return;
            }

            ProcessSocketSendQueue();
        }

        /// <summary>
        /// Sends queued TLS messages asynchronously without occupying a ThreadPool thread while blocked.
        /// </summary>
        private async void SendQueuedStreamMessagesLoop()
        {
            try
            {
                while (true)
                {
                    if (currentSendingTCPMessage == null && !TryTakeNextTcpMessage())
                    {
                        if (TryCompleteSendPump())
                            return;
                        continue;
                    }

                    Stream stream = tcpTransportStream;
                    if (stream == null)
                        throw new ObjectDisposedException(nameof(tcpTransportStream));

                    PooledByteBuffer message = currentSendingTCPMessage;
                    await stream.WriteAsync(message.Buffer, 0, message.Length).ConfigureAwait(false);
                    Interlocked.Increment(ref nbMessagesSended);
                    DisposeCurrentSendingMessage();
                }
            }
            catch (Exception ex)
            {
                HandleTcpSendFailure(ex);
            }
        }

        /// <summary>
        /// Advances raw socket sends with one reusable SocketAsyncEventArgs instance.
        /// </summary>
        private void ProcessSocketSendQueue()
        {
            try
            {
                while (true)
                {
                    if (currentSendingTCPMessage == null && !TryTakeNextTcpMessage())
                    {
                        if (TryCompleteSendPump())
                            return;
                        continue;
                    }

                    PooledByteBuffer message = currentSendingTCPMessage;
                    sendingArgs.SetBuffer(
                        message.Buffer,
                        currentSendingOffset,
                        message.Length - currentSendingOffset);
                    if (TcpSocket.SendAsync(sendingArgs))
                        return;

                    CompleteSocketSend(sendingArgs);
                }
            }
            catch (Exception ex)
            {
                HandleTcpSendFailure(ex);
            }
        }

        /// <summary>
        /// Continues the raw TCP send pump after one asynchronous socket operation completes.
        /// </summary>
        /// <param name="sender">Socket that completed the operation.</param>
        /// <param name="eventArgs">Reusable send operation state.</param>
        private void TcpMessageSent(object sender, SocketAsyncEventArgs eventArgs)
        {
            try
            {
                CompleteSocketSend(eventArgs);
            }
            catch (Exception ex)
            {
                HandleTcpSendFailure(ex);
                return;
            }

            ProcessSocketSendQueue();
        }

        /// <summary>
        /// Applies one raw socket send result and completes the active message when fully transferred.
        /// </summary>
        /// <param name="eventArgs">Completed send operation.</param>
        private void CompleteSocketSend(SocketAsyncEventArgs eventArgs)
        {
            if (eventArgs.SocketError != SocketError.Success)
                throw new SocketException((int)eventArgs.SocketError);
            if (eventArgs.BytesTransferred <= 0)
                throw new SocketException((int)SocketError.ConnectionReset);

            currentSendingOffset += eventArgs.BytesTransferred;
            if (currentSendingOffset < currentSendingTCPMessage.Length)
                return;

            Interlocked.Increment(ref nbMessagesSended);
            DisposeCurrentSendingMessage();
        }

        /// <summary>
        /// Dequeues and prepares one TCP message while releasing its bounded queue reservation.
        /// </summary>
        /// <returns>True when a message became active.</returns>
        private bool TryTakeNextTcpMessage()
        {
            PooledByteBuffer nextMessage;
            if (!SendingQueue.TryDequeue(out nextMessage))
                return false;

            ReleaseTcpQueueCapacity(nextMessage.Length);
            currentSendingTCPMessage = nextMessage;
            currentSendingOffset = 0;
            Interlocked.Add(ref sendedBytes, nextMessage.Length);
            NotifyTcpMessageSending(nextMessage);
            return true;
        }

        /// <summary>
        /// Publishes one logical TCP send to diagnostics subscribers.
        /// </summary>
        /// <param name="message">Message starting transmission.</param>
        private void NotifyTcpMessageSending(PooledByteBuffer message)
        {
            Action<byte[]> onMessageSend = OnMessageSend;
            if (onMessageSend == null)
                return;

            byte[] sentData = message.Buffer;
            if (message.Length != sentData.Length)
            {
                sentData = new byte[message.Length];
                Buffer.BlockCopy(message.Buffer, 0, sentData, 0, message.Length);
            }

            try { onMessageSend(sentData); }
            catch (Exception ex) { OnException?.Invoke(ex); }
        }

        /// <summary>
        /// Releases pump ownership when empty or reacquires it after a producer race.
        /// </summary>
        /// <returns>True when this pump must terminate.</returns>
        private bool TryCompleteSendPump()
        {
            Interlocked.Exchange(ref isSendingTCPMessage, 0);
            if (Volatile.Read(ref tcpTransportClosed) != 0)
                return true;
            if (SendingQueue.IsEmpty)
                return true;
            return Interlocked.CompareExchange(ref isSendingTCPMessage, 1, 0) != 0;
        }

        /// <summary>
        /// Cleans queued buffers and publishes a terminal TCP send failure.
        /// </summary>
        /// <param name="exception">Transport failure.</param>
        private void HandleTcpSendFailure(Exception exception)
        {
            Interlocked.Exchange(ref isSendingTCPMessage, 0);
            DisposeCurrentSendingMessage();
            DrainSendingQueue();
            OnException?.Invoke(exception);
            if (exception is SocketException ||
                exception is IOException ||
                exception is ObjectDisposedException)
            {
                OnDisconected?.Invoke(ID);
            }
        }

        /// <summary>
        /// Releases the active pooled TCP message.
        /// </summary>
        private void DisposeCurrentSendingMessage()
        {
            PooledByteBuffer message = currentSendingTCPMessage;
            currentSendingTCPMessage = null;
            currentSendingOffset = 0;
            message?.Dispose();
        }

        /// <summary>
        /// Releases every queued pooled TCP message and its capacity reservation.
        /// </summary>
        private void DrainSendingQueue()
        {
            PooledByteBuffer queuedMessage;
            while (SendingQueue.TryDequeue(out queuedMessage))
            {
                ReleaseTcpQueueCapacity(queuedMessage.Length);
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
        /// Starts the asynchronous receive loop used by encrypted TCP transports.
        /// </summary>
        private void StartReceivingStreamMessages()
        {
            // The loop observes and reports its own failures, so the fire-and-forget task is safe.
            _ = ReceiveStreamMessagesLoop();
        }

        /// <summary>
        /// Reads complete NetSquare messages from the established TLS stream.
        /// </summary>
        /// <returns>A task that completes when the encrypted transport closes.</returns>
        private async Task ReceiveStreamMessagesLoop()
        {
            try
            {
                while (true)
                {
                    byte[] lengthBuffer = new byte[4];
                    await ReadStreamExactAsync(lengthBuffer, 0, lengthBuffer.Length).ConfigureAwait(false);
                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (messageLength < MinTcpMessageSize || messageLength > MaxTcpMessageSize)
                    {
                        throw new InvalidDataException(
                            "Invalid TCP message length " + messageLength + " from client " + ID + ".");
                    }

                    byte[] messageBuffer = new byte[messageLength];
                    Buffer.BlockCopy(lengthBuffer, 0, messageBuffer, 0, lengthBuffer.Length);
                    await ReadStreamExactAsync(
                        messageBuffer,
                        lengthBuffer.Length,
                        messageLength - lengthBuffer.Length).ConfigureAwait(false);

                    NbMessagesReceived++;
                    receivedBytes += messageBuffer.Length;
                    MarkMessageReceived();
                    NetworkMessage receivedMessage = new NetworkMessage(messageBuffer);
                    receivedMessage.Client = this;
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
                // Closing or authenticating failures terminate the same public connection state as socket failures.
                OnException?.Invoke(ex);
                OnDisconected?.Invoke(ID);
            }
        }

        /// <summary>
        /// Fills one section of a buffer from the TLS stream.
        /// </summary>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="offset">First destination index.</param>
        /// <param name="length">Number of bytes required.</param>
        /// <returns>A task that completes when the requested bytes are available.</returns>
        private async Task ReadStreamExactAsync(byte[] buffer, int offset, int length)
        {
            // SslStream may return partial application records, so continue until the frame is complete.
            int remaining = length;
            while (remaining > 0)
            {
                int received = await tcpTransportStream.ReadAsync(buffer, offset, remaining).ConfigureAwait(false);
                if (received <= 0)
                    throw new IOException("The remote peer closed the encrypted TCP stream.");
                offset += received;
                remaining -= received;
            }
        }
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
                    MarkMessageReceived();
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
