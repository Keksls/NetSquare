using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using NetSquare.Core.Messages;

namespace NetSquare.Core
{
    /// <summary>
    /// Represents the udp connection component.
    /// </summary>
    public class UDPConnection
    {
        /// <summary>
        /// Stores the connection value.
        /// </summary>
        public UdpClient connection;
        /// <summary>
        /// Stores the remote end point value.
        /// </summary>
        public IPEndPoint RemoteEndPoint;
        /// <summary>
        /// Stores the nb sending messages value.
        /// </summary>
        public int NbSendingMessages;
        /// <summary>
        /// Stores the nb messages sended value.
        /// </summary>
        public int NbMessagesSended;
        /// <summary>
        /// Stores the nb messages dropped value.
        /// </summary>
        private long nbMessagesDropped;
        /// <summary>
        /// Gets or sets the nb messages dropped value.
        /// </summary>
        public long NbMessagesDropped { get { return Interlocked.Read(ref nbMessagesDropped); } }
        /// <summary>
        /// Stores the sended bytes value.
        /// </summary>
        internal long sendedBytes = 0;
        /// <summary>
        /// Stores the received bytes value.
        /// </summary>
        internal long receivedBytes = 0;
        /// <summary>
        /// Stores the udp sending queue value.
        /// </summary>
        private Dictionary<ushort, byte[]> UDPSendingQueue;
        /// <summary>
        /// Stores the queued udp messages value.
        /// </summary>
        private int queuedUdpMessages;
        /// <summary>
        /// Stores the related client value.
        /// </summary>
        private ConnectedClient relatedClient;
        /// <summary>
        /// Stores the is sending udp message value.
        /// </summary>
        private bool isSendingUDPMessage = false;
        /// <summary>
        /// Stores the is server value.
        /// </summary>
        private bool isServer = false;
        /// <summary>
        /// Stores routes whose coalesced payload became ready, avoiding a scan of every registered route.
        /// </summary>
        private readonly Queue<ushort> pendingUdpRoutes = new Queue<ushort>();
        /// <summary>
        /// Stores the current sending message value.
        /// </summary>
        private byte[] currentSendingMessage = null;
        /// <summary>
        /// Keeps the authenticated datagram alive until the socket completes the asynchronous send.
        /// </summary>
        private byte[] currentSendingDatagram;
        /// <summary>
        /// Reuses one socket operation object for every outgoing datagram on this connection.
        /// </summary>
        private readonly SocketAsyncEventArgs sendingArgs;
        /// <summary>
        /// Stores the send lock value.
        /// </summary>
        private readonly object sendLock = new object();
        /// <summary>
        /// Stores the server hub value.
        /// </summary>
        private ServerUdpHub serverHub;
        /// <summary>
        /// Stores the local UDP endpoint used by the server hub.
        /// </summary>
        private IPEndPoint serverLocalEndPoint;
        /// <summary>
        /// Stores the UDP session key until the transport direction is known.
        /// </summary>
        private byte[] udpSessionKey;
        /// <summary>
        /// Authenticates UDP datagrams when MAC64 was negotiated for this TCP session.
        /// </summary>
        private UdpDatagramAuthenticator authenticator;
        /// <summary>
        /// Stores whether both UDP directions were registered.
        /// </summary>
        private int registrationCompleted;
        /// <summary>
        /// Stores whether this UDP transport was closed.
        /// </summary>
        private int closed;
        /// <summary>
        /// Gets whether UDP endpoint registration completed.
        /// </summary>
        public bool IsRegistrationCompleted { get { return Volatile.Read(ref registrationCompleted) != 0; } }
        /// <summary>
        /// Stores the server hubs value.
        /// </summary>
        private static readonly ConcurrentDictionary<string, ServerUdpHub> ServerHubs = new ConcurrentDictionary<string, ServerUdpHub>();

        /// <summary>
        /// Executes the udp connection operation.
        /// </summary>
        public UDPConnection()
        {
            UDPSendingQueue = new Dictionary<ushort, byte[]>();
            Volatile.Write(ref closed, 0);
            sendingArgs = new SocketAsyncEventArgs();
            sendingArgs.Completed += MessageSended;
        }

        /// <summary>
        /// Stores the session key delivered by the TCP handshake.
        /// </summary>
        /// <param name="sessionKey">Sixteen-byte session key, or null for unprotected UDP.</param>
        public void SetAuthenticationKey(byte[] sessionKey)
        {
            // Own a private copy so callers cannot mutate an authenticated session after setup.
            Interlocked.Exchange(ref registrationCompleted, 0);
            if (udpSessionKey != null)
                Array.Clear(udpSessionKey, 0, udpSessionKey.Length);
            authenticator?.Dispose();
            authenticator = null;

            if (sessionKey == null)
            {
                udpSessionKey = null;
                return;
            }
            if (sessionKey.Length != NetSquareHandshakeProtocol.NonceLength)
            {
                // A malformed negotiated key must fail closed instead of silently downgrading to raw UDP.
                throw new ArgumentException(
                    "The UDP authentication key must contain exactly " +
                    NetSquareHandshakeProtocol.NonceLength + " bytes.",
                    nameof(sessionKey));
            }

            udpSessionKey = new byte[sessionKey.Length];
            Buffer.BlockCopy(sessionKey, 0, udpSessionKey, 0, sessionKey.Length);
        }

        /// <summary>
        /// Initializes directional UDP authentication after the client or server role is known.
        /// </summary>
        private void InitializeAuthenticator()
        {
            if (udpSessionKey == null)
                return;

            // The authenticator derives and owns its keys; erase the temporary handshake key copy.
            byte[] sessionKey = udpSessionKey;
            udpSessionKey = null;
            try
            {
                authenticator = new UdpDatagramAuthenticator(sessionKey, isServer);
            }
            finally
            {
                Array.Clear(sessionKey, 0, sessionKey.Length);
            }
        }

        /// <summary>
        /// Validates one datagram according to the UDP mode negotiated during the TCP handshake.
        /// </summary>
        /// <param name="datagram">Received UDP bytes.</param>
        /// <param name="payloadLength">Validated NetworkMessage length.</param>
        /// <returns>True when the raw envelope or MAC64 envelope is valid.</returns>
        private bool TryDecodeDatagram(byte[] datagram, out int payloadLength)
        {
            if (authenticator != null)
            {
                return authenticator.TryAuthenticate(
                    datagram,
                    ConnectedClient.MinTcpMessageSize,
                    out payloadLength);
            }

            // Unprotected mode still enforces the exact declared message length before parsing.
            return UdpDatagramAuthenticator.TryGetPayloadLength(
                datagram,
                ConnectedClient.MinTcpMessageSize,
                false,
                out payloadLength);
        }

        /// <summary>
        /// Completes client registration and binds its observed endpoint.
        /// </summary>
        /// <param name="message">Validated UDP registration message.</param>
        /// <param name="remoteEndPoint">Observed datagram source.</param>
        /// <returns>True when the endpoint was registered.</returns>
        private bool TryCompleteServerRegistration(NetworkMessage message, IPEndPoint remoteEndPoint)
        {
            // MAC64 allows authenticated rebinding; unprotected UDP keeps the first observed endpoint.
            if (!isServer ||
                message == null ||
                message.HeadID != (ushort)NetSquareMessageID.UdpRegister ||
                remoteEndPoint == null)
                return false;

            if (IsRegistrationCompleted &&
                authenticator == null &&
                (RemoteEndPoint == null || !RemoteEndPoint.Equals(remoteEndPoint)))
                return false;

            RemoteEndPoint = remoteEndPoint;
            Interlocked.Exchange(ref registrationCompleted, 1);
            return true;
        }

        /// <summary>
        /// Completes registration after a validated server acknowledgement.
        /// </summary>
        /// <param name="message">Validated UDP registration acknowledgement.</param>
        /// <returns>True when registration became complete.</returns>
        private bool TryCompleteClientRegistration(NetworkMessage message)
        {
            if (isServer ||
                message == null ||
                message.HeadID != (ushort)NetSquareMessageID.UdpRegister)
                return false;

            Interlocked.Exchange(ref registrationCompleted, 1);
            return true;
        }

        /// <summary>
        /// Create new Client Side UDP Connection
        /// </summary>
        /// <param name="_relatedClient">ConnectedClient owner</param>
        /// <param name="relatedTcpClient">TCP socket equivalent</param>
        public void CreateClientConnection(ConnectedClient _relatedClient, Socket relatedTcpClient)
        {
            isServer = false;
            relatedClient = _relatedClient;
            InitializeAuthenticator();
            IPEndPoint localTcpEndPoint = (IPEndPoint)relatedTcpClient.LocalEndPoint;
            IPEndPoint remoteTcpEndPoint = (IPEndPoint)relatedTcpClient.RemoteEndPoint;
            RemoteEndPoint = new IPEndPoint(remoteTcpEndPoint.Address, remoteTcpEndPoint.Port + 1);
            connection = new UdpClient(new IPEndPoint(localTcpEndPoint.Address, 0));
            connection.Connect(RemoteEndPoint);
            connection.BeginReceive(OnReceiveUDP, connection);
            SendRegistration();
        }

        /// <summary>
        /// Create new Server Side UDP Connection
        /// </summary>
        /// <param name="_relatedClient">ConnectedClient owner</param>
        /// <param name="relatedTcpClient">TCP socket equivalent</param>
        public void CreateServerConnection(ConnectedClient _relatedClient, Socket relatedTcpClient)
        {
            isServer = true;
            relatedClient = _relatedClient;
            InitializeAuthenticator();
            IPEndPoint localTcpEndPoint = (IPEndPoint)relatedTcpClient.LocalEndPoint;
            IPEndPoint remoteTcpEndPoint = (IPEndPoint)relatedTcpClient.RemoteEndPoint;
            RemoteEndPoint = new IPEndPoint(remoteTcpEndPoint.Address, remoteTcpEndPoint.Port + 1);
            serverLocalEndPoint = new IPEndPoint(localTcpEndPoint.Address, localTcpEndPoint.Port + 1);
            serverHub = GetServerHub(serverLocalEndPoint);
            connection = serverHub.Connection;
            RegisterServerClient();
        }

        /// <summary>
        /// Executes the register server client operation.
        /// </summary>
        public void RegisterServerClient()
        {
            if (!isServer || relatedClient == null || relatedClient.ID == 0)
                return;

            if (serverHub == null)
            {
                if (serverLocalEndPoint == null)
                    serverLocalEndPoint = (IPEndPoint)connection.Client.LocalEndPoint;

                serverHub = GetServerHub(serverLocalEndPoint);
                connection = serverHub.Connection;
            }

            serverHub.Register(this);
        }

        /// <summary>
        /// Closes this UDP transport, clears queued data, and releases authentication state.
        /// </summary>
        public void Close()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0)
                return;

            UnregisterServerClient();
            Interlocked.Exchange(ref registrationCompleted, 0);
            lock (sendLock)
            {
                currentSendingMessage = null;
                isSendingUDPMessage = false;
                queuedUdpMessages = 0;
                ClearQueuedMessagesLocked();
                RefreshSendingCountLocked();
            }

            UdpDatagramAuthenticator currentAuthenticator = authenticator;
            authenticator = null;
            currentAuthenticator?.Dispose();
            byte[] sessionKey = udpSessionKey;
            udpSessionKey = null;
            if (sessionKey != null)
                Array.Clear(sessionKey, 0, sessionKey.Length);

            // Server transports share their hub socket; only standalone client sockets are owned here.
            if (!isServer)
            {
                try { connection?.Close(); }
                catch { }
            }
        }
        /// <summary>
        /// Executes the unregister server client operation.
        /// </summary>
        public void UnregisterServerClient()
        {
            if (!isServer || serverHub == null || relatedClient == null || relatedClient.ID == 0)
                return;

            serverHub.Unregister(relatedClient.ID);
            serverHub = null;
        }

        /// <summary>
        /// Sends a UDP endpoint registration datagram.
        /// </summary>
        public void SendRegistration()
        {
            if (isServer || relatedClient == null || relatedClient.ID == 0)
                return;

            // The empty registration body avoids transmitting the session secret on UDP.
            NetworkMessage registration = new NetworkMessage(NetSquareMessageID.UdpRegister, relatedClient.ID);
            QueueDatagram(registration.HeadID, registration.Serialize(), true);
        }

        /// <summary>
        /// Sends the server acknowledgement for UDP registration.
        /// </summary>
        private void SendRegistrationAcknowledgement()
        {
            if (!isServer || relatedClient == null || relatedClient.ID == 0)
                return;

            NetworkMessage acknowledgement = new NetworkMessage(NetSquareMessageID.UdpRegister, relatedClient.ID);
            QueueDatagram(acknowledgement.HeadID, acknowledgement.Serialize(), true);
        }

        /// <summary>
        /// Serializes and queues one UDP message.
        /// </summary>
        /// <param name="msg">Message to send.</param>
        public void SendMessage(NetworkMessage msg)
        {
            if (msg == null)
                return;

            QueueDatagram(msg.HeadID, msg.Serialize(), false);
        }

        /// <summary>
        /// Queues one serialized message as a UDP datagram.
        /// </summary>
        /// <param name="headID">Message route identifier used by the coalescing queue.</param>
        /// <param name="msg">Serialized NetworkMessage bytes.</param>
        public void SendMessage(ushort headID, byte[] msg)
        {
            QueueDatagram(headID, msg, false);
        }

        /// <summary>
        /// Queues one UDP payload and defers optional authentication until it is actually sent.
        /// </summary>
        /// <param name="headID">Message route identifier used by the coalescing queue.</param>
        /// <param name="payload">Serialized NetworkMessage bytes.</param>
        /// <param name="allowBeforeRegistration">Whether this is a transport registration frame.</param>
        private void QueueDatagram(
            ushort headID,
            byte[] payload,
            bool allowBeforeRegistration)
        {
            if (payload == null || payload.Length == 0 || Volatile.Read(ref closed) != 0)
                return;

            bool shouldStartSend = false;
            lock (sendLock)
            {
                if (!allowBeforeRegistration && !IsRegistrationCompleted)
                    return;

                byte[] queuedPayload;
                if (!UDPSendingQueue.TryGetValue(headID, out queuedPayload))
                {
                    UDPSendingQueue.Add(headID, null);
                    queuedPayload = null;
                }

                // Keep only the newest pending datagram for each route and enqueue the route once.
                if (isSendingUDPMessage)
                {
                    if (queuedPayload != null)
                    {
                        Interlocked.Increment(ref nbMessagesDropped);
                    }
                    else
                    {
                        pendingUdpRoutes.Enqueue(headID);
                        queuedUdpMessages++;
                    }

                    UDPSendingQueue[headID] = payload;
                }
                else
                {
                    isSendingUDPMessage = true;
                    currentSendingMessage = payload;
                    shouldStartSend = true;
                }
                RefreshSendingCountLocked();
            }

            if (shouldStartSend)
                BeginSendMessage(payload);
        }

        #region UDP
        /// <summary>
        /// Validates and dispatches one client-side UDP datagram.
        /// </summary>
        /// <param name="res">Asynchronous receive result.</param>
        private void OnReceiveUDP(IAsyncResult res)
        {
            UdpClient receiveConnection = res != null ? res.AsyncState as UdpClient : null;
            if (receiveConnection == null || Volatile.Read(ref closed) != 0)
                return;

            try
            {
                byte[] datagram = receiveConnection.EndReceive(res, ref RemoteEndPoint);
                Interlocked.Add(ref receivedBytes, datagram.Length);

                int payloadLength;
                if (!TryDecodeDatagram(datagram, out payloadLength))
                    return;

                // Never parse or dispatch bytes before their configured envelope is valid.
                NetworkMessage message = new NetworkMessage();
                if (!message.SafeSetDatagram(datagram, payloadLength))
                    return;

                if (message.HeadID == (ushort)NetSquareMessageID.UdpRegister)
                {
                    // Consume registration acknowledgements inside the transport layer.
                    TryCompleteClientRegistration(message);
                }
                else if (IsRegistrationCompleted)
                {
                    relatedClient.NbMessagesReceived++;
                    message.Client = relatedClient;
                    relatedClient.Fire_OnMessageReceived(message);
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
            catch (NullReferenceException) when (Volatile.Read(ref closed) != 0)
            {
                // .NET Framework's UdpClient can clear its internal socket before EndReceive observes Close.
            }
            finally
            {
                if (Volatile.Read(ref closed) == 0)
                {
                    try { receiveConnection.BeginReceive(OnReceiveUDP, receiveConnection); } catch { }
                }
            }
        }

        /// <summary>
        /// Starts one UDP send with the reusable socket operation object.
        /// </summary>
        /// <param name="message">Coalesced serialized payload to send.</param>
        private void BeginSendMessage(byte[] message)
        {
            if (Volatile.Read(ref closed) != 0)
            {
                ResetSendPump();
                return;
            }

            try
            {
                // Protect only the payload that survived coalescing, preserving sequence order and CPU time.
                byte[] datagram = authenticator != null ? authenticator.Protect(message) : message;
                currentSendingDatagram = datagram;
                sendingArgs.RemoteEndPoint = isServer ? RemoteEndPoint : null;
                sendingArgs.SetBuffer(datagram, 0, datagram.Length);
                Interlocked.Add(ref sendedBytes, datagram.Length);

                Socket socket = connection.Client;
                bool pending = isServer
                    ? socket.SendToAsync(sendingArgs)
                    : socket.SendAsync(sendingArgs);
                if (!pending)
                    ProcessSendCompletion(sendingArgs);
            }
            catch (SocketException)
            {
                ResetSendPump();
            }
            catch (ObjectDisposedException)
            {
                ResetSendPump();
            }
        }

        /// <summary>
        /// Handles completion of the reusable UDP socket operation.
        /// </summary>
        /// <param name="sender">Socket that completed the send.</param>
        /// <param name="eventArgs">Reusable send operation state.</param>
        private void MessageSended(object sender, SocketAsyncEventArgs eventArgs)
        {
            ProcessSendCompletion(eventArgs);
        }

        /// <summary>
        /// Advances the coalesced send pump after one synchronous or asynchronous socket completion.
        /// </summary>
        /// <param name="eventArgs">Completed socket operation.</param>
        private void ProcessSendCompletion(SocketAsyncEventArgs eventArgs)
        {
            if (eventArgs.SocketError != SocketError.Success ||
                currentSendingDatagram == null ||
                eventArgs.BytesTransferred != currentSendingDatagram.Length)
            {
                ResetSendPump();
                return;
            }

            NbMessagesSended++;
            currentSendingDatagram = null;
            eventArgs.SetBuffer(null, 0, 0);

            byte[] nextMessage = null;
            lock (sendLock)
            {
                currentSendingMessage = null;
                if (Volatile.Read(ref closed) == 0 && GetNextSendingMessage(ref nextMessage))
                    currentSendingMessage = nextMessage;
                else
                    isSendingUDPMessage = false;
                RefreshSendingCountLocked();
            }

            if (nextMessage != null)
                BeginSendMessage(nextMessage);
        }

        /// <summary>
        /// Dequeues the next route whose coalesced UDP payload is ready.
        /// </summary>
        /// <param name="message">Newest payload retained for the dequeued route.</param>
        /// <returns>True when a payload is ready to send.</returns>
        private bool GetNextSendingMessage(ref byte[] message)
        {
            while (pendingUdpRoutes.Count > 0)
            {
                ushort headID = pendingUdpRoutes.Dequeue();
                byte[] queuedPayload;
                if (!UDPSendingQueue.TryGetValue(headID, out queuedPayload) || queuedPayload == null)
                    continue;

                UDPSendingQueue[headID] = null;
                queuedUdpMessages--;
                message = queuedPayload;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears the active and queued UDP sends after a terminal socket failure.
        /// </summary>
        private void ResetSendPump()
        {
            lock (sendLock)
            {
                currentSendingMessage = null;
                currentSendingDatagram = null;
                sendingArgs.SetBuffer(null, 0, 0);
                isSendingUDPMessage = false;
                queuedUdpMessages = 0;
                ClearQueuedMessagesLocked();
                RefreshSendingCountLocked();
            }
        }
        /// <summary>
        /// Executes the refresh sending count locked operation.
        /// </summary>
        private void RefreshSendingCountLocked()
        {
            NbSendingMessages = queuedUdpMessages + (isSendingUDPMessage ? 1 : 0);
        }

        /// <summary>
        /// Executes the clear queued messages locked operation.
        /// </summary>
        private void ClearQueuedMessagesLocked()
        {
            pendingUdpRoutes.Clear();
            if (UDPSendingQueue.Count == 0)
                return;

            // Route registration is rare, so clearing retained payloads after failure stays off the hot path.
            ushort[] routeIDs = new ushort[UDPSendingQueue.Count];
            UDPSendingQueue.Keys.CopyTo(routeIDs, 0);
            for (int index = 0; index < routeIDs.Length; index++)
                UDPSendingQueue[routeIDs[index]] = null;
        }

        /// <summary>
        /// Executes the get server hub operation.
        /// </summary>
        private static ServerUdpHub GetServerHub(IPEndPoint localEndPoint)
        {
            return ServerHubs.GetOrAdd(localEndPoint.ToString(), key => new ServerUdpHub(key, localEndPoint));
        }

        /// <summary>
        /// Represents the server udp hub component.
        /// </summary>
        private class ServerUdpHub
        {
            /// <summary>
            /// Gets or sets the connection value.
            /// </summary>
            public UdpClient Connection { get; private set; }
            /// <summary>
            /// Stores the hub key value.
            /// </summary>
            private readonly string key;
            /// <summary>
            /// Stores the clients value.
            /// </summary>
            private ConcurrentDictionary<uint, UDPConnection> clients = new ConcurrentDictionary<uint, UDPConnection>();

            /// <summary>
            /// Executes the server udp hub operation.
            /// </summary>
            public ServerUdpHub(string key, IPEndPoint localEndPoint)
            {
                this.key = key;
                Connection = new UdpClient(localEndPoint);
                StartReceive();
            }

            /// <summary>
            /// Executes the register operation.
            /// </summary>
            public void Register(UDPConnection connection)
            {
                clients[connection.relatedClient.ID] = connection;
            }

            /// <summary>
            /// Executes the unregister operation.
            /// </summary>
            public void Unregister(uint clientID)
            {
                UDPConnection removed;
                clients.TryRemove(clientID, out removed);
                if (clients.IsEmpty)
                    DisposeIfUnused();
            }

            /// <summary>
            /// Disposes the shared UDP socket when no server clients still use it.
            /// </summary>
            private void DisposeIfUnused()
            {
                if (!clients.IsEmpty)
                    return;

                ServerUdpHub removed;
                if (ServerHubs.TryRemove(key, out removed) && object.ReferenceEquals(removed, this))
                {
                    try { Connection.Close(); } catch { }
                }
                else if (removed != null)
                {
                    ServerHubs.TryAdd(key, removed);
                }
            }

            /// <summary>
            /// Executes the start receive operation.
            /// </summary>
            private void StartReceive()
            {
                Connection.BeginReceive(OnReceiveUDP, null);
            }

            /// <summary>
            /// Routes, validates and dispatches one server-side UDP datagram.
            /// </summary>
            /// <param name="res">Asynchronous receive result.</param>
            private void OnReceiveUDP(IAsyncResult res)
            {
                try
                {
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] datagram = Connection.EndReceive(res, ref remoteEndPoint);

                    // ClientID is only a routing hint; the selected connection validates the negotiated envelope.
                    uint clientID;
                    if (!UdpDatagramAuthenticator.TryReadClientID(
                            datagram,
                            ConnectedClient.MinTcpMessageSize,
                            out clientID))
                        return;

                    UDPConnection clientConnection;
                    if (!clients.TryGetValue(clientID, out clientConnection))
                        return;

                    Interlocked.Add(ref clientConnection.receivedBytes, datagram.Length);
                    int payloadLength;
                    if (!clientConnection.TryDecodeDatagram(datagram, out payloadLength))
                        return;

                    // Parsing is safe only after validating the negotiated UDP envelope.
                    NetworkMessage message = new NetworkMessage();
                    if (!message.SafeSetDatagram(datagram, payloadLength))
                        return;

                    // The TCP-associated session is authoritative, even if the UDP header was altered.
                    message.Client = clientConnection.relatedClient;
                    message.ClientID = clientConnection.relatedClient.ID;

                    if (message.HeadID == (ushort)NetSquareMessageID.UdpRegister)
                    {
                        // MAC64 can authenticate rebinding; unprotected mode accepts only the first endpoint.
                        if (clientConnection.TryCompleteServerRegistration(message, remoteEndPoint))
                            clientConnection.SendRegistrationAcknowledgement();
                        return;
                    }

                    // Drop application datagrams until registration and source endpoint validation succeed.
                    if (!clientConnection.IsRegistrationCompleted ||
                        clientConnection.RemoteEndPoint == null ||
                        !clientConnection.RemoteEndPoint.Equals(remoteEndPoint))
                        return;

                    clientConnection.relatedClient.NbMessagesReceived++;
                    clientConnection.relatedClient.Fire_OnMessageReceived(message);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException) { }
                finally
                {
                    try { StartReceive(); } catch { }
                }
            }
        }
        #endregion
    }
}
