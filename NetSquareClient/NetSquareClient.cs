using NetSquare.Core;
using NetSquare.Core.Collections;
using NetSquare.Core.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace NetSquare.Client
{
    /// <summary>
    /// Represents the net square client component.
    /// </summary>
    public class NetSquareClient
    {
        #region Events
        /// <summary>
        /// Occurs when disconected is raised.
        /// </summary>
        public event Action OnDisconected;
        /// <summary>
        /// Occurs when an established connection ends with typed feedback.
        /// </summary>
        public event Action<DisconnectInfo> OnDisconnected;
        /// <summary>
        /// Occurs when the server refuses a connection before assigning a client ID.
        /// </summary>
        public event Action<ConnectionRejectionInfo> OnConnectionRejected;
        /// <summary>
        /// Occurs when connected is raised.
        /// </summary>
        public event Action<uint> OnConnected;
        /// <summary>
        /// Occurs when connection fail is raised.
        /// </summary>
        public event Action OnConnectionFail;
        /// <summary>
        /// Occurs when unregistered message received is raised.
        /// </summary>
        public event Action<NetworkMessage> OnUnregisteredMessageReceived;
        /// <summary>
        /// Occurs when exception is raised.
        /// </summary>
        public event Action<Exception> OnException;
        #endregion

        #region Variables
        /// <summary>
        /// Stores the dispatcher value.
        /// </summary>
        public NetSquareDispatcher Dispatcher;
        /// <summary>
        /// Gets or sets the worlds manager value.
        /// </summary>
        public WorldsManager WorldsManager { get; private set; }
        /// <summary>
        /// Gets or sets the client value.
        /// </summary>
        public ConnectedClient Client { get; private set; }
        /// <summary>
        /// Gets or sets the port value.
        /// </summary>
        public int Port { get; private set; }
        /// <summary>
        /// Gets or sets the default connection timeout in milliseconds.
        /// </summary>
        public int ConnectionTimeoutMilliseconds { get; set; }
        /// <summary>
        /// Gets or sets the ip adress value.
        /// </summary>
        public string IPAdress { get; private set; }
        /// <summary>
        /// Gets or sets the client id value.
        /// </summary>
        public uint ClientID { get { return Client != null ? Client.ID : 0; } }
        /// <summary>
        /// Gets or sets the is connected value.
        /// </summary>
        public bool IsConnected { get { return Client?.TcpSocket?.Connected ?? false; } }
        /// <summary>
        /// Gets or sets the nb sending messages value.
        /// </summary>
        public int NbSendingMessages { get { return Client != null ? Client.NbMessagesToSend : 0; } }
        /// <summary>
        /// Gets or sets the nb processing messages value.
        /// </summary>
        public int NbProcessingMessages { get { return messagesQueue.Count; } }
        /// <summary>
        /// Stores the protocole type value.
        /// </summary>
        public NetSquareProtocoleType ProtocoleType;
        /// <summary>
        /// Gets the configuration currently applied to this client.
        /// </summary>
        public NetSquareClientConfiguration Configuration { get; private set; }
        /// <summary>
        /// Gets or sets whether the complete TCP connection uses TLS.
        /// </summary>
        public bool UseTLS { get; set; }
        /// <summary>
        /// Gets or sets whether UDP datagrams use sequence and MAC64 authentication.
        /// </summary>
        public bool UseUdpAuthentication { get; set; }
        /// <summary>
        /// Gets or sets the optional DNS name validated instead of the connection host.
        /// </summary>
        public string TLSServerName { get; set; }
        /// <summary>
        /// Gets or sets optional custom server certificate validation for private certificate authorities.
        /// </summary>
        public RemoteCertificateValidationCallback TLSCertificateValidationCallback { get; set; }
        /// <summary>
        /// Gets or sets the is time synchonized value.
        /// </summary>
        public bool IsTimeSynchonized { get { return hasServerTimeOffset; } }
        /// <summary>
        /// Gets whether the server time is synchronized.
        /// </summary>
        public bool IsTimeSynchronized { get { return hasServerTimeOffset; } }
        /// <summary>
        /// Gets whether automatic server time synchronization is enabled.
        /// </summary>
        public bool IsAutoTimeSynchronizationEnabled { get { return isAutoSynchronizingTime; } }
        /// <summary>
        /// Stores the is synchronizing time value.
        /// </summary>
        private volatile bool isSynchronizingTime = false;
        /// <summary>
        /// Gets or sets the server time offset value.
        /// </summary>
        public float ServerTimeOffset { get; private set; }
        /// <summary>
        /// Gets the target server time offset used by smoothing.
        /// </summary>
        public float TargetServerTimeOffset { get; private set; }
        /// <summary>
        /// Gets or sets whether server time offset changes are smoothed.
        /// </summary>
        public bool SmoothServerTimeOffset { get; set; }
        /// <summary>
        /// Gets or sets the server time offset smoothing speed.
        /// </summary>
        public float ServerTimeOffsetSmoothingSpeed { get; set; }
        /// <summary>
        /// Gets or sets the timeout for one time synchronization request.
        /// </summary>
        public int TimeSynchronizationRequestTimeoutMs { get; set; }
        /// <summary>
        /// Gets or sets the maximum request attempts for one synchronization. Use 0 to derive it from precision.
        /// </summary>
        public int TimeSynchronizationMaxAttempts { get; set; }
        /// <summary>
        /// Gets the current automatic time synchronization interval.
        /// </summary>
        public int AutoTimeSynchronizationIntervalMs { get; private set; }
        /// <summary>
        /// Gets when the server time offset was last synchronized.
        /// </summary>
        public DateTime LastServerTimeSynchronizationUtc { get; private set; }
        /// <summary>
        /// Gets the last measured TCP ping in milliseconds.
        /// </summary>
        public ushort Ping { get; private set; }
        /// <summary>
        /// Gets whether the server requires TCP heartbeats.
        /// </summary>
        public bool HeartbeatEnabled { get; private set; }
        /// <summary>
        /// Gets the server-provided heartbeat interval in milliseconds.
        /// </summary>
        public int HeartbeatIntervalMs { get; private set; }
        /// <summary>
        /// Gets the server-provided heartbeat reply timeout in milliseconds.
        /// </summary>
        public int HeartbeatTimeoutMs { get; private set; }
        /// <summary>
        /// Gets when the last heartbeat reply was received.
        /// </summary>
        public DateTime LastHeartbeatUtc { get; private set; }
        /// <summary>
        /// Stores whether server time was synchronized at least once.
        /// </summary>
        private bool hasServerTimeOffset;
        /// <summary>
        /// Stores the last server time offset update timestamp.
        /// </summary>
        private DateTime lastServerTimeOffsetUpdateUtc;
        /// <summary>
        /// Stores the time synchronization lock value.
        /// </summary>
        private readonly object timeSynchronizationLock = new object();
        /// <summary>
        /// Stores the active time synchronization generation value.
        /// </summary>
        private volatile int timeSynchronizationGeneration;
        /// <summary>
        /// Stores the automatic time synchronization lock value.
        /// </summary>
        private readonly object autoTimeSynchronizationLock = new object();
        /// <summary>
        /// Stores whether automatic server time synchronization is running.
        /// </summary>
        private volatile bool isAutoSynchronizingTime;
        /// <summary>
        /// Stores the automatic time synchronization thread.
        /// </summary>
        private Thread autoTimeSynchronizationThread;
        /// <summary>
        /// Signals the automatic time synchronization thread to stop.
        /// </summary>
        private ManualResetEventSlim autoTimeSynchronizationStopSignal = new ManualResetEventSlim(false);
        /// <summary>
        /// Stores the high precision time synchronization protocol version.
        /// </summary>
        private const byte HighPrecisionTimeSynchronizationVersion = 1;
        /// <summary>
        /// Stores the heartbeat protocol version.
        /// </summary>
        private const byte HeartbeatProtocolVersion = 1;
        /// <summary>
        /// Stores whether the heartbeat loop is running.
        /// </summary>
        private volatile bool isHeartbeatRunning;
        /// <summary>
        /// Stores the heartbeat thread.
        /// </summary>
        private Thread heartbeatThread;
        /// <summary>
        /// Signals the heartbeat thread to stop.
        /// </summary>
        private ManualResetEventSlim heartbeatStopSignal = new ManualResetEventSlim(false);
        /// <summary>
        /// Stores the heartbeat lock value.
        /// </summary>
        private readonly object heartbeatLock = new object();
        /// <summary>
        /// Represents one server time synchronization sample.
        /// </summary>
        private struct TimeSynchronizationSample
        {
            public float Offset;
            public float RoundTrip;
        }

        /// <summary>
        /// Stores the nb reply asked value.
        /// </summary>
        private uint nbReplyAsked = 0;
        /// <summary>
        /// Stores the reply id lock value.
        /// </summary>
        private readonly object replyIDLock = new object();
        /// <summary>
        /// Stores the messages queue value.
        /// </summary>
        private BoundedConcurrentQueue<NetworkMessage> messagesQueue =
            new BoundedConcurrentQueue<NetworkMessage>(8192);
        /// <summary>
        /// Stores the message processing worker.
        /// </summary>
        private Thread messageProcessingThread;
        /// <summary>
        /// Cancels the message processing worker after a graceful shutdown timeout.
        /// </summary>
        private CancellationTokenSource messageProcessingCancellation;
        /// <summary>
        /// Stores pending reply callbacks under the reply ID lock.
        /// </summary>
        private readonly Dictionary<uint, PendingReplyCallback> pendingReplyCallbacks =
            new Dictionary<uint, PendingReplyCallback>();
        /// <summary>
        /// Reuses reply IDs collected during expiration cleanup.
        /// </summary>
        private readonly List<uint> expiredReplyCallbackIDs = new List<uint>();
        /// <summary>
        /// Periodically removes callbacks whose remote reply never arrived.
        /// </summary>
        private Timer replyCallbackCleanupTimer;
        /// <summary>
        /// Stores the applied maximum number of pending reply callbacks.
        /// </summary>
        private int maxPendingReplyCallbacks = 4096;
        /// <summary>
        /// Stores the applied callback timeout in milliseconds.
        /// </summary>
        private int replyCallbackTimeoutMilliseconds = 30000;
        /// <summary>
        /// Stores the callback cleanup frequency in milliseconds.
        /// </summary>
        private const int ReplyCallbackCleanupIntervalMilliseconds = 1000;
        /// <summary>
        /// Stores the disconnect started value.
        /// </summary>
        private int disconnectStarted = 1;
        /// <summary>
        /// Stores whether a connection attempt is already active.
        /// </summary>
        private int connectionAttemptActive;
        /// <summary>
        /// Synchronizes access to the active connection cancellation source.
        /// </summary>
        private readonly object connectionAttemptLock = new object();
        /// <summary>
        /// Cancels the active connection attempt when requested by the client.
        /// </summary>
        private CancellationTokenSource activeConnectionAttemptCancellation;
        /// <summary>
        /// Stores the disconnect notice timeout ms value.
        /// </summary>
        public static int DisconnectNoticeTimeoutMs = 500;
        /// <summary>
        /// Stores the types dic value.
        /// </summary>
        private static Dictionary<Type, Action<NetworkMessage, object>> typesDic;
        #endregion

        /// <summary>
        /// Instantiate a new NetSquare client
        /// </summary>
        /// <param name="autoBindNetsquareActions">If true, will automatically bind all NetSquareActions from the assembly</param>
        public NetSquareClient(bool autoBindNetsquareActions = true)
        {
            Dispatcher = new NetSquareDispatcher();
            SmoothServerTimeOffset = true;
            ServerTimeOffsetSmoothingSpeed = 8f;
            TimeSynchronizationRequestTimeoutMs = 1500;
            TimeSynchronizationMaxAttempts = 0;
            AutoTimeSynchronizationIntervalMs = 30000;
            ConnectionTimeoutMilliseconds = 30000;
            LastHeartbeatUtc = DateTime.MinValue;
            lastServerTimeOffsetUpdateUtc = DateTime.UtcNow;
            // TLS is opt-in so existing clients keep their current connection behavior.
            UseTLS = false;
            UseUdpAuthentication = false;
            LastServerTimeSynchronizationUtc = DateTime.MinValue;
            TLSServerName = string.Empty;
            if (autoBindNetsquareActions)
                Dispatcher.AutoBindHeadActionsFromAttributes();
            WorldsManager = new WorldsManager(this);

            // initiate Type Dictionnary
            typesDic = new Dictionary<Type, Action<NetworkMessage, object>>
            {
                { typeof(short), (message, item) => { message.Set((short)Convert.ChangeType(item, typeof(short))); } },
                { typeof(int), (message, item) => { message.Set((int)Convert.ChangeType(item, typeof(int))); } },
                { typeof(long), (message, item) => { message.Set((long)Convert.ChangeType(item, typeof(long))); } },
                { typeof(float), (message, item) => { message.Set((float)Convert.ChangeType(item, typeof(float))); } },
                { typeof(double), (message, item) => { message.Set((double)Convert.ChangeType(item, typeof(double))); } },
                { typeof(ushort), (message, item) => { message.Set((ushort)Convert.ChangeType(item, typeof(ushort))); } },
                { typeof(uint), (message, item) => { message.Set((uint)Convert.ChangeType(item, typeof(uint))); } },
                { typeof(ulong), (message, item) => { message.Set((ulong)Convert.ChangeType(item, typeof(ulong))); } },
                { typeof(UInt24), (message, item) => { message.Set((UInt24)Convert.ChangeType(item, typeof(UInt24))); } },
                { typeof(bool), (message, item) => { message.Set((bool)Convert.ChangeType(item, typeof(bool))); } },
                { typeof(string), (message, item) => { message.Set((string)Convert.ChangeType(item, typeof(string))); } },
                { typeof(char), (message, item) => { message.Set((char)Convert.ChangeType(item, typeof(char))); } },
                { typeof(byte[]), (message, item) => { message.Set((byte[])Convert.ChangeType(item, typeof(byte[]))); } }
            };
            Configuration = new NetSquareClientConfiguration();
            ApplyConfiguration(Configuration);
        }

        /// <summary>
        /// Instantiates a NetSquare client and applies a strongly typed configuration.
        /// </summary>
        /// <param name="configuration">Client settings loaded from JSON or created in code.</param>
        /// <param name="autoBindNetsquareActions">Whether attributed actions are bound automatically.</param>
        public NetSquareClient(NetSquareClientConfiguration configuration, bool autoBindNetsquareActions = true)
            : this(autoBindNetsquareActions)
        {
            // Apply JSON settings after the client subsystems have been initialized.
            ApplyConfiguration(configuration);
        }

        #region Connection / Disconnection
        /// <summary>
        /// Applies a client configuration while no connection is active.
        /// </summary>
        /// <param name="configuration">Configuration to validate and apply.</param>
        public void ApplyConfiguration(NetSquareClientConfiguration configuration)
        {
            // Runtime transport changes during a connection would desynchronize the active socket state.
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (IsConnected || Volatile.Read(ref connectionAttemptActive) != 0)
            {
                throw new InvalidOperationException(
                    "A NetSquare client configuration can only be applied while disconnected.");
            }

            configuration.Validate();
            Configuration = configuration;
            IPAdress = configuration.Host;
            Port = configuration.Port;
            ProtocoleType = configuration.ProtocoleType;
            ConnectionTimeoutMilliseconds = configuration.ConnectionTimeoutMilliseconds;
            maxPendingReplyCallbacks = configuration.MaxPendingReplyCallbacks;
            replyCallbackTimeoutMilliseconds = configuration.ReplyCallbackTimeoutMilliseconds;
            UseTLS = configuration.UseTLS;
            UseUdpAuthentication = configuration.UseUdpAuthentication;
            TLSServerName = configuration.TLSServerName ?? string.Empty;
            SmoothServerTimeOffset = configuration.SmoothServerTimeOffset;
            ServerTimeOffsetSmoothingSpeed = configuration.ServerTimeOffsetSmoothingSpeed;
            TimeSynchronizationRequestTimeoutMs =
                configuration.TimeSynchronizationRequestTimeoutMilliseconds;
            TimeSynchronizationMaxAttempts = configuration.TimeSynchronizationMaxAttempts;
            WorldsManager.SynchronizationTransport = configuration.SynchronizationTransport;
            WorldsManager.MaxStoredSynchFrames = configuration.MaxStoredSynchronizationFrames;
            WorldsManager.AutoSendFrames = configuration.AutoSendSynchronizationFrames;
        }

        /// <summary>
        /// Starts a connection using the currently applied client configuration.
        /// </summary>
        public void Connect()
        {
            // Reapply the referenced configuration so edits made after loading are honored.
            ApplyConfiguration(Configuration);
            Connect(
                Configuration.Host,
                Configuration.Port,
                Configuration.ProtocoleType,
                Configuration.SynchronizationTransport);
        }

        /// <summary>
        /// Connects asynchronously using the currently applied client configuration.
        /// </summary>
        /// <param name="cancellationToken">Token used to cancel the connection attempt.</param>
        /// <returns>The final typed connection result.</returns>
        public Task<ConnectionResult> ConnectAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Use the same configured values as the non-blocking configured connection API.
            ApplyConfiguration(Configuration);
            return ConnectAsync(
                Configuration.Host,
                Configuration.Port,
                Configuration.ProtocoleType,
                Configuration.SynchronizationTransport,
                Configuration.ConnectionTimeoutMilliseconds,
                cancellationToken);
        }

        /// <summary>
        /// Starts a connection attempt while preserving the legacy event-based API.
        /// </summary>
        /// <param name="hostNameOrIpAddress">Host name or IP address.</param>
        /// <param name="port">Server port.</param>
        /// <param name="protocoleType">Socket protocol to use.</param>
        /// <param name="synchronizeUsingUDP">Whether world synchronization uses UDP.</param>
        public void Connect(
            string hostNameOrIpAddress,
            int port,
            NetSquareProtocoleType protocoleType = NetSquareProtocoleType.TCP_AND_UDP,
            bool synchronizeUsingUDP = true)
        {
            // Keep the existing non-blocking API while routing all work through the typed async operation.
            _ = ConnectAsync(
                hostNameOrIpAddress,
                port,
                protocoleType,
                synchronizeUsingUDP,
                ConnectionTimeoutMilliseconds,
                CancellationToken.None);
        }

        /// <summary>
        /// Starts a connection attempt with an explicit synchronization transport.
        /// </summary>
        /// <param name="hostNameOrIpAddress">Host name or IP address.</param>
        /// <param name="port">Server port.</param>
        /// <param name="protocoleType">Socket protocol to use.</param>
        /// <param name="synchronizationTransport">Transport used for world synchronization frames.</param>
        public void Connect(
            string hostNameOrIpAddress,
            int port,
            NetSquareProtocoleType protocoleType,
            NetSquareSyncTransport synchronizationTransport)
        {
            WorldsManager.SynchronizationTransport = synchronizationTransport;
            Connect(
                hostNameOrIpAddress,
                port,
                protocoleType,
                synchronizationTransport == NetSquareSyncTransport.UnreliableUdp);
        }

        /// <summary>
        /// Connects to a NetSquare server and returns a typed result.
        /// </summary>
        /// <param name="hostNameOrIpAddress">Host name or IP address.</param>
        /// <param name="port">Server port.</param>
        /// <param name="protocoleType">Socket protocol to use.</param>
        /// <param name="synchronizeUsingUDP">Whether world synchronization uses UDP.</param>
        /// <param name="timeoutMilliseconds">Timeout in milliseconds, or -1 to use ConnectionTimeoutMilliseconds.</param>
        /// <param name="cancellationToken">Token used to cancel the attempt.</param>
        /// <returns>The final typed connection result.</returns>
        public Task<ConnectionResult> ConnectAsync(
            string hostNameOrIpAddress,
            int port,
            NetSquareProtocoleType protocoleType = NetSquareProtocoleType.TCP_AND_UDP,
            bool synchronizeUsingUDP = true,
            int timeoutMilliseconds = -1,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            int effectiveTimeout = ValidateConnectionArguments(
                hostNameOrIpAddress,
                port,
                timeoutMilliseconds);
            return ConnectAsyncInternal(
                hostNameOrIpAddress,
                port,
                protocoleType,
                synchronizeUsingUDP,
                effectiveTimeout,
                cancellationToken);
        }

        /// <summary>
        /// Connects to a NetSquare server using an explicit synchronization transport.
        /// </summary>
        /// <param name="hostNameOrIpAddress">Host name or IP address.</param>
        /// <param name="port">Server port.</param>
        /// <param name="protocoleType">Socket protocol to use.</param>
        /// <param name="synchronizationTransport">Transport used for world synchronization frames.</param>
        /// <param name="timeoutMilliseconds">Timeout in milliseconds, or -1 to use ConnectionTimeoutMilliseconds.</param>
        /// <param name="cancellationToken">Token used to cancel the attempt.</param>
        /// <returns>The final typed connection result.</returns>
        public Task<ConnectionResult> ConnectAsync(
            string hostNameOrIpAddress,
            int port,
            NetSquareProtocoleType protocoleType,
            NetSquareSyncTransport synchronizationTransport,
            int timeoutMilliseconds = -1,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            WorldsManager.SynchronizationTransport = synchronizationTransport;
            return ConnectAsync(
                hostNameOrIpAddress,
                port,
                protocoleType,
                synchronizationTransport == NetSquareSyncTransport.UnreliableUdp,
                timeoutMilliseconds,
                cancellationToken);
        }

        /// <summary>
        /// Cancels the active connection attempt without affecting an established connection.
        /// </summary>
        public void CancelConnectionAttempt()
        {
            lock (connectionAttemptLock)
                activeConnectionAttemptCancellation?.Cancel();
        }

        /// <summary>
        /// Disconnects this client from the server or cancels its pending connection attempt.
        /// </summary>
        public void Disconnect()
        {
            CancelConnectionAttempt();
            DisconnectInternal(true, new DisconnectInfo(DisconnectReason.ClientRequest));
        }

        /// <summary>
        /// Validates connection arguments and resolves the effective timeout.
        /// </summary>
        /// <param name="hostNameOrIpAddress">Host name or IP address.</param>
        /// <param name="port">Server port.</param>
        /// <param name="timeoutMilliseconds">Requested timeout or -1 for the configured default.</param>
        /// <returns>The effective positive timeout.</returns>
        private int ValidateConnectionArguments(
            string hostNameOrIpAddress,
            int port,
            int timeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(hostNameOrIpAddress))
                throw new ArgumentException("A host name or IP address is required.", nameof(hostNameOrIpAddress));
            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));

            int effectiveTimeout = timeoutMilliseconds == -1
                ? ConnectionTimeoutMilliseconds
                : timeoutMilliseconds;
            if (effectiveTimeout <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutMilliseconds),
                    "The connection timeout must be greater than zero.");

            return effectiveTimeout;
        }
        #endregion

        #region Message Logic
        /// <summary>
        /// Runs one guarded connection attempt and converts every expected outcome into a ConnectionResult.
        /// </summary>
        /// <param name="hostNameOrIpAddress">Host name or IP address.</param>
        /// <param name="port">Server port.</param>
        /// <param name="protocoleType">Socket protocol to use.</param>
        /// <param name="synchronizeUsingUDP">Whether world synchronization uses UDP.</param>
        /// <param name="timeoutMilliseconds">Effective timeout in milliseconds.</param>
        /// <param name="cancellationToken">Caller cancellation token.</param>
        /// <returns>The final typed connection result.</returns>
        private async Task<ConnectionResult> ConnectAsyncInternal(
            string hostNameOrIpAddress,
            int port,
            NetSquareProtocoleType protocoleType,
            bool synchronizeUsingUDP,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            if (IsConnected)
                return ConnectionResult.AlreadyConnected(ClientID);
            if (Interlocked.CompareExchange(ref connectionAttemptActive, 1, 0) != 0)
                return ConnectionResult.ConnectionInProgress();

            TcpClient tcpClient = null;
            ConnectionResult result = null;
            bool useTls = UseTLS;
            CancellationTokenSource manualCancellation = new CancellationTokenSource();
            CancellationTokenSource timeoutCancellation = new CancellationTokenSource(timeoutMilliseconds);
            CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                manualCancellation.Token,
                timeoutCancellation.Token);

            lock (connectionAttemptLock)
                activeConnectionAttemptCancellation = manualCancellation;

            try
            {
                if (synchronizeUsingUDP)
                    protocoleType = NetSquareProtocoleType.TCP_AND_UDP;

                ProtocoleType = protocoleType;
                WorldsManager.SynchronizeUsingUDP = synchronizeUsingUDP;
                Port = port;
                IPAdress = hostNameOrIpAddress;

                tcpClient = new TcpClient();
                tcpClient.NoDelay = true;
                using (linkedCancellation.Token.Register(ClosePendingTcpClient, tcpClient))
                {
                    await tcpClient.ConnectAsync(hostNameOrIpAddress, port).ConfigureAwait(false);
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    Stream transportStream = await CreateClientTransportStreamAsync(
                        tcpClient,
                        hostNameOrIpAddress,
                        useTls).ConfigureAwait(false);
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    result = await ValidateConnectionAsync(
                        tcpClient,
                        transportStream,
                        useTls,
                        linkedCancellation.Token).ConfigureAwait(false);
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || manualCancellation.IsCancellationRequested)
                    result = ConnectionResult.Cancelled();
                else if (timeoutCancellation.IsCancellationRequested)
                    result = ConnectionResult.TimedOut(timeoutMilliseconds);
                else
                    result = ConnectionResult.TransportError(ex);
            }
            finally
            {
                lock (connectionAttemptLock)
                {
                    if (ReferenceEquals(activeConnectionAttemptCancellation, manualCancellation))
                        activeConnectionAttemptCancellation = null;
                }

                linkedCancellation.Dispose();
                timeoutCancellation.Dispose();
                manualCancellation.Dispose();
                Interlocked.Exchange(ref connectionAttemptActive, 0);

                if (result == null || !result.IsConnected)
                    ClosePendingTcpClient(tcpClient);
            }

            result = result ?? ConnectionResult.TransportError(
                new InvalidOperationException("The NetSquare connection attempt ended without a result."));
            PublishConnectionResult(result);
            return result;
        }

        /// <summary>
        /// Creates the raw or TLS-authenticated stream used by the NetSquare handshake.
        /// </summary>
        /// <param name="tcpClient">Connected TCP client.</param>
        /// <param name="targetHost">DNS name or IP address validated against the server certificate.</param>
        /// <param name="useTls">Whether TLS is enabled for this attempt.</param>
        /// <returns>The stream used for handshake and established TLS messages.</returns>
        private async Task<Stream> CreateClientTransportStreamAsync(
            TcpClient tcpClient,
            string targetHost,
            bool useTls)
        {
            // Keep the raw connection path allocation-free beyond TcpClient's NetworkStream.
            NetworkStream networkStream = tcpClient.GetStream();
            if (!useTls)
                return networkStream;

            SslStream tlsStream = new SslStream(
                networkStream,
                false,
                TLSCertificateValidationCallback);
            string certificateTargetHost = string.IsNullOrWhiteSpace(TLSServerName)
                ? targetHost
                : TLSServerName;
            await tlsStream.AuthenticateAsClientAsync(
                certificateTargetHost,
                new X509CertificateCollection(),
                SslProtocols.Tls12,
                true).ConfigureAwait(false);
            return tlsStream;
        }

        /// <summary>
        /// Performs the NetSquare handshake and initializes the connected client.
        /// </summary>
        /// <param name="tcpClient">Connected TCP transport.</param>
        /// <param name="cancellationToken">Attempt timeout and cancellation token.</param>
        /// <param name="transportStream">Raw or TLS-authenticated handshake stream.</param>
        /// <param name="useTls">Whether this connection keeps the stream for established messages.</param>
        /// <returns>The handshake result.</returns>
        private async Task<ConnectionResult> ValidateConnectionAsync(
            TcpClient tcpClient,
            Stream transportStream,
            bool useTls,
            CancellationToken cancellationToken)
        {
            ConnectedClient initializedClient = null;
            try
            {
                // The client speaks first so generic scanners never receive a NetSquare fingerprint.
                HandshakeCapabilities requestedCapabilities = NetSquareHandshakeProtocol.SupportedCapabilities;
                if (!UseUdpAuthentication)
                    requestedCapabilities &= ~HandshakeCapabilities.AuthenticatedUdpDatagrams;
                byte[] clientHelloFrame = NetSquareHandshakeProtocol.CreateClientHello(
                    ProtocoleType,
                    requestedCapabilities);
                NetSquareHandshakeProtocol.SendAll(transportStream, clientHelloFrame);

                byte[] serverChallengeFrame = await ReceiveHandshakeFrameAsync(
                    transportStream,
                    NetSquareHandshakeProtocol.ServerChallengeLength,
                    cancellationToken).ConfigureAwait(false);
                HandshakeServerChallenge challenge =
                    NetSquareHandshakeProtocol.DeserializeServerChallenge(serverChallengeFrame);

                HandshakeCapabilities requiredCapabilities =
                    HandshakeCapabilities.HighPrecisionTimeSynchronization;
                if (ProtocoleType == NetSquareProtocoleType.TCP_AND_UDP && UseUdpAuthentication)
                    requiredCapabilities |= HandshakeCapabilities.AuthenticatedUdpDatagrams;

                if (challenge.SelectedWireProtocolVersion != NetSquareHandshakeProtocol.WireProtocolVersion ||
                    challenge.SelectedTransport != ProtocoleType ||
                    (challenge.Capabilities & requiredCapabilities) != requiredCapabilities ||
                    (challenge.Capabilities & ~requestedCapabilities) != 0 ||
                    !NetSquareHandshakeProtocol.ValidateChallenge(clientHelloFrame, challenge))
                {
                    throw new InvalidOperationException("The server returned incompatible handshake settings.");
                }

                // Solving is cancellable and has zero cost while the server is below its activation threshold.
                byte[] clientProofFrame = NetSquareHandshakeProtocol.CreateClientProof(
                    clientHelloFrame,
                    serverChallengeFrame,
                    cancellationToken);
                NetSquareHandshakeProtocol.SendAll(transportStream, clientProofFrame);

                byte[] serverAcceptFrame = await ReceiveHandshakeFrameAsync(
                    transportStream,
                    NetSquareHandshakeProtocol.ServerAcceptLength,
                    cancellationToken).ConfigureAwait(false);
                HandshakeServerAccept accept =
                    NetSquareHandshakeProtocol.DeserializeServerAccept(serverAcceptFrame);
                if (accept.SelectedWireProtocolVersion != challenge.SelectedWireProtocolVersion ||
                    accept.SelectedTransport != challenge.SelectedTransport ||
                    accept.Capabilities != challenge.Capabilities ||
                    !NetSquareHandshakeProtocol.ValidateServerAccept(
                        clientHelloFrame,
                        serverChallengeFrame,
                        clientProofFrame,
                        accept))
                {
                    throw new InvalidOperationException("The server acceptance does not match the negotiated handshake.");
                }

                byte[] clientReadyFrame = NetSquareHandshakeProtocol.CreateClientReady(
                    clientHelloFrame,
                    serverChallengeFrame,
                    clientProofFrame,
                    serverAcceptFrame);
                NetSquareHandshakeProtocol.SendAll(transportStream, clientReadyFrame);

                byte[] serverConnectedFrame = await ReceiveHandshakeFrameAsync(
                    transportStream,
                    NetSquareHandshakeProtocol.ServerConnectedLength,
                    cancellationToken).ConfigureAwait(false);
                HandshakeServerConnected connected =
                    NetSquareHandshakeProtocol.DeserializeServerConnected(serverConnectedFrame);
                if (connected.ClientID == 0 ||
                    (connected.HeartbeatEnabled &&
                        (accept.Capabilities & HandshakeCapabilities.Heartbeat) == 0) ||
                    !NetSquareHandshakeProtocol.ValidateServerConnected(clientReadyFrame, connected))
                {
                    throw new InvalidOperationException("The server connected confirmation is invalid.");
                }

                initializedClient = InitializeConnectedClient(
                    tcpClient,
                    connected.ClientID,
                    accept.SelectedTransport,
                    (accept.Capabilities & HandshakeCapabilities.AuthenticatedUdpDatagrams) != 0
                        ? accept.SessionToken
                        : null,
                    useTls ? transportStream : null);

                if (accept.SelectedTransport == NetSquareProtocoleType.TCP_AND_UDP)
                    await WaitForUdpRegistrationAsync(initializedClient, cancellationToken).ConfigureAwait(false);

                ApplyServerHeartbeatPolicy(connected);

                // Heartbeats start only after every negotiated transport is fully usable.
                StartHeartbeat();
                return ConnectionResult.Connected(connected.ClientID);
            }
            catch (HandshakeRejectedException ex)
            {
                return ConnectionResult.Rejected(ex.RejectionInfo);
            }
            catch
            {
                if (initializedClient != null)
                    RollbackConnectedClientInitialization(initializedClient);
                throw;
            }
        }

        /// <summary>
        /// Initializes message processing after a successful TCP handshake.
        /// </summary>
        /// <param name="tcpClient">Validated TCP transport.</param>
        /// <param name="clientID">Server-assigned client ID.</param>
        /// <param name="selectedTransport">Transport negotiated with the server.</param>
        /// <param name="udpSessionKey">Session key used to authenticate UDP datagrams.</param>
        /// <param name="tcpStream">Established TLS stream, or null for raw TCP.</param>
        /// <returns>The initialized connected client.</returns>
        private ConnectedClient InitializeConnectedClient(
            TcpClient tcpClient,
            uint clientID,
            NetSquareProtocoleType selectedTransport,
            byte[] udpSessionKey,
            Stream tcpStream)
        {
            ConnectedClient connectedClient = new ConnectedClient
            {
                ID = clientID
            };

            try
            {
                // Apply only the settings selected by the authenticated server transcript.
                ProtocoleType = selectedTransport;
                connectedClient.SetClient(
                    tcpClient.Client,
                    true,
                    selectedTransport == NetSquareProtocoleType.TCP_AND_UDP,
                    udpSessionKey,
                    tcpStream);
                connectedClient.OnMessageReceived += Client_OnMessageReceived;
                connectedClient.OnDisconected += Client_OnDisconected;
                Client = connectedClient;
                Interlocked.Exchange(ref disconnectStarted, 0);
                messagesQueue = new BoundedConcurrentQueue<NetworkMessage>(
                    Configuration.MaxQueuedInboundMessages);
                messageProcessingCancellation = new CancellationTokenSource();
                BoundedConcurrentQueue<NetworkMessage> workerQueue = messagesQueue;
                CancellationToken workerToken = messageProcessingCancellation.Token;
                messageProcessingThread = new Thread(() => ProcessMessagesLoop(workerQueue, workerToken));
                messageProcessingThread.IsBackground = true;
                messageProcessingThread.Name = "NetSquare client messages";
                messageProcessingThread.Start();
                return connectedClient;
            }
            catch
            {
                RollbackConnectedClientInitialization(connectedClient);
                throw;
            }
        }


        /// <summary>
        /// Waits for UDP endpoint registration and retransmits the probe when necessary.
        /// </summary>
        /// <param name="client">Initialized client with UDP enabled.</param>
        /// <param name="cancellationToken">Connection timeout and cancellation token.</param>
        private static async Task WaitForUdpRegistrationAsync(
            ConnectedClient client,
            CancellationToken cancellationToken)
        {
            DateTime nextRegistrationUtc = DateTime.MinValue;
            while (client != null && client.UDP != null && !client.UDP.IsRegistrationCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!client.IsConnected())
                    throw new SocketException((int)SocketError.ConnectionReset);

                // UDP is unreliable, so repeat the registration probe until its acknowledgement arrives.
                DateTime nowUtc = DateTime.UtcNow;
                if (nowUtc >= nextRegistrationUtc)
                {
                    client.UDP.SendRegistration();
                    nextRegistrationUtc = nowUtc.AddMilliseconds(250);
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            if (client == null || client.UDP == null || !client.UDP.IsRegistrationCompleted)
                throw new InvalidOperationException("The negotiated UDP transport was not initialized.");
        }

        /// <summary>
        /// Allocates and fills a buffer from a raw or TLS transport stream.
        /// </summary>
        /// <param name="stream">Readable transport stream.</param>
        /// <param name="length">Number of bytes required.</param>
        /// <param name="cancellationToken">Connection timeout and cancellation token.</param>
        /// <returns>The complete buffer.</returns>
        private static async Task<byte[]> ReceiveStreamBytesAsync(
            Stream stream,
            int length,
            CancellationToken cancellationToken)
        {
            // Delegate to the buffer overload so every partial-read rule stays in one place.
            byte[] buffer = new byte[length];
            await ReceiveStreamBytesAsync(
                stream,
                buffer,
                0,
                buffer.Length,
                cancellationToken).ConfigureAwait(false);
            return buffer;
        }

        /// <summary>
        /// Fills one section of a buffer from a raw or TLS transport stream.
        /// </summary>
        /// <param name="stream">Readable transport stream.</param>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="offset">First destination index.</param>
        /// <param name="length">Number of bytes required.</param>
        /// <param name="cancellationToken">Connection timeout and cancellation token.</param>
        /// <returns>A task that completes when the requested bytes are available.</returns>
        private static async Task ReceiveStreamBytesAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int length,
            CancellationToken cancellationToken)
        {
            // NetworkStream and SslStream may both return partial data.
            int remaining = length;
            while (remaining > 0)
            {
                int received = await stream.ReadAsync(
                    buffer,
                    offset,
                    remaining,
                    cancellationToken).ConfigureAwait(false);
                if (received <= 0)
                    throw new IOException("The remote peer closed the TCP stream.");
                offset += received;
                remaining -= received;
            }
        }

        /// <summary>
        /// Reads one complete handshake frame from a stream while preserving typed rejection feedback.
        /// </summary>
        /// <param name="stream">Raw or TLS-authenticated transport stream.</param>
        /// <param name="frameLength">Expected fixed frame length.</param>
        /// <param name="cancellationToken">Connection timeout and cancellation token.</param>
        /// <returns>The received frame.</returns>
        private static async Task<byte[]> ReceiveHandshakeFrameAsync(
            Stream stream,
            int frameLength,
            CancellationToken cancellationToken)
        {
            // Read the marker first because SslStream cannot expose or peek its encrypted socket bytes.
            byte[] prefix = await ReceiveStreamBytesAsync(
                stream,
                NetSquareHandshakeProtocol.FrameMarkerLength,
                cancellationToken).ConfigureAwait(false);
            if (ConnectionFeedbackProtocol.IsConnectionRejectionMarker(prefix))
            {
                throw new HandshakeRejectedException(
                    ConnectionFeedbackProtocol.ReceiveConnectionRejection(stream, prefix));
            }

            byte[] frame = new byte[frameLength];
            Buffer.BlockCopy(prefix, 0, frame, 0, prefix.Length);
            await ReceiveStreamBytesAsync(
                stream,
                frame,
                prefix.Length,
                frame.Length - prefix.Length,
                cancellationToken).ConfigureAwait(false);
            return frame;
        }


        /// <summary>
        /// Removes partially initialized transports without publishing connection events.
        /// </summary>
        /// <param name="connectedClient">Partially initialized client.</param>
        private void RollbackConnectedClientInitialization(ConnectedClient connectedClient)
        {
            // A failed UDP proof must leave the instance reusable for a later connection attempt.
            if (connectedClient == null)
                return;

            connectedClient.OnMessageReceived -= Client_OnMessageReceived;
            connectedClient.OnDisconected -= Client_OnDisconected;
            StopMessageProcessingWorker();
            ClearPendingReplyCallbacks();
            if (ReferenceEquals(Client, connectedClient))
                Client = null;
            Interlocked.Exchange(ref disconnectStarted, 1);

            try { connectedClient.UDP?.connection?.Close(); } catch { }
            connectedClient.CloseTcpTransport();
        }

        /// <summary>
        /// Publishes compatibility events for a completed typed connection result.
        /// </summary>
        /// <param name="result">Completed connection result.</param>
        private void PublishConnectionResult(ConnectionResult result)
        {
            if (result == null)
                return;

            switch (result.Status)
            {
                case ConnectionResultStatus.Connected:
                    TryInvokeConnectionEvent(() => OnConnected?.Invoke(result.ClientID));
                    break;

                case ConnectionResultStatus.Rejected:
                    TryInvokeConnectionEvent(() => OnConnectionRejected?.Invoke(result.RejectionInfo));
                    break;

                case ConnectionResultStatus.TransportError:
                case ConnectionResultStatus.TimedOut:
                    if (result.Exception != null)
                        TryNotifyException(result.Exception);
                    TryInvokeConnectionEvent(() => OnConnectionFail?.Invoke());
                    break;
            }
        }

        /// <summary>
        /// Invokes a connection event without allowing subscriber exceptions to fault ConnectAsync.
        /// </summary>
        /// <param name="callback">Event callback.</param>
        private void TryInvokeConnectionEvent(Action callback)
        {
            try
            {
                callback?.Invoke();
            }
            catch (Exception ex)
            {
                TryNotifyException(ex);
            }
        }

        /// <summary>
        /// Notifies exception subscribers while keeping connection state deterministic.
        /// </summary>
        /// <param name="exception">Exception to publish.</param>
        private void TryNotifyException(Exception exception)
        {
            try
            {
                OnException?.Invoke(exception);
            }
            catch
            {
                // Exception observers must not change the connection result.
            }
        }

        /// <summary>
        /// Closes a pending TCP transport when cancellation or cleanup occurs.
        /// </summary>
        /// <param name="state">TcpClient instance supplied by CancellationToken.Register.</param>
        private static void ClosePendingTcpClient(object state)
        {
            ClosePendingTcpClient(state as TcpClient);
        }

        /// <summary>
        /// Closes and disposes a pending TCP transport.
        /// </summary>
        /// <param name="tcpClient">TCP transport to close.</param>
        private static void ClosePendingTcpClient(TcpClient tcpClient)
        {
            if (tcpClient == null)
                return;

            try { tcpClient.Close(); } catch { }
            try { tcpClient.Dispose(); } catch { }
        }


        /// <summary>
        /// invoked when new message was received from server
        /// </summary>
        /// <param name="message"></param>
        private void Client_OnMessageReceived(NetworkMessage message)
        {
            // Process terminal feedback immediately so it cannot be overtaken by the socket close event.
            if (message.HeadID == (ushort)NetSquareMessageID.Disconnecting)
            {
                // Pending transports are rolled back by ConnectAsync without publishing established events.
                if (Volatile.Read(ref connectionAttemptActive) != 0)
                    return;

                ServerDisconnecting(message);
                return;
            }

            messagesQueue.Enqueue(message);
        }

        /// <summary>
        /// Invoked when the connected client socket is disconnected.
        /// </summary>
        /// <param name="clientID">The client ID.</param>
        private void Client_OnDisconected(uint clientID)
        {
            // A socket lost before ConnectAsync completes was never publicly connected.
            if (Volatile.Read(ref connectionAttemptActive) != 0)
                return;

            DisconnectInternal(false, new DisconnectInfo(DisconnectReason.ConnectionLost));
        }

        /// <summary>
        /// Invoked when the server announces it is disconnecting.
        /// </summary>
        /// <param name="message">The message.</param>
        private void ServerDisconnecting(NetworkMessage message)
        {
            DisconnectInfo info = ConnectionFeedbackProtocol.ReadDisconnectInfo(
                message,
                DisconnectReason.ServerRequest);
            DisconnectInternal(false, info);
        }

        /// <summary>
        /// Disconnect this client.
        /// </summary>
        /// <param name="notifyRemote">If true, send a disconnect notice before closing.</param>
        /// <param name="info">Typed reason exposed to the remote peer and local event subscribers.</param>
        private void DisconnectInternal(bool notifyRemote, DisconnectInfo info)
        {
            if (Interlocked.Exchange(ref disconnectStarted, 1) != 0)
                return;

            info = info ?? new DisconnectInfo(DisconnectReason.Unknown);
            StopHeartbeat(true);
            StopAutoSyncTime(true);
            CancelTimeSynchronization();

            ConnectedClient client = Client;
            if (notifyRemote)
                TryNotifyServerDisconnecting(client, info);

            if (client != null)
            {
                // Detach network producers before completing and draining the bounded queue.
                client.OnMessageReceived -= Client_OnMessageReceived;
                client.OnDisconected -= Client_OnDisconected;
            }

            StopMessageProcessingWorker();
            ClearPendingReplyCallbacks();
            Client = null;
            client?.CloseTcpTransport();

            OnDisconnected?.Invoke(info);
            OnDisconected?.Invoke();
        }

        /// <summary>
        /// Try to tell the server this client is disconnecting before closing the socket.
        /// </summary>
        /// <param name="client">The connected client.</param>
        /// <param name="info">Typed reason sent to the server.</param>
        private void TryNotifyServerDisconnecting(ConnectedClient client, DisconnectInfo info)
        {
            if (client == null || client.TcpSocket == null || !client.TcpSocket.Connected)
                return;

            try
            {
                client.AddTCPMessageAndWait(
                    ConnectionFeedbackProtocol.CreateDisconnectMessage(info, client.ID),
                    DisconnectNoticeTimeoutMs);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
            }
        }

        /// <summary>
        /// Applies the heartbeat policy received in the validated final server handshake frame.
        /// </summary>
        /// <param name="connected">Validated final server confirmation.</param>
        private void ApplyServerHeartbeatPolicy(HandshakeServerConnected connected)
        {
            if (connected == null)
                throw new ArgumentNullException(nameof(connected));

            // Runtime heartbeat behavior is owned exclusively by the connected server.
            HeartbeatEnabled = connected.HeartbeatEnabled;
            HeartbeatIntervalMs = connected.HeartbeatIntervalMilliseconds;
            HeartbeatTimeoutMs = connected.HeartbeatTimeoutMilliseconds;
            LastHeartbeatUtc = DateTime.MinValue;
        }

        /// <summary>
        /// Starts the heartbeat loop.
        /// </summary>
        private void StartHeartbeat()
        {
            if (!HeartbeatEnabled)
                return;

            lock (heartbeatLock)
            {
                if (isHeartbeatRunning)
                    return;

                heartbeatStopSignal.Reset();
                isHeartbeatRunning = true;
                heartbeatThread = new Thread(HeartbeatLoop);
                heartbeatThread.IsBackground = true;
                heartbeatThread.Start();
            }
        }

        /// <summary>
        /// Stops the heartbeat loop.
        /// </summary>
        public void StopHeartbeat()
        {
            StopHeartbeat(true);
        }

        /// <summary>
        /// Stops the heartbeat loop.
        /// </summary>
        private void StopHeartbeat(bool waitForStop)
        {
            Thread threadToWait = null;
            lock (heartbeatLock)
            {
                if (!isHeartbeatRunning)
                    return;

                isHeartbeatRunning = false;
                heartbeatStopSignal.Set();
                threadToWait = heartbeatThread;
                heartbeatThread = null;
            }

            if (waitForStop && threadToWait != null && threadToWait != Thread.CurrentThread && threadToWait.IsAlive)
            {
                int heartbeatTimeoutMs = GetHeartbeatTimeoutMs();
                int joinTimeoutMs = heartbeatTimeoutMs > int.MaxValue - 250
                    ? int.MaxValue
                    : heartbeatTimeoutMs + 250;
                threadToWait.Join(joinTimeoutMs);
            }
        }

        /// <summary>
        /// Runs periodic heartbeats to measure ping and detect a dead server.
        /// </summary>
        private void HeartbeatLoop()
        {
            try
            {
                while (isHeartbeatRunning)
                {
                    if (!HeartbeatEnabled || Client == null || !IsConnected)
                        return;

                    if (!TryRequestHeartbeat())
                    {
                        if (isHeartbeatRunning)
                            DisconnectInternal(false, new DisconnectInfo(DisconnectReason.Timeout));
                        return;
                    }

                    if (heartbeatStopSignal.Wait(HeartbeatIntervalMs))
                        return;
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
                if (isHeartbeatRunning)
                    DisconnectInternal(false, new DisconnectInfo(DisconnectReason.ConnectionLost));
            }
            finally
            {
                lock (heartbeatLock)
                {
                    if (heartbeatThread == Thread.CurrentThread)
                    {
                        isHeartbeatRunning = false;
                        heartbeatThread = null;
                    }
                }
            }
        }

        /// <summary>
        /// Sends one heartbeat and waits for its reply.
        /// </summary>
        private bool TryRequestHeartbeat()
        {
            ConnectedClient client = Client;
            if (client == null || !IsConnected)
                return false;

            using (ManualResetEventSlim received = new ManualResetEventSlim(false))
            {
                Stopwatch roundTripWatch = Stopwatch.StartNew();
                Exception callbackException = null;
                uint replyID = 0;
                bool hasReplyID = false;

                try
                {
                    NetworkMessage message = new NetworkMessage(NetSquareMessageID.Heartbeat, ClientID)
                        .Set(HeartbeatProtocolVersion)
                        .Set(Ping);

                    replyID = SendMessageWithReply(message, (reply) =>
                    {
                        try
                        {
                            double serverTime;
                            if (!TryReadServerTime(reply, out serverTime))
                                callbackException = new Exception("Invalid heartbeat reply.");

                            SetPingFromRoundTrip((float)roundTripWatch.Elapsed.TotalSeconds);
                            LastHeartbeatUtc = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            callbackException = ex;
                        }
                        finally
                        {
                            received.Set();
                        }
                    }, true, GetHeartbeatTimeoutMs());
                    hasReplyID = true;
                }
                catch (Exception ex)
                {
                    roundTripWatch.Stop();
                    OnException?.Invoke(ex);
                    return false;
                }

                int heartbeatTimeoutMs = GetHeartbeatTimeoutMs();
                Stopwatch waitWatch = Stopwatch.StartNew();
                bool replyReceived = false;
                while (waitWatch.ElapsedMilliseconds < heartbeatTimeoutMs)
                {
                    if (received.Wait(25))
                    {
                        replyReceived = true;
                        break;
                    }

                    if (!isHeartbeatRunning)
                        break;
                }

                if (!replyReceived)
                {
                    roundTripWatch.Stop();
                    if (hasReplyID)
                        RemoveReplyCallback(replyID);
                    return false;
                }

                roundTripWatch.Stop();
                if (callbackException != null)
                {
                    OnException?.Invoke(callbackException);
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Gets the server-provided heartbeat timeout in milliseconds.
        /// </summary>
        private int GetHeartbeatTimeoutMs()
        {
            return HeartbeatTimeoutMs;
        }

        /// <summary>
        /// Applies a measured round-trip ping value.
        /// </summary>
        private void SetPingFromRoundTrip(float roundTripSeconds)
        {
            int pingMs = (int)Math.Round(Math.Max(0f, roundTripSeconds) * 1000f);
            if (pingMs > ushort.MaxValue)
                pingMs = ushort.MaxValue;

            Ping = (ushort)pingMs;
            ConnectedClient client = Client;
            if (client != null)
                client.Ping = Ping;
        }

        /// <summary>
        /// Processes accepted messages until the bounded queue is completed or forcibly cancelled.
        /// </summary>
        /// <param name="workerQueue">Queue owned by this connection generation.</param>
        /// <param name="cancellationToken">Forced-stop token.</param>
        private void ProcessMessagesLoop(
            BoundedConcurrentQueue<NetworkMessage> workerQueue,
            CancellationToken cancellationToken)
        {
            try
            {
                NetworkMessage message;
                while (workerQueue.TryDequeue(out message, cancellationToken))
                {
                    try
                    {
                        ProcessReceivedMessage(message);
                    }
                    catch (Exception ex)
                    {
                        // User handlers are isolated so one failure cannot terminate the network worker.
                        OnException?.Invoke(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Forced cancellation is expected only after the graceful shutdown timeout.
            }
            finally
            {
                if (messageProcessingThread == Thread.CurrentThread)
                {
                    messageProcessingThread = null;
                    messageProcessingCancellation = null;
                }
            }
        }

        /// <summary>
        /// Dispatches one received message according to its protocol message type.
        /// </summary>
        /// <param name="message">Message to dispatch.</param>
        private void ProcessReceivedMessage(NetworkMessage message)
        {
            switch ((NetSquareMessageType)message.MsgType)
            {
                default:
                case NetSquareMessageType.Default:
                    if (!Dispatcher.DispatchMessage(message))
                        OnUnregisteredMessageReceived?.Invoke(message);
                    break;

                case NetSquareMessageType.Reply:
                    PendingReplyCallback pendingReply;
                    if (TryTakePendingReplyCallback(message.ReplyID, out pendingReply))
                    {
                        if (pendingReply.ExecuteInline)
                            pendingReply.Callback.Invoke(message);
                        else
                            Dispatcher.ExecuteinMainThread(pendingReply.Callback, message);
                    }
                    break;

                case NetSquareMessageType.SynchronizeMessageCurrentWorld:
                    Dispatcher.ExecuteinMainThread((receivedMessage) =>
                    {
                        WorldsManager.Fire_OnSyncronize(receivedMessage);
                    }, message);
                    break;
            }
        }

        /// <summary>
        /// Completes the current message queue and waits for its worker to drain accepted messages.
        /// </summary>
        private void StopMessageProcessingWorker()
        {
            BoundedConcurrentQueue<NetworkMessage> queueToComplete = messagesQueue;
            Thread threadToJoin = messageProcessingThread;
            CancellationTokenSource cancellation = messageProcessingCancellation;
            queueToComplete?.CompleteAdding();

            if (threadToJoin == null || threadToJoin == Thread.CurrentThread)
                return;

            int timeout = Configuration == null
                ? 5000
                : Math.Max(1, Configuration.MessageWorkerStopTimeoutMilliseconds);
            if (!threadToJoin.Join(timeout))
            {
                cancellation?.Cancel();
                if (!threadToJoin.Join(Math.Min(250, timeout)))
                {
                    OnException?.Invoke(new TimeoutException(
                        "The NetSquare client message worker did not stop within the configured timeout."));
                }
            }

            if (!threadToJoin.IsAlive && messageProcessingThread == threadToJoin)
            {
                messageProcessingThread = null;
                messageProcessingCancellation = null;
            }
        }

        /// <summary>
        /// Reserves the next non-zero reply ID while the reply lock is held.
        /// </summary>
        /// <returns>An unused reply ID.</returns>
        private uint GetNextReplyIDLocked()
        {
            // IDs wrap inside the protocol range and skip every active reservation.
            do
            {
                nbReplyAsked++;
                if (nbReplyAsked == 0 || nbReplyAsked > UInt24.MaxValue)
                    nbReplyAsked = 1;
            }
            while (pendingReplyCallbacks.ContainsKey(nbReplyAsked));

            return nbReplyAsked;
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
        /// Send an empty message to server without waiting for response
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessage(Enum HeadID)
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
            SendMessageWithReply(msg, callback, false);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(ushort headID, NetSquareAction callback)
        {
            SendMessageWithReply(new NetworkMessage(headID), callback, false);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server respond to this message
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="callback">callback to invoke when server respond</param>
        public void SendMessage(Enum headID, NetSquareAction callback)
        {
            SendMessageWithReply(new NetworkMessage(headID), callback, false);
        }

        /// <summary>
        /// Send a message to server and invoke callback when server responds.
        /// </summary>
        /// <param name="msg">Message expecting a reply.</param>
        /// <param name="callback">Callback invoked for the matching reply.</param>
        /// <param name="executeReplyInline">Whether to invoke the callback on the network processing thread.</param>
        /// <param name="callbackTimeoutMilliseconds">Optional callback lifetime override.</param>
        private uint SendMessageWithReply(
            NetworkMessage msg,
            NetSquareAction callback,
            bool executeReplyInline,
            int callbackTimeoutMilliseconds = 0)
        {
            if (msg == null)
                throw new ArgumentNullException(nameof(msg));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            int effectiveCallbackTimeoutMilliseconds = callbackTimeoutMilliseconds > 0
                ? callbackTimeoutMilliseconds
                : replyCallbackTimeoutMilliseconds;
            uint rplID = RegisterReplyCallback(callback, executeReplyInline, effectiveCallbackTimeoutMilliseconds);
            msg.ReplyTo(rplID);

            try
            {
                SendMessage(msg);
                return rplID;
            }
            catch
            {
                RemoveReplyCallback(rplID);
                throw;
            }
        }

        /// <summary>
        /// Removes a pending reply callback.
        /// </summary>
        private void RemoveReplyCallback(uint replyID)
        {
            // Removal and timer shutdown share the same lock as registration.
            lock (replyIDLock)
            {
                pendingReplyCallbacks.Remove(replyID);
                StopReplyCallbackCleanupTimerIfIdleLocked();
            }
        }

        /// <summary>
        /// Registers one callback after enforcing expiration and capacity limits.
        /// </summary>
        /// <param name="callback">Callback invoked for the matching reply.</param>
        /// <param name="executeReplyInline">Whether to execute on the network processing thread.</param>
        /// <param name="callbackTimeoutMilliseconds">Lifetime of this callback reservation.</param>
        /// <returns>The reserved reply ID.</returns>
        private uint RegisterReplyCallback(
            NetSquareAction callback,
            bool executeReplyInline,
            int callbackTimeoutMilliseconds)
        {
            // Expired entries are reclaimed before enforcing the hard capacity.
            long currentTimestamp = Stopwatch.GetTimestamp();
            long expirationTimestamp = GetReplyCallbackExpirationTimestamp(currentTimestamp, callbackTimeoutMilliseconds);
            lock (replyIDLock)
            {
                CleanupExpiredReplyCallbacksLocked(currentTimestamp);
                if (pendingReplyCallbacks.Count >= maxPendingReplyCallbacks)
                {
                    throw new InvalidOperationException(
                        "The maximum number of pending NetSquare reply callbacks has been reached.");
                }

                uint replyID = GetNextReplyIDLocked();
                pendingReplyCallbacks.Add(
                    replyID,
                    new PendingReplyCallback(callback, executeReplyInline, expirationTimestamp));
                EnsureReplyCallbackCleanupTimerLocked();
                return replyID;
            }
        }

        /// <summary>
        /// Removes and returns a non-expired callback for one reply.
        /// </summary>
        /// <param name="replyID">Reply ID received from the server.</param>
        /// <param name="pendingReply">Registered callback when it is still valid.</param>
        /// <returns>True when a valid callback was removed.</returns>
        private bool TryTakePendingReplyCallback(uint replyID, out PendingReplyCallback pendingReply)
        {
            // Taking removes the reservation exactly once, including late replies.
            lock (replyIDLock)
            {
                if (!pendingReplyCallbacks.TryGetValue(replyID, out pendingReply))
                    return false;

                pendingReplyCallbacks.Remove(replyID);
                StopReplyCallbackCleanupTimerIfIdleLocked();
                if (!pendingReply.IsExpired(Stopwatch.GetTimestamp()))
                    return true;

                pendingReply = null;
                return false;
            }
        }

        /// <summary>
        /// Computes a monotonic expiration timestamp for a newly registered callback.
        /// </summary>
        /// <param name="currentTimestamp">Current monotonic timestamp.</param>
        /// <param name="callbackTimeoutMilliseconds">Callback lifetime in milliseconds.</param>
        /// <returns>Expiration timestamp with overflow saturation.</returns>
        private long GetReplyCallbackExpirationTimestamp(
            long currentTimestamp,
            int callbackTimeoutMilliseconds)
        {
            double timeoutTicks =
                (double)callbackTimeoutMilliseconds * Stopwatch.Frequency / 1000d;
            long timeoutTimestampDelta = timeoutTicks >= long.MaxValue
                ? long.MaxValue
                : Math.Max(1L, (long)Math.Ceiling(timeoutTicks));

            // Saturation keeps very large configured timeouts from wrapping into the past.
            return timeoutTimestampDelta >= long.MaxValue - currentTimestamp
                ? long.MaxValue
                : currentTimestamp + timeoutTimestampDelta;
        }

        /// <summary>
        /// Starts the shared low-frequency cleanup timer when callbacks are pending.
        /// </summary>
        private void EnsureReplyCallbackCleanupTimerLocked()
        {
            if (replyCallbackCleanupTimer != null)
                return;

            // One timer per connected client bounds cleanup overhead independently of request volume.
            replyCallbackCleanupTimer = new Timer(
                CleanupExpiredReplyCallbacks,
                null,
                ReplyCallbackCleanupIntervalMilliseconds,
                ReplyCallbackCleanupIntervalMilliseconds);
        }

        /// <summary>
        /// Removes expired callbacks from the timer thread.
        /// </summary>
        /// <param name="state">Unused timer state.</param>
        private void CleanupExpiredReplyCallbacks(object state)
        {
            // Timer callbacks only hold the lock for bounded dictionary maintenance.
            lock (replyIDLock)
            {
                CleanupExpiredReplyCallbacksLocked(Stopwatch.GetTimestamp());
                StopReplyCallbackCleanupTimerIfIdleLocked();
            }
        }

        /// <summary>
        /// Removes expired callbacks without allocating a collection on every timer tick.
        /// </summary>
        /// <param name="currentTimestamp">Current monotonic timestamp.</param>
        private void CleanupExpiredReplyCallbacksLocked(long currentTimestamp)
        {
            // Reuse one ID list because dictionary entries cannot be removed during enumeration.
            expiredReplyCallbackIDs.Clear();
            foreach (KeyValuePair<uint, PendingReplyCallback> callback in pendingReplyCallbacks)
            {
                if (callback.Value.IsExpired(currentTimestamp))
                    expiredReplyCallbackIDs.Add(callback.Key);
            }

            foreach (uint replyID in expiredReplyCallbackIDs)
                pendingReplyCallbacks.Remove(replyID);
            expiredReplyCallbackIDs.Clear();
        }

        /// <summary>
        /// Stops the cleanup timer when no callback remains.
        /// </summary>
        private void StopReplyCallbackCleanupTimerIfIdleLocked()
        {
            // No periodic work remains useful after the final reservation disappears.
            if (pendingReplyCallbacks.Count != 0 || replyCallbackCleanupTimer == null)
                return;

            Timer timer = replyCallbackCleanupTimer;
            replyCallbackCleanupTimer = null;
            timer.Dispose();
        }

        /// <summary>
        /// Clears all callbacks and stops their cleanup timer during transport teardown.
        /// </summary>
        private void ClearPendingReplyCallbacks()
        {
            // Transport teardown invalidates every outstanding request/reply association.
            lock (replyIDLock)
            {
                pendingReplyCallbacks.Clear();
                expiredReplyCallbackIDs.Clear();
                StopReplyCallbackCleanupTimerIfIdleLocked();
            }
        }

        /// <summary>
        /// Send a network message to the server
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="items">Items to set into messages, can only be primitives types handeled by NetSquare, a bit slower than creating the network message yourself but faster to write. Only for lazy dev</param>
        public void SendMessage(ushort headID, params object[] items)
        {
            NetworkMessage message = new NetworkMessage(headID, Client.ID);
            foreach (object item in items)
            {
                Type itemType = item.GetType();
                if (typesDic.ContainsKey(itemType))
                    typesDic[itemType].Invoke(message, item);
                else
                    throw new Exception("Item type not handled by NetSquare");
            }
            SendMessage(message);
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
            Client.AddUnreliableMessage(msg.HeadID, msg.Serialize());
        }

        /// <summary>
        /// Send an empty message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessageUDP(ushort HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddUnreliableMessage(HeadID, msg.Serialize());
        }

        /// <summary>
        /// Send an empty message to server without waiting for response, sended in UDP, faster but no way to know is server received it
        /// </summary>
        /// <param name="HeadID">ID of the message to send</param>
        public void SendMessageUDP(Enum HeadID)
        {
            NetworkMessage msg = new NetworkMessage(HeadID);
            msg.ClientID = Client.ID;
            Client.AddUnreliableMessage(msg.HeadID, msg.Serialize());
        }

        /// <summary>
        /// Send a network message to the server
        /// </summary>
        /// <param name="headID">Head ID of the message</param>
        /// <param name="items">Items to set into messages, can only be primitives types handeled by NetSquare, a bit slower than creating the network message yourself but faster to write. Only for lazy dev</param>
        public void SendMessageUDP(ushort headID, params object[] items)
        {
            NetworkMessage message = new NetworkMessage(headID, Client.ID);
            foreach (object item in items)
            {
                Type itemType = item.GetType();
                if (typesDic.ContainsKey(itemType))
                    typesDic[itemType].Invoke(message, item);
                else
                    throw new Exception("Item type not handled by NetSquare");
            }
            SendMessageUDP(message);
        }
        #endregion

        #region Time Synchronization
        /// <summary>
        /// Get server time from client time
        /// </summary>
        /// <param name="clientTime"> Client time</param>
        /// <returns> Server time</returns>
        public float GetServerTime(float clientTime)
        {
            UpdateSmoothedServerTimeOffset();
            return clientTime + ServerTimeOffset;
        }

        /// <summary>
        /// Synchronize time with server. The more precision, the more time it will take to synchronize
        /// </summary>
        /// <param name="precision">1 to 10, 1 is the less precise, 10 is the most precise</param>
        /// <param name="timeBetweenSyncs">Time between each sync in milliseconds</param>
        /// <param name="onServerTimeGet">Callback</param>
        public void SyncTime(Func<float> getClientTime, int precision = 5, int timeBetweenSyncs = 1000, Action<float> onServerTimeGet = null, Action<string> onLog = null)
        {
            int generation;
            TryStartTimeSynchronization(getClientTime, precision, timeBetweenSyncs, onServerTimeGet, onLog, true, out generation);
        }

        /// <summary>
        /// Starts automatic server time synchronization.
        /// </summary>
        /// <param name="getClientTime">Monotonic local client time source.</param>
        /// <param name="precision">Samples per synchronization.</param>
        /// <param name="timeBetweenSyncs">Time between samples in milliseconds.</param>
        /// <param name="intervalMs">Time between synchronization rounds in milliseconds.</param>
        /// <param name="onServerTimeGet">Callback invoked when server time is updated.</param>
        /// <param name="onLog">Optional log callback.</param>
        public void StartAutoSyncTime(Func<float> getClientTime, int precision = 3, int timeBetweenSyncs = 50, int intervalMs = 30000, Action<float> onServerTimeGet = null, Action<string> onLog = null)
        {
            if (getClientTime == null)
                throw new ArgumentNullException(nameof(getClientTime));

            precision = Math.Max(1, Math.Min(10, precision));
            timeBetweenSyncs = Math.Max(0, timeBetweenSyncs);
            intervalMs = Math.Max(1000, intervalMs);

            StopAutoSyncTime();

            lock (autoTimeSynchronizationLock)
            {
                AutoTimeSynchronizationIntervalMs = intervalMs;
                autoTimeSynchronizationStopSignal.Reset();
                isAutoSynchronizingTime = true;
                autoTimeSynchronizationThread = new Thread(() =>
                {
                    AutoTimeSynchronizationLoop(getClientTime, precision, timeBetweenSyncs, intervalMs, onServerTimeGet, onLog);
                });
                autoTimeSynchronizationThread.IsBackground = true;
                autoTimeSynchronizationThread.Start();
            }
        }

        /// <summary>
        /// Stops automatic server time synchronization.
        /// </summary>
        public void StopAutoSyncTime()
        {
            StopAutoSyncTime(true);
        }

        /// <summary>
        /// Returns whether the server time synchronization was refreshed within the given age.
        /// </summary>
        /// <param name="maxAgeMs">Maximum synchronization age in milliseconds.</param>
        /// <returns>true when the current server time offset is recent enough.</returns>
        public bool IsServerTimeSynchronizationFresh(int maxAgeMs)
        {
            if (!hasServerTimeOffset)
                return false;

            maxAgeMs = Math.Max(0, maxAgeMs);
            return (DateTime.UtcNow - LastServerTimeSynchronizationUtc).TotalMilliseconds <= maxAgeMs;
        }

        /// <summary>
        /// Starts one server time synchronization round.
        /// </summary>
        private bool TryStartTimeSynchronization(Func<float> getClientTime, int precision, int timeBetweenSyncs, Action<float> onServerTimeGet, Action<string> onLog, bool cancelIfAlreadySynchronizing, out int generation)
        {
            generation = 0;
            if (getClientTime == null)
                throw new ArgumentNullException(nameof(getClientTime));

            precision = Math.Max(1, Math.Min(10, precision));
            timeBetweenSyncs = Math.Max(0, timeBetweenSyncs);

            lock (timeSynchronizationLock)
            {
                if (isSynchronizingTime)
                {
                    if (cancelIfAlreadySynchronizing)
                    {
                        isSynchronizingTime = false;
                        timeSynchronizationGeneration++;
                    }
                    return false;
                }

                isSynchronizingTime = true;
                generation = ++timeSynchronizationGeneration;
            }

            int requestTimeoutMs = Math.Max(100, Math.Min(30000, TimeSynchronizationRequestTimeoutMs));
            int maxAttempts = TimeSynchronizationMaxAttempts > 0
                ? Math.Max(precision, TimeSynchronizationMaxAttempts)
                : Math.Max(precision, precision * 2);

            int syncGeneration = generation;
            Thread syncThread = new Thread(() =>
            {
                List<TimeSynchronizationSample> samples = new List<TimeSynchronizationSample>(precision);
                int attemptsDone = 0;

                try
                {
                    for (int attempt = 0; attempt < maxAttempts && samples.Count < precision; attempt++)
                    {
                        if (!IsTimeSynchronizationActive(syncGeneration))
                            return;

                        attemptsDone++;
                        TimeSynchronizationSample sample;
                        if (TryRequestServerTimeSample(getClientTime, requestTimeoutMs, syncGeneration, onLog, out sample))
                        {
                            samples.Add(sample);
                            onLog?.Invoke($"Time sync sample {samples.Count}/{precision} | Offset : {sample.Offset} | RTT : {(sample.RoundTrip * 1000f):F1} ms");

                            if (samples.Count == 1 && !hasServerTimeOffset)
                            {
                                SetServerTimeOffset(sample.Offset, true);
                                onServerTimeGet?.Invoke(GetServerTime(getClientTime()));
                            }
                        }

                        if (samples.Count < precision && attempt + 1 < maxAttempts && !SleepDuringTimeSynchronization(syncGeneration, timeBetweenSyncs))
                            return;
                    }

                    if (!IsTimeSynchronizationActive(syncGeneration))
                        return;

                    if (samples.Count == 0)
                    {
                        onLog?.Invoke("Time sync failed: no server response received.");
                        return;
                    }

                    float offset = GetFilteredServerTimeOffset(samples);
                    SetServerTimeOffset(offset, false);

                    onLog?.Invoke($"Time sync offset : {offset} | Samples : {samples.Count}/{precision} | Attempts : {attemptsDone}/{maxAttempts}");
                    onLog?.Invoke($"Client time : {getClientTime()} | Server time : {GetServerTime(getClientTime())}");
                    onServerTimeGet?.Invoke(GetServerTime(getClientTime()));
                }
                finally
                {
                    FinishTimeSynchronization(syncGeneration);
                }
            });

            syncThread.IsBackground = true;
            syncThread.Start();
            return true;
        }

        /// <summary>
        /// Runs automatic time synchronization while enabled.
        /// </summary>
        private void AutoTimeSynchronizationLoop(Func<float> getClientTime, int precision, int timeBetweenSyncs, int intervalMs, Action<float> onServerTimeGet, Action<string> onLog)
        {
            try
            {
                while (isAutoSynchronizingTime)
                {
                    if (IsConnected)
                    {
                        int generation;
                        if (TryStartTimeSynchronization(getClientTime, precision, timeBetweenSyncs, onServerTimeGet, onLog, false, out generation))
                            WaitForTimeSynchronizationCompletion(generation);
                    }

                    int waitMs = IsConnected ? intervalMs : 1000;
                    if (autoTimeSynchronizationStopSignal.Wait(waitMs))
                        return;
                }
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex);
            }
            finally
            {
                lock (autoTimeSynchronizationLock)
                {
                    if (autoTimeSynchronizationThread == Thread.CurrentThread)
                    {
                        isAutoSynchronizingTime = false;
                        autoTimeSynchronizationThread = null;
                    }
                }
            }
        }

        /// <summary>
        /// Stops automatic server time synchronization.
        /// </summary>
        private void StopAutoSyncTime(bool waitForStop)
        {
            Thread threadToWait = null;
            lock (autoTimeSynchronizationLock)
            {
                if (!isAutoSynchronizingTime)
                    return;

                isAutoSynchronizingTime = false;
                autoTimeSynchronizationStopSignal.Set();
                threadToWait = autoTimeSynchronizationThread;
                autoTimeSynchronizationThread = null;
            }

            CancelTimeSynchronization();

            if (waitForStop && threadToWait != null && threadToWait != Thread.CurrentThread && threadToWait.IsAlive)
                threadToWait.Join(Math.Max(1000, TimeSynchronizationRequestTimeoutMs + 250));
        }

        /// <summary>
        /// Waits until one time synchronization generation completes or auto sync stops.
        /// </summary>
        private void WaitForTimeSynchronizationCompletion(int generation)
        {
            while (IsTimeSynchronizationActive(generation) && isAutoSynchronizingTime)
            {
                if (autoTimeSynchronizationStopSignal.Wait(25))
                {
                    CancelTimeSynchronization();
                    return;
                }
            }
        }

        /// <summary>
        /// Cancels the active time synchronization generation.
        /// </summary>
        private void CancelTimeSynchronization()
        {
            lock (timeSynchronizationLock)
            {
                if (isSynchronizingTime)
                {
                    isSynchronizingTime = false;
                    timeSynchronizationGeneration++;
                }
            }
        }

        /// <summary>
        /// Requests one server time sample.
        /// </summary>
        private bool TryRequestServerTimeSample(Func<float> getClientTime, int requestTimeoutMs, int generation, Action<string> onLog, out TimeSynchronizationSample sample)
        {
            sample = new TimeSynchronizationSample();
            if (Client == null || !IsConnected)
            {
                onLog?.Invoke("Time sync skipped: client is not connected.");
                return false;
            }

            using (ManualResetEventSlim received = new ManualResetEventSlim(false))
            {
                TimeSynchronizationSample receivedSample = new TimeSynchronizationSample();
                bool hasSample = false;
                Exception callbackException = null;
                float clientSendTime = getClientTime();
                Stopwatch roundTripWatch = Stopwatch.StartNew();
                uint replyID = 0;

                try
                {
                    NetworkMessage message = new NetworkMessage(NetSquareMessageID.ClientSynchronizeTime, ClientID)
                        .Set(HighPrecisionTimeSynchronizationVersion);
                    replyID = SendMessageWithReply(message, (reply) =>
                    {
                        try
                        {
                            double serverTime;
                            if (!TryReadServerTime(reply, out serverTime))
                            {
                                callbackException = new Exception("Invalid time synchronization reply.");
                                return;
                            }

                            float clientReceiveTime = getClientTime();
                            float measuredRoundTrip = (float)roundTripWatch.Elapsed.TotalSeconds;
                            float clientRoundTrip = clientReceiveTime - clientSendTime;
                            float midpointRoundTrip = clientRoundTrip > 0f ? clientRoundTrip : measuredRoundTrip;

                            receivedSample.Offset = (float)serverTime - (clientSendTime + midpointRoundTrip * 0.5f);
                            receivedSample.RoundTrip = measuredRoundTrip;
                            hasSample = true;
                        }
                        catch (Exception ex)
                        {
                            callbackException = ex;
                        }
                        finally
                        {
                            received.Set();
                        }
                    }, true);
                }
                catch (Exception ex)
                {
                    roundTripWatch.Stop();
                    callbackException = ex;
                    OnException?.Invoke(ex);
                    onLog?.Invoke("Time sync request failed: " + ex.Message);
                    return false;
                }

                Stopwatch waitWatch = Stopwatch.StartNew();
                bool replyReceived = false;
                while (waitWatch.ElapsedMilliseconds < requestTimeoutMs)
                {
                    if (received.Wait(25))
                    {
                        replyReceived = true;
                        break;
                    }

                    if (!IsTimeSynchronizationActive(generation))
                        break;
                }

                if (!replyReceived)
                {
                    roundTripWatch.Stop();
                    RemoveReplyCallback(replyID);
                    if (IsTimeSynchronizationActive(generation))
                        onLog?.Invoke("Time sync request timed out after " + requestTimeoutMs + " ms.");
                    return false;
                }

                roundTripWatch.Stop();
                if (!IsTimeSynchronizationActive(generation))
                    return false;

                if (callbackException != null)
                {
                    OnException?.Invoke(callbackException);
                    onLog?.Invoke("Time sync reply failed: " + callbackException.Message);
                    return false;
                }

                if (!hasSample)
                    return false;

                sample = receivedSample;
                return true;
            }
        }

        /// <summary>
        /// Reads server time from a time synchronization reply.
        /// </summary>
        private static bool TryReadServerTime(NetworkMessage reply, out double serverTime)
        {
            if (reply.Serializer.CanGetDouble())
            {
                serverTime = reply.Serializer.GetDouble();
                return true;
            }

            if (reply.Serializer.CanGetFloat())
            {
                serverTime = reply.Serializer.GetFloat();
                return true;
            }

            serverTime = 0d;
            return false;
        }

        /// <summary>
        /// Gets a stable offset from the lowest-latency samples.
        /// </summary>
        private static float GetFilteredServerTimeOffset(List<TimeSynchronizationSample> samples)
        {
            samples.Sort((a, b) => a.RoundTrip.CompareTo(b.RoundTrip));

            int count = Math.Max(1, (samples.Count + 1) / 2);
            float weightedOffset = 0f;
            float totalWeight = 0f;
            for (int i = 0; i < count; i++)
            {
                float weight = 1f / Math.Max(samples[i].RoundTrip, 0.0001f);
                weightedOffset += samples[i].Offset * weight;
                totalWeight += weight;
            }

            return weightedOffset / totalWeight;
        }

        /// <summary>
        /// Sleeps while allowing synchronization cancellation to be observed quickly.
        /// </summary>
        private bool SleepDuringTimeSynchronization(int generation, int delayMs)
        {
            int remainingMs = delayMs;
            while (remainingMs > 0)
            {
                if (!IsTimeSynchronizationActive(generation))
                    return false;

                int sleepMs = Math.Min(remainingMs, 25);
                Thread.Sleep(sleepMs);
                remainingMs -= sleepMs;
            }

            return IsTimeSynchronizationActive(generation);
        }

        /// <summary>
        /// Checks whether the current synchronization generation is still active.
        /// </summary>
        private bool IsTimeSynchronizationActive(int generation)
        {
            return isSynchronizingTime && generation == timeSynchronizationGeneration;
        }

        /// <summary>
        /// Finishes the active time synchronization generation.
        /// </summary>
        private void FinishTimeSynchronization(int generation)
        {
            lock (timeSynchronizationLock)
            {
                if (generation == timeSynchronizationGeneration)
                    isSynchronizingTime = false;
            }
        }

        /// <summary>
        /// Applies a new server time offset.
        /// </summary>
        /// <param name="offset">Offset to apply.</param>
        /// <param name="immediate">Whether the offset should be applied immediately.</param>
        private void SetServerTimeOffset(float offset, bool immediate)
        {
            TargetServerTimeOffset = offset;
            if (immediate || !SmoothServerTimeOffset || !hasServerTimeOffset)
                ServerTimeOffset = offset;

            DateTime now = DateTime.UtcNow;
            hasServerTimeOffset = true;
            lastServerTimeOffsetUpdateUtc = now;
            LastServerTimeSynchronizationUtc = now;
        }

        /// <summary>
        /// Smoothly moves the current server time offset toward the target offset.
        /// </summary>
        private void UpdateSmoothedServerTimeOffset()
        {
            if (!hasServerTimeOffset || !SmoothServerTimeOffset)
                return;

            DateTime now = DateTime.UtcNow;
            float deltaTime = (float)(now - lastServerTimeOffsetUpdateUtc).TotalSeconds;
            lastServerTimeOffsetUpdateUtc = now;

            if (deltaTime <= 0f)
                return;

            float t = 1f - (float)Math.Exp(-ServerTimeOffsetSmoothingSpeed * deltaTime);
            ServerTimeOffset += (TargetServerTimeOffset - ServerTimeOffset) * t;
            if (Math.Abs(TargetServerTimeOffset - ServerTimeOffset) < 0.0001f)
                ServerTimeOffset = TargetServerTimeOffset;
        }
        #endregion

        #region Public Utils
        /// <summary>
        /// Replace the client ID with a new one
        /// </summary>
        /// <param name="newID"> New ID to set</param>
        public void ReplaceClientID(uint newID)
        {
            Client.ID = newID;
            Client.UDP?.SendRegistration();
        }
        #endregion
    }
}
