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
        /// Stores the message types array value.
        /// </summary>
        private ushort[] messageTypesArray;
        /// <summary>
        /// Stores the last message type index sended value.
        /// </summary>
        private int lastMessageTypeIndexSended;
        /// <summary>
        /// Stores the current sending message value.
        /// </summary>
        private byte[] currentSendingMessage = null;
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
            messageTypesArray = new ushort[0];
            lastMessageTypeIndexSended = 0;
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
            connection.BeginReceive(OnReceiveUDP, RemoteEndPoint);
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
            if (payload == null || payload.Length == 0)
                return;

            bool shouldStartSend = false;
            lock (sendLock)
            {
                if (!allowBeforeRegistration && !IsRegistrationCompleted)
                    return;

                if (!UDPSendingQueue.ContainsKey(headID))
                {
                    UDPSendingQueue.Add(headID, null);
                    Array.Resize(ref messageTypesArray, messageTypesArray.Length + 1);
                    messageTypesArray[messageTypesArray.Length - 1] = headID;
                }

                // Keep only the newest pending datagram for each route.
                if (isSendingUDPMessage)
                {
                    if (UDPSendingQueue[headID] != null)
                        Interlocked.Increment(ref nbMessagesDropped);
                    else
                        queuedUdpMessages++;

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
            try
            {
                byte[] datagram = connection.EndReceive(res, ref RemoteEndPoint);
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
            finally
            {
                try { connection.BeginReceive(OnReceiveUDP, RemoteEndPoint); } catch { }
            }
        }

        /// <summary>
        /// Executes the begin send message operation.
        /// </summary>
        private void BeginSendMessage(byte[] message)
        {
            try
            {
                // Protect only the payload that survived coalescing, preserving sequence order and CPU time.
                byte[] datagram = authenticator != null ? authenticator.Protect(message) : message;
                Interlocked.Add(ref sendedBytes, datagram.Length);
                if (isServer)
                    connection.BeginSend(datagram, datagram.Length, RemoteEndPoint, MessageSended, null);
                else
                    connection.BeginSend(datagram, datagram.Length, MessageSended, null);
            }
            catch (SocketException)
            {
                lock (sendLock)
                {
                    currentSendingMessage = null;
                    isSendingUDPMessage = false;
                    queuedUdpMessages = 0;
                    ClearQueuedMessagesLocked();
                    RefreshSendingCountLocked();
                }
            }
            catch (ObjectDisposedException)
            {
                lock (sendLock)
                {
                    currentSendingMessage = null;
                    isSendingUDPMessage = false;
                    queuedUdpMessages = 0;
                    ClearQueuedMessagesLocked();
                    RefreshSendingCountLocked();
                }
            }
        }

        /// <summary>
        /// Executes the message sended operation.
        /// </summary>
        private void MessageSended(IAsyncResult res)
        {
            try
            {
                connection.EndSend(res);
                NbMessagesSended++;

                // send other message if there is some
                byte[] nextMessage = null;
                lock (sendLock)
                {
                    currentSendingMessage = null;
                    if (GetNextSendingMessage(ref nextMessage))
                    {
                        currentSendingMessage = nextMessage;
                    }
                    else
                    {
                        isSendingUDPMessage = false;
                    }
                    RefreshSendingCountLocked();
                }

                if (nextMessage != null)
                    BeginSendMessage(nextMessage);
            }
            catch (SocketException)
            {
                lock (sendLock)
                {
                    currentSendingMessage = null;
                    isSendingUDPMessage = false;
                    queuedUdpMessages = 0;
                    ClearQueuedMessagesLocked();
                    RefreshSendingCountLocked();
                }
            }
            catch (ObjectDisposedException)
            {
                lock (sendLock)
                {
                    currentSendingMessage = null;
                    isSendingUDPMessage = false;
                    queuedUdpMessages = 0;
                    ClearQueuedMessagesLocked();
                    RefreshSendingCountLocked();
                }
            }
        }

        /// <summary>
        /// Executes the get next sending message operation.
        /// </summary>
        private bool GetNextSendingMessage(ref byte[] message)
        {
            if (messageTypesArray.Length == 0)
                return false;

            // switch to next index
            lastMessageTypeIndexSended++;
            lastMessageTypeIndexSended %= messageTypesArray.Length;

            int nbTry = 0;
            while (nbTry < messageTypesArray.Length)
            {
                if (UDPSendingQueue[messageTypesArray[lastMessageTypeIndexSended]] != null)
                {
                    message = UDPSendingQueue[messageTypesArray[lastMessageTypeIndexSended]];
                    UDPSendingQueue[messageTypesArray[lastMessageTypeIndexSended]] = null;
                    queuedUdpMessages--;
                    return true;
                }
                // switch to next index
                lastMessageTypeIndexSended++;
                lastMessageTypeIndexSended %= messageTypesArray.Length;
                nbTry++;
            }

            return false;
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
            for (int i = 0; i < messageTypesArray.Length; i++)
                UDPSendingQueue[messageTypesArray[i]] = null;
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
