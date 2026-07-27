using NetSquare.Core;
using NetSquare.Server.Utils;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Security.Authentication;

namespace NetSquare.Server
{
    /// <summary>
    /// Represents the tcp listener component.
    /// </summary>
    public class TcpListener
    {
        /// <summary>
        /// Gets or sets the started value.
        /// </summary>
        public bool Started { get { return Volatile.Read(ref started) != 0; } }
        /// <summary>
        /// Gets or sets the verifying clients value.
        /// </summary>
        public int VerifyingClients { get { return Volatile.Read(ref verifyingClients); } }
        /// <summary>
        /// Stores the atomic verifying client count.
        /// </summary>
        private int verifyingClients;
        /// <summary>
        /// Stores the atomic listener running state.
        /// </summary>
        private int started;
        /// <summary>
        /// Counts accepted connection workers, including queued ThreadPool work.
        /// </summary>
        private int pendingConnectionWorkers;
        /// <summary>
        /// Wakes periodic listener work during shutdown.
        /// </summary>
        private readonly ManualResetEventSlim stopSignal = new ManualResetEventSlim(false);
        /// <summary>
        /// Stores the socket accept worker.
        /// </summary>
        private Thread connectionThread;
        /// <summary>
        /// Stores the disconnected-client monitor worker.
        /// </summary>
        private Thread disconnectionThread;
        /// <summary>
        /// Gets or sets the check black list value.
        /// </summary>
        public bool CheckBlackList { get; private set; }
        /// <summary>
        /// Stores the listen backlog value.
        /// </summary>
        public static int ListenBacklog = 1024;
        /// <summary>
        /// Maximum time allowed for the client-first hello frame.
        /// </summary>
        public static int ClientHelloTimeoutMilliseconds = 2000;
        /// <summary>
        /// Maximum total duration of a recognized NetSquare handshake.
        /// </summary>
        public static int HandshakeTimeoutMilliseconds = 5000;
        /// <summary>
        /// Maximum concurrent handshakes accepted by one listener.
        /// </summary>
        public static int MaxConcurrentHandshakes = 256;
        /// <summary>
        /// Maximum concurrent handshakes accepted from one address.
        /// </summary>
        public static int MaxConcurrentHandshakesPerAddress = 4;
        /// <summary>
        /// Concurrent handshake count that activates client proof of work.
        /// </summary>
        public static int ProofOfWorkActivationThreshold = 32;
        /// <summary>
        /// Leading SHA-256 zero bits required while proof of work is active.
        /// </summary>
        public static byte ProofOfWorkDifficulty = 18;
        /// <summary>
        /// Tracks concurrent pre-authentication work by remote address.
        /// </summary>
        private readonly ConcurrentDictionary<string, int> verifyingClientsByAddress =
            new ConcurrentDictionary<string, int>();
        /// <summary>
        /// Synchronizes updates to per-address handshake counters.
        /// </summary>
        private readonly object handshakeLimitLock = new object();
        /// <summary>
        /// Stores the listener value.
        /// </summary>
        private TcpListenerEx _listener = null;
        /// <summary>
        /// Stores the server value.
        /// </summary>
        private NetSquareServer server = null;
        /// <summary>
        /// Gets or sets the ip address value.
        /// </summary>
        internal IPAddress IPAddress { get; private set; }
        /// <summary>
        /// Gets or sets the port value.
        /// </summary>
        internal int Port { get; private set; }
        /// <summary>
        /// Gets or sets the listener value.
        /// </summary>
        internal TcpListenerEx Listener { get { return _listener; } }

        /// <summary>
        /// Create a new TcpListener
        /// </summary>
        /// <param name="_server"> The server </param>
        /// <param name="ipAddress"> The ip address </param>
        /// <param name="port"> The port </param>
        /// <param name="checkBlackList"> Check if the client is blacklisted </param>
        public TcpListener(NetSquareServer _server, IPAddress ipAddress, int port, bool checkBlackList)
        {
            CheckBlackList = checkBlackList;
            Volatile.Write(ref started, 1);
            server = _server;
            IPAddress = ipAddress;
            Port = port;
            _listener = new TcpListenerEx(ipAddress, port);
            _listener.Start(ListenBacklog);
            connectionThread = new Thread(HandleConnection);
            connectionThread.IsBackground = true;
            connectionThread.Name = "NetSquare accept " + ipAddress;
            disconnectionThread = new Thread(HandleDisconnection);
            disconnectionThread.IsBackground = true;
            disconnectionThread.Name = "NetSquare disconnect monitor " + ipAddress;
            connectionThread.Start();
            disconnectionThread.Start();
        }

        /// <summary>
        /// Stop the listener
        /// </summary>
        public bool Stop()
        {
            // Repeated calls must keep waiting for workers that exceeded an earlier timeout.
            Interlocked.Exchange(ref started, 0);
            stopSignal.Set();
            try { _listener.Stop(); } catch { }

            int configuredTimeout = NetSquareConfigurationManager
                .Get<NetSquareConfiguration>().WorkerStopTimeoutMilliseconds;
            int timeout = configuredTimeout > 0 ? configuredTimeout : 5000;

            bool connectionStopped = connectionThread == null ||
                (connectionThread != Thread.CurrentThread &&
                    (!connectionThread.IsAlive || connectionThread.Join(timeout)));
            bool disconnectionStopped = disconnectionThread == null ||
                (disconnectionThread != Thread.CurrentThread &&
                    (!disconnectionThread.IsAlive || disconnectionThread.Join(timeout)));

            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeout);
            while (Volatile.Read(ref pendingConnectionWorkers) > 0 && DateTime.UtcNow < deadlineUtc)
                Thread.Sleep(1);

            return connectionStopped &&
                disconnectionStopped &&
                Volatile.Read(ref pendingConnectionWorkers) == 0;
        }

        /// <summary>
        /// Loop to handle new clients Connection
        /// </summary>
        private void HandleConnection()
        {
            while (Started)
            {
                try
                {
                    Socket newClient = _listener.AcceptTcpClient().Client;
                    newClient.NoDelay = true;
                    QueueConnectionValidation(newClient);
                }
                catch (SocketException ex)
                {
                    if (Started)
                        Writer.Write("Fail to accept client : " + ex.ToString(), ConsoleColor.Red);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Queues one accepted socket for validation and tracks it until completion.
        /// </summary>
        /// <param name="newClient">Accepted socket.</param>
        private void QueueConnectionValidation(Socket newClient)
        {
            if (!Started)
            {
                CloseUnvalidatedSocket(newClient);
                return;
            }

            Interlocked.Increment(ref pendingConnectionWorkers);
            bool queued = false;
            try
            {
                queued = ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
                    {
                        AcceptConnection(state);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref pendingConnectionWorkers);
                    }
                }, newClient);
            }
            finally
            {
                if (!queued)
                {
                    Interlocked.Decrement(ref pendingConnectionWorkers);
                    CloseUnvalidatedSocket(newClient);
                }
            }
        }

        /// <summary>
        /// Accept a new connection
        /// </summary>
        /// <param name="sender"> The sender </param>
        private void AcceptConnection(object sender)
        {
            Socket newClient = (Socket)sender;
            if (!Started)
            {
                CloseUnvalidatedSocket(newClient);
                return;
            }

            string remoteAddress = IPAddressUtilities.GetRemoteAddress(newClient);
            if (CheckBlackList && BlackListManager.IsBlackListed(newClient))
            {
                BlackListStatus status = BlackListManager.GetStatus(remoteAddress);
                if (server.UseTLS)
                {
                    // Banned peers are dropped before the expensive TLS handshake and receive no raw TLS-invalid frame.
                    CloseUnvalidatedSocket(newClient);
                }
                else
                    RejectConnection(newClient, BlackListConnectionFeedback.CreateRejection(status));
                return;
            }

            if (!TryEnterHandshake(remoteAddress))
            {
                // Capacity rejection stays silent before a valid NetSquare marker to avoid scanner feedback.
                CloseUnvalidatedSocket(newClient);
                return;
            }

            try
            {
                Stream handshakeStream = CreateHandshakeStream(newClient);
                ValidateClient(newClient, handshakeStream);
            }
            catch (AuthenticationException)
            {
                // Invalid TLS clients receive no NetSquare protocol fingerprint.
                CloseUnvalidatedSocket(newClient);
            }
            catch (IOException)
            {
                // Closed or malformed TLS streams are rejected before NetSquare validation.
                CloseUnvalidatedSocket(newClient);
            }
            finally
            {
                ExitHandshake(remoteAddress);
            }
        }

        /// <summary>
        /// Loop to handle clients Disconnection
        /// </summary>
        private void HandleDisconnection()
        {
            while (Started)
            {
                // Handle Disconnect
                var ids = server.Clients.Keys;
                foreach (uint clientID in ids)
                {
                    try
                    {
                        ConnectedClient client;
                        if (server.Clients.TryGetValue(clientID, out client))
                        {
                            if (!client.IsConnected())
                                server.Server_ClientDisconnected(client);
                            else if (client.HasHeartbeatTimedOut)
                                server.DisconnectClient(client, DisconnectReason.Timeout);
                        }
                    }
                    catch (Exception ex)
                    {
                        Writer.Write("Fail to disconnect client " + clientID + "  : " + ex.ToString(), ConsoleColor.Red);
                    }
                }

                if (stopSignal.Wait(1000))
                    return;
            }
        }


        /// <summary>
        /// Reserves one bounded handshake slot globally and for a remote address.
        /// </summary>
        /// <param name="remoteAddress">Normalized remote address.</param>
        /// <returns>True when the connection may start its handshake.</returns>
        private bool TryEnterHandshake(string remoteAddress)
        {
            // A single lock keeps address removal race-free while the total remains atomically observable.
            lock (handshakeLimitLock)
            {
                if (VerifyingClients >= Math.Max(1, MaxConcurrentHandshakes))
                    return false;

                int addressCount;
                verifyingClientsByAddress.TryGetValue(remoteAddress, out addressCount);
                if (addressCount >= Math.Max(1, MaxConcurrentHandshakesPerAddress))
                    return false;

                verifyingClientsByAddress[remoteAddress] = addressCount + 1;
                Interlocked.Increment(ref verifyingClients);
                return true;
            }
        }

        /// <summary>
        /// Releases one global and per-address handshake slot.
        /// </summary>
        /// <param name="remoteAddress">Normalized remote address.</param>
        private void ExitHandshake(string remoteAddress)
        {
            // Remove zero counters so arbitrary scanner addresses cannot grow the dictionary forever.
            lock (handshakeLimitLock)
            {
                int addressCount;
                if (verifyingClientsByAddress.TryGetValue(remoteAddress, out addressCount))
                {
                    if (addressCount <= 1)
                    {
                        int removed;
                        verifyingClientsByAddress.TryRemove(remoteAddress, out removed);
                    }
                    else
                    {
                        verifyingClientsByAddress[remoteAddress] = addressCount - 1;
                    }
                }

                Interlocked.Decrement(ref verifyingClients);
            }
        }

        /// <summary>
        /// Closes a socket without sending protocol-identifying bytes.
        /// </summary>
        /// <param name="client">Unvalidated socket.</param>
        private static void CloseUnvalidatedSocket(Socket client)
        {
            // Generic crawlers receive no marker or detailed reason before proving NetSquare awareness.
            if (client == null)
                return;

            try { client.Close(); } catch { }
            try { client.Dispose(); } catch { }
        }

        /// <summary>
        /// Creates the stream used by the NetSquare handshake and enables TLS when configured.
        /// </summary>
        /// <param name="client">Accepted TCP socket.</param>
        /// <returns>A raw NetworkStream or an authenticated SslStream.</returns>
        private Stream CreateHandshakeStream(Socket client)
        {
            // Keep the raw socket path unchanged when TLS is disabled.
            NetworkStream networkStream = new NetworkStream(client, false);
            if (!server.UseTLS)
                return networkStream;

            SslStream tlsStream = new SslStream(networkStream, false);
            int previousReceiveTimeout = client.ReceiveTimeout;
            int previousSendTimeout = client.SendTimeout;
            try
            {
                int tlsTimeout = Math.Max(1000, HandshakeTimeoutMilliseconds);
                client.ReceiveTimeout = tlsTimeout;
                client.SendTimeout = tlsTimeout;
                tlsStream.AuthenticateAsServer(
                    server.TLSCertificate,
                    false,
                    SslProtocols.Tls12,
                    false);
                return tlsStream;
            }
            finally
            {
                // Application and NetSquare handshake deadlines manage timeouts after TLS authentication.
                client.ReceiveTimeout = previousReceiveTimeout;
                client.SendTimeout = previousSendTimeout;
            }
        }

        #region Handshake Security
        /// <summary>
        /// Handles a wrong handshake answer and returns the resulting rejection reason.
        /// </summary>
        /// <param name="client">Client socket.</param>
        /// <returns>A ban rejection when the hit threshold was reached, otherwise an invalid-handshake rejection.</returns>
        private ConnectionRejectionInfo HandleWrongHandshake(Socket client)
        {
            // Reuse the shared hit engine so application and transport violations follow the same policy.
            string remoteAddress = IPAddressUtilities.GetRemoteAddress(client);
            const string reason = "Invalid NetSquare handshake proof or frame";
            Writer.Write("Client " + remoteAddress + " sent an invalid handshake frame.", ConsoleColor.Red);

            if (CheckBlackList)
            {
                BlackListHitResult result = BlackListManager.AddHit(remoteAddress, 1, reason);
                if (result.IsBanned)
                    return BlackListConnectionFeedback.CreateRejection(BlackListManager.GetStatus(remoteAddress));
            }

            return new ConnectionRejectionInfo(ConnectionRejectionReason.InvalidHandshake, reason);
        }

        #endregion

        /// <summary>
        /// Validates a new client with the strict client-first NetSquare handshake.
        /// </summary>
        /// <param name="client">Unvalidated TCP socket.</param>
        /// <param name="clientStream">Raw or TLS-authenticated handshake stream.</param>
        private void ValidateClient(Socket client, Stream clientStream)
        {
            bool netSquareClientIdentified = false;
            bool clientAdded = false;
            ConnectedClient connectedClient = null;
            // Every recognized rejection must use the same raw or TLS stream selected for this connection.
            Action<ConnectionRejectionInfo> rejectConnection = info => RejectConnection(client, info, clientStream);
            try
            {
                // Generic scanners must send a complete NetSquare marker within the short first-stage deadline.
                DateTime helloDeadlineUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(250, ClientHelloTimeoutMilliseconds));
                byte[] clientHelloFrame = NetSquareHandshakeProtocol.ReceiveExact(
                    clientStream,
                    NetSquareHandshakeProtocol.ClientHelloLength,
                    helloDeadlineUtc);

                HandshakeClientHello clientHello;
                try
                {
                    clientHello = NetSquareHandshakeProtocol.DeserializeClientHello(clientHelloFrame);
                }
                catch (InvalidOperationException)
                {
                    // Invalid first bytes receive no NetSquare response and count as one blacklist hit.
                    HandleWrongHandshake(client);
                    CloseUnvalidatedSocket(client);
                    return;
                }

                netSquareClientIdentified = true;
                DateTime handshakeDeadlineUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(1000, HandshakeTimeoutMilliseconds));
                Version serverVersion = typeof(NetSquareHandshakeProtocol).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

                // Exact package versions are mandatory until an explicit compatibility range is introduced.
                if (!serverVersion.Equals(clientHello.LibraryVersion) ||
                    clientHello.MinimumWireProtocolVersion > NetSquareHandshakeProtocol.WireProtocolVersion ||
                    clientHello.MaximumWireProtocolVersion < NetSquareHandshakeProtocol.WireProtocolVersion)
                {
                    rejectConnection(
                        new ConnectionRejectionInfo(
                            ConnectionRejectionReason.ProtocolMismatch,
                            "Client NetSquare " + clientHello.LibraryVersion +
                            " is incompatible with server NetSquare " + serverVersion + "."));
                    return;
                }

                if (clientHello.RequestedTransport == NetSquareProtocoleType.TCP_AND_UDP &&
                    server.ProtocoleType != NetSquareProtocoleType.TCP_AND_UDP)
                {
                    rejectConnection(
                        new ConnectionRejectionInfo(
                            ConnectionRejectionReason.ProtocolMismatch,
                            "The server does not support the requested TCP and UDP transport."));
                    return;
                }

                NetSquareProtocoleType selectedTransport = clientHello.RequestedTransport;
                HandshakeCapabilities selectedCapabilities =
                    clientHello.Capabilities & NetSquareHandshakeProtocol.SupportedCapabilities;
                HandshakeCapabilities requiredCapabilities =
                    HandshakeCapabilities.HighPrecisionTimeSynchronization;
                if (server.HeartbeatEnabled)
                    requiredCapabilities |= HandshakeCapabilities.Heartbeat;
                if (selectedTransport == NetSquareProtocoleType.TCP_AND_UDP && server.UseUdpAuthentication)
                    requiredCapabilities |= HandshakeCapabilities.AuthenticatedUdpDatagrams;
                else
                    selectedCapabilities &= ~HandshakeCapabilities.AuthenticatedUdpDatagrams;
                if ((selectedCapabilities & requiredCapabilities) != requiredCapabilities)
                {
                    rejectConnection(
                        new ConnectionRejectionInfo(
                            ConnectionRejectionReason.ProtocolMismatch,
                            "The client does not support the capabilities required by this server."));
                    return;
                }

                byte proofDifficulty = VerifyingClients >= Math.Max(1, ProofOfWorkActivationThreshold)
                    ? (byte)Math.Min(ProofOfWorkDifficulty, NetSquareHandshakeProtocol.MaximumProofOfWorkDifficulty)
                    : (byte)0;
                byte[] serverChallengeFrame = NetSquareHandshakeProtocol.CreateServerChallenge(
                    clientHelloFrame,
                    selectedTransport,
                    selectedCapabilities,
                    proofDifficulty);
                NetSquareHandshakeProtocol.SendAll(clientStream, serverChallengeFrame);

                byte[] clientProofFrame = NetSquareHandshakeProtocol.ReceiveExact(
                    clientStream,
                    NetSquareHandshakeProtocol.ClientProofLength,
                    handshakeDeadlineUtc);
                if (!NetSquareHandshakeProtocol.ValidateClientProof(
                    clientHelloFrame,
                    serverChallengeFrame,
                    clientProofFrame))
                {
                    rejectConnection(HandleWrongHandshake(client));
                    return;
                }

                byte[] sessionToken = NetSquareHandshakeProtocol.CreateRandomBytes(NetSquareHandshakeProtocol.NonceLength);
                byte[] serverAcceptFrame = NetSquareHandshakeProtocol.CreateServerAccept(
                    clientHelloFrame,
                    serverChallengeFrame,
                    clientProofFrame,
                    selectedTransport,
                    selectedCapabilities,
                    sessionToken);
                NetSquareHandshakeProtocol.SendAll(clientStream, serverAcceptFrame);

                byte[] clientReadyFrame = NetSquareHandshakeProtocol.ReceiveExact(
                    clientStream,
                    NetSquareHandshakeProtocol.ClientReadyLength,
                    handshakeDeadlineUtc);
                if (!NetSquareHandshakeProtocol.ValidateClientReady(
                    clientHelloFrame,
                    serverChallengeFrame,
                    clientProofFrame,
                    serverAcceptFrame,
                    clientReadyFrame))
                {
                    rejectConnection(HandleWrongHandshake(client));
                    return;
                }

                // The client enters the public server collection only after its final ReadyAck is valid.
                connectedClient = new ConnectedClient
                {
                    HeartbeatTimeoutMilliseconds = server.HeartbeatEnabled
                        ? server.HeartbeatTimeoutMilliseconds : 0
                };
                bool enableUdp = selectedTransport == NetSquareProtocoleType.TCP_AND_UDP;
                connectedClient.SetClient(
                    client,
                    false,
                    enableUdp,
                    (selectedCapabilities & HandshakeCapabilities.AuthenticatedUdpDatagrams) != 0
                        ? sessionToken
                        : null,
                    server.UseTLS ? clientStream : null);
                uint clientID = server.AddClient(connectedClient);
                clientAdded = true;
                if (!connectedClient.IsConnected())
                    throw new SocketException((int)SocketError.ConnectionReset);

                NetSquareHandshakeProtocol.SendAll(
                    clientStream,
                    NetSquareHandshakeProtocol.CreateServerConnected(
                        clientID,
                        server.HeartbeatEnabled,
                        server.HeartbeatIntervalMilliseconds,
                        server.HeartbeatTimeoutMilliseconds,
                        clientReadyFrame));

                if (enableUdp)
                    WaitForUdpRegistration(connectedClient, handshakeDeadlineUtc);

                // Application code sees only clients whose negotiated transports are fully usable.
                server.Server_ClientConnected(server.GetClient(clientID), clientID);
            }
            catch (TimeoutException)
            {
                if (clientAdded)
                    server.RemovePendingClient(connectedClient);
                else if (netSquareClientIdentified)
                    rejectConnection(
                        new ConnectionRejectionInfo(
                            ConnectionRejectionReason.HandshakeTimeout,
                            "The NetSquare handshake timed out."));
                else
                    CloseUnvalidatedSocket(client);
            }
            catch (InvalidOperationException)
            {
                ConnectionRejectionInfo rejection = HandleWrongHandshake(client);
                if (clientAdded)
                    server.RemovePendingClient(connectedClient);
                else if (netSquareClientIdentified)
                    rejectConnection(rejection);
                else
                    CloseUnvalidatedSocket(client);
            }
            catch (Exception ex)
            {
                Writer.Write("Fail to handshake client : " + ex.ToString(), ConsoleColor.Red);
                if (clientAdded)
                    server.RemovePendingClient(connectedClient);
                else if (connectedClient != null)
                    CloseUnvalidatedSocket(client);
                else if (netSquareClientIdentified)
                    rejectConnection(
                        new ConnectionRejectionInfo(ConnectionRejectionReason.ServerError, ex.Message));
                else
                    CloseUnvalidatedSocket(client);
            }
        }

        /// <summary>
        /// Waits until the UDP endpoint completes its negotiated registration mode.
        /// </summary>
        /// <param name="client">Pending connected client.</param>
        /// <param name="deadlineUtc">Overall handshake deadline.</param>
        private static void WaitForUdpRegistration(ConnectedClient client, DateTime deadlineUtc)
        {
            // TCP remains monitored while the shared UDP hub validates the registration datagram.
            while (DateTime.UtcNow < deadlineUtc)
            {
                if (client == null || !client.IsConnected())
                    throw new SocketException((int)SocketError.ConnectionReset);
                if (client.UDP != null && client.UDP.IsRegistrationCompleted)
                    return;
                Thread.Sleep(1);
            }

            throw new TimeoutException("The NetSquare UDP registration timed out.");
        }

        /// <summary>
        /// Sends connection feedback synchronously and then closes the unvalidated socket.
        /// </summary>
        /// <param name="client">Socket to reject.</param>
        /// <param name="info">Typed rejection information.</param>
        /// <param name="clientStream">Authenticated stream when rejection must remain inside TLS.</param>
        private void RejectConnection(Socket client, ConnectionRejectionInfo info, Stream clientStream = null)
        {
            if (client == null)
                return;

            try
            {
                if (client.Connected)
                {
                    if (clientStream != null)
                    {
                        // Recognized TLS peers receive feedback only inside their encrypted channel.
                        ConnectionFeedbackProtocol.SendConnectionRejection(clientStream, info);
                    }
                    else
                    {
                        ConnectionFeedbackProtocol.SendConnectionRejection(client, info);
                        try { client.Shutdown(SocketShutdown.Send); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Writer.Write("Fail to send connection rejection feedback : " + ex.Message, ConsoleColor.DarkYellow);
            }
            finally
            {
                try { client.Close(); } catch { }
                try { clientStream?.Dispose(); } catch { }
                try { client.Dispose(); } catch { }
            }
        }
    }
}
