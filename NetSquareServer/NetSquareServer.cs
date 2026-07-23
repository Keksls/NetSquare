using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquare.Server.Server;
using NetSquare.Server.Utils;
using NetSquare.Server.Worlds;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.Cryptography.X509Certificates;

namespace NetSquare.Server
{
    /// <summary>
    /// Represents the net square server component.
    /// </summary>
    public class NetSquareServer
    {
        #region DllImport
        [DllImport("kernel32.dll")]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        [DllImport("kernel32.dll")]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int nStdHandle);
        #endregion

        #region Events
        /// <summary>
        /// Gets or sets the client id counter value.
        /// </summary>
        public uint ClientIDCounter { get { return unchecked((uint)Interlocked.Read(ref clientIDCounter)); } }
        /// <summary>
        /// Occurs when client connected is raised.
        /// </summary>
        public event Action<uint> OnClientConnected;
        /// <summary>
        /// Occurs when client disconnected is raised.
        /// </summary>
        public event Action<uint> OnClientDisconnected;
        /// <summary>
        /// Occurs when message received is raised.
        /// </summary>
        public event Action<NetworkMessage> OnMessageReceived;
        /// <summary>
        /// Occurs when message send is raised.
        /// </summary>
        public event Action<byte[]> OnMessageSend;
        /// <summary>
        /// Occurs when time loop is raised.
        /// </summary>
        public event Action<float> OnTimeLoop;
        /// <summary>
        /// Stores the draw header override callback value.
        /// </summary>
        public Action<string> DrawHeaderOverrideCallback = null;
        #endregion

        #region Variables
        /// <summary>
        /// Gets or sets the time value.
        /// </summary>
        public float Time { get; private set; }
        /// <summary>
        /// Stores the monotonic server clock value.
        /// </summary>
        private readonly Stopwatch serverClock = new Stopwatch();
        /// <summary>
        /// Stores the server tick rate value.
        /// </summary>
        private float serverTickRate = 1f / 60f;
        /// <summary>
        /// Gets or sets the is started value.
        /// </summary>
        public bool IsStarted { get { return Listeners.Any(l => l.Listener.Active); } }
        /// <summary>
        /// Gets or sets the server i ps value.
        /// </summary>
        public HashSet<string> ServerIPs { get; private set; }
        /// <summary>
        /// Stores the listeners value.
        /// </summary>
        public List<TcpListener> Listeners = new List<TcpListener>();
        /// <summary>
        /// Stores the dispatcher value.
        /// </summary>
        public NetSquareDispatcher Dispatcher;
        /// <summary>
        /// Gets or sets the protocole type value.
        /// </summary>
        public NetSquareProtocoleType ProtocoleType { get; private set; }
        /// <summary>
        /// Gets whether this server requires TLS for every TCP connection.
        /// </summary>
        internal bool UseTLS { get; private set; }
        /// <summary>
        /// Gets whether UDP datagrams require sequence and MAC64 authentication.
        /// </summary>
        internal bool UseUdpAuthentication { get; private set; }
        /// <summary>
        /// Stores the certificate used by TLS listeners.
        /// </summary>
        internal X509Certificate2 TLSCertificate { get; private set; }
        /// <summary>
        /// Stores the message queue manager value.
        /// </summary>
        internal MessageQueueManager MessageQueueManager;
        /// <summary>
        /// Stores the worlds value.
        /// </summary>
        public WorldsManager Worlds;
        /// <summary>
        /// Stores the statistics value.
        /// </summary>
        public ServerStatisticsManager Statistics;
        /// <summary>
        /// Gets or sets the clients value.
        /// </summary>
        public ConcurrentDictionary<uint, ConnectedClient> Clients = new ConcurrentDictionary<uint, ConnectedClient>(); // ID Client => ConnectedClient
        /// <summary>
        /// Stores the disconnect notice timeout ms value.
        /// </summary>
        public static int DisconnectNoticeTimeoutMs = 500;
        /// <summary>
        /// Stores the get new client id value.
        /// </summary>
        public Func<uint> GetNewClientID;
        private long clientIDCounter;
        /// <summary>
        /// Cancels the server update worker during shutdown.
        /// </summary>
        private CancellationTokenSource serverStopCancellation;
        /// <summary>
        /// Stores the server update worker for deterministic shutdown.
        /// </summary>
        private Thread updateThread;
        /// <summary>
        /// Prevents concurrent server shutdown sequences.
        /// </summary>
        private int stopStarted;
        #endregion

        /// <summary>
        /// Create a new NetSquareServer
        /// </summary>
        /// <param name="protocoleType"> The protocole type to use (TCP, UDP, Both) </param>
        public NetSquareServer(NetSquareProtocoleType protocoleType = NetSquareProtocoleType.TCP_AND_UDP, bool useWorldManager = true)
        {
            ProtocoleType = protocoleType;
            Dispatcher = new NetSquareDispatcher();
            if (useWorldManager)
                Worlds = new WorldsManager(this);
            // Configuration must be explicitly initialized before server services are constructed.
            NetSquareConfiguration configuration = NetSquareConfigurationManager.Get<NetSquareConfiguration>();
            UseTLS = configuration.UseTLS;
            UseUdpAuthentication = configuration.UseUdpAuthentication;
            if (UseUdpAuthentication && !UseTLS && ProtocoleType == NetSquareProtocoleType.TCP_AND_UDP)
            {
                Writer.Write(
                    "UDP MAC64 is enabled without TLS. The UDP session key crosses TCP without transport encryption; enable TLS to protect it against on-path interception.",
                    ConsoleColor.DarkYellow);
            }
            if (UseTLS)
                TLSCertificate = LoadTLSCertificate(configuration);

            MessageQueueManager = new MessageQueueManager(
                this,
                configuration.NbQueueThreads,
                configuration.MessageQueueCapacity > 0 ? configuration.MessageQueueCapacity : 8192,
                configuration.WorkerStopTimeoutMilliseconds > 0
                    ? configuration.WorkerStopTimeoutMilliseconds : 5000);
            Statistics = new ServerStatisticsManager();
            // register client sync time
            Dispatcher.AddHeadAction(NetSquareMessageID.ClientSynchronizeTime, "ClientSyncTime", (message) =>
            {
                double serverTime = GetCurrentServerTimeSeconds();
                NetworkMessage reply = new NetworkMessage();
                if (ClientWantsHighPrecisionServerTime(message))
                    reply.Set(serverTime);
                else
                    reply.Set((float)serverTime);
                message.Reply(reply);
            });
            Dispatcher.AddHeadAction(NetSquareMessageID.Heartbeat, "ClientHeartbeat", ClientHeartbeat);
            Dispatcher.AddHeadAction(NetSquareMessageID.Disconnecting, "ClientDisconnecting", ClientDisconnecting);
            // set default client ID generator, can be override later by user
            GetNewClientID = GetNextSequentialClientID;
        }

        /// <summary>
        /// Loads and validates the private certificate configured for TLS listeners.
        /// </summary>
        /// <param name="configuration">Active server configuration.</param>
        /// <returns>The certificate containing the server private key.</returns>
        private static X509Certificate2 LoadTLSCertificate(NetSquareConfiguration configuration)
        {
            // Fail during server construction instead of accepting sockets with an unusable TLS setup.
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (string.IsNullOrWhiteSpace(configuration.TLSCertificatePath))
            {
                throw new InvalidOperationException(
                    "TLSCertificatePath is required when UseTLS is enabled.");
            }
            if (!File.Exists(configuration.TLSCertificatePath))
            {
                throw new FileNotFoundException(
                    "The configured TLS certificate file was not found.",
                    configuration.TLSCertificatePath);
            }

            X509Certificate2 certificate = new X509Certificate2(
                configuration.TLSCertificatePath,
                configuration.TLSCertificatePassword ?? string.Empty);
            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException("The configured TLS certificate has no private key.");
            return certificate;
        }

        /// <summary>
        /// Generates the next non-zero client ID atomically.
        /// </summary>
        /// <returns>The next sequential client ID.</returns>
        private uint GetNextSequentialClientID()
        {
            // Interlocked prevents concurrent handshake threads from receiving the same ID.
            uint clientID;
            do
            {
                clientID = unchecked((uint)Interlocked.Increment(ref clientIDCounter));
            }
            while (clientID == 0);

            return clientID;
        }

        /// <summary>
        /// Start the server
        /// </summary>
        /// <param name="port"> The port to use </param>
        /// <param name="allowLocalIP"> Allow local IP </param>
        /// <param name="bindDispatcher"> Bind dispatcher </param>
        /// <param name="CheckBlackList"> Check black list </param>
        private void ServerRoutine(int port, bool allowLocalIP, bool bindDispatcher, bool CheckBlackList)
        {
            // Reuse the concrete project configuration through its NetSquare base contract.
            NetSquareConfiguration configuration = NetSquareConfigurationManager.Get<NetSquareConfiguration>();
            Writer.StartDisplayLog();
            // Start by drawing header
            DrawHeader("v" + Assembly.GetAssembly(typeof(NetSquareServer)).GetName().Version);

            // Display the configuration that was explicitly initialized by the consuming project.
            Writer.Write_Server("Configuration...", ConsoleColor.DarkYellow, false);
            if (configuration.LockConsole)
            {
                try
                {
                    const uint ENABLE_QUICK_EDIT = 0x0040;
                    IntPtr consoleHandle = GetStdHandle(-10);
                    uint consoleMode;
                    GetConsoleMode(consoleHandle, out consoleMode);
                    consoleMode &= ~ENABLE_QUICK_EDIT;
                    SetConsoleMode(consoleHandle, consoleMode);
                }
                catch { Writer.Write("Fail to set Console unselectable. Don't worry, everything is OK.", ConsoleColor.DarkGray); }
            }

            Writer.Write("OK", ConsoleColor.Green);
            Writer.Write(configuration.ToString(), ConsoleColor.Yellow, false);
            if (CheckBlackList)
                BlackListManager.Initialize();

            if (bindDispatcher)
            {
                Writer.Write_Server("Loading Network Methods...", ConsoleColor.DarkYellow, false);
                if (Dispatcher == null)
                    Dispatcher = new NetSquareDispatcher();
                Dispatcher.AutoBindHeadActionsFromAttributes();
                Writer.Write(Dispatcher.Count.ToString(), ConsoleColor.Green);
            }

            Interlocked.Exchange(ref stopStarted, 0);
            serverStopCancellation = new CancellationTokenSource();

            port = port > 0 ? port : configuration.Port;
            BindServerIP(allowLocalIP);

            // Queues must accept messages before the first listener completes a handshake.
            Writer.Write_Server("Starting Message Queues...", ConsoleColor.DarkYellow, false);
            MessageQueueManager.StartQueues();

            // Start TCP server
            if (!StartTCPServer(port, CheckBlackList))
            {
                MessageQueueManager.StopQueues();
                Writer.Write("ERROR : Can't Start TCP Server...", ConsoleColor.Red);
                return;
            }

            // start update loop
            Writer.Write_Server("Starting Update Loop...", ConsoleColor.DarkYellow, false);
            serverTickRate = 1f / Math.Max(0.1f, configuration.UpdateFrequencyHz);
            serverClock.Restart();
            Time = 0f;
            CancellationToken updateToken = serverStopCancellation.Token;
            updateThread = new Thread(() => UpdateLoop(updateToken));
            updateThread.IsBackground = true;
            updateThread.Name = "NetSquare server update";
            updateThread.Start();
            Writer.Write("Started", ConsoleColor.Green);

            Statistics.StartReceivingStatistics(this);
            Writer.Write("Started", ConsoleColor.Green);
        }

        #region Update Loop
        /// <summary>
        /// Update loop of the server
        /// </summary>
        private void UpdateLoop(CancellationToken cancellationToken)
        {
            if (!serverClock.IsRunning)
                serverClock.Restart();

            float lastTime = Time;
            try
            {
                while (!cancellationToken.IsCancellationRequested && IsStarted)
                {
                    Time = (float)GetCurrentServerTimeSeconds();
                    if (Time - lastTime >= serverTickRate)
                    {
                        lastTime = Time;
                        OnTimeLoop?.Invoke(Time);
                    }
                    if (cancellationToken.WaitHandle.WaitOne(1))
                        return;
                }
            }
            finally
            {
                serverClock.Stop();
            }
        }
        #endregion

        #region Time Synchronization
        /// <summary>
        /// Gets the current monotonic server time in seconds.
        /// </summary>
        private double GetCurrentServerTimeSeconds()
        {
            return serverClock.IsRunning ? serverClock.Elapsed.TotalSeconds : Time;
        }

        /// <summary>
        /// Handles a client heartbeat and returns server time for RTT measurement.
        /// </summary>
        private void ClientHeartbeat(NetworkMessage message)
        {
            ConnectedClient client = message.Client ?? SafeGetClient(message.ClientID);
            if (client != null)
            {
                client.MarkMessageReceived();
                try
                {
                    if (message.Serializer.CanGetByte())
                        message.Serializer.GetByte();

                    if (message.Serializer.CanGetUShort())
                        client.Ping = message.Serializer.GetUShort();
                }
                catch (Exception ex)
                {
                    Writer.Write("Invalid heartbeat from client " + client.ID + " : " + ex.Message, ConsoleColor.DarkYellow);
                }
            }

            message.Reply(new NetworkMessage().Set(GetCurrentServerTimeSeconds()));
        }
        /// <summary>
        /// Returns whether the client asked for the high precision time synchronization payload.
        /// </summary>
        private static bool ClientWantsHighPrecisionServerTime(NetworkMessage message)
        {
            return message.Serializer.CanGetByte() && message.Serializer.GetByte() >= 1;
        }
        #endregion

        #region Start/Stop Server
        /// <summary>
        /// Start the server
        /// </summary>
        /// <param name="port"> The port to use </param>
        /// <param name="allowLocalIP"> Allow local IP </param>
        /// <param name="bindDispatcher"> Bind dispatcher </param>
        /// <param name="CheckBlackList"> Check black list </param>
        public void Start(int port = -1, bool allowLocalIP = true, bool bindDispatcher = true, bool CheckBlackList = true)
        {
            if (Debugger.IsAttached)
                ServerRoutine(port, allowLocalIP, bindDispatcher, CheckBlackList);
            else
            {
                Loop:
                try
                {
                    ServerRoutine(port, allowLocalIP, bindDispatcher, CheckBlackList);
                }
                catch (Exception ex)
                {
                    Writer.Write(ex.Message + Environment.NewLine + Environment.NewLine + ex.StackTrace, ConsoleColor.Red);
                    goto Loop;
                }
            }
        }

        /// <summary>
        /// Bind the server IP addresses
        /// </summary>
        /// <param name="allowLocalIP"> Allow local IP </param>
        private void BindServerIP(bool allowLocalIP)
        {
            Writer.Write("Getting server IPs : ", ConsoleColor.Gray);
            ServerIPs = new HashSet<string>(); var ipSorted = GetIPAddresses();
            foreach (var ipAddr in ipSorted)
            {
                if ((ipAddr.ToString() != "127.0.0.1" || allowLocalIP) && ipAddr.AddressFamily == AddressFamily.InterNetwork)
                {
                    ServerIPs.Add(ipAddr.ToString());
                    Writer.Write_Server("  - " + ipAddr.ToString(), ConsoleColor.Yellow);
                }
                else
                    Writer.Write_Server("  - Switch IP : " + ipAddr.ToString(), ConsoleColor.DarkGray);
            }
        }

        /// <summary>
        /// Start the TCP server
        /// </summary>
        /// <param name="port"> The port to use </param>
        /// <param name="CheckBlackList"> Check black list </param>
        /// <returns> True if the server started successfully, false otherwise </returns>
        private bool StartTCPServer(int port, bool CheckBlackList)
        {
            Writer.Write_Server("Starting TCP server on port " + port.ToString() + "...", ConsoleColor.DarkYellow);
            bool anyNicFailed = false;
            foreach (string ipAddr in ServerIPs)
            {
                try
                {
                    TcpListener listener = new TcpListener(this, IPAddress.Parse(ipAddr), port, CheckBlackList);
                    Listeners.Add(listener);
                }
                catch (SocketException ex)
                {
                    Writer.Write("Fail to start server : " + ex.ToString(), ConsoleColor.Red);
                    anyNicFailed = true;
                }
            }

            if (!IsStarted)
                throw new InvalidOperationException("Port was already occupied for all network interfaces");

            if (anyNicFailed)
            {
                Stop();
                throw new InvalidOperationException("Port was already occupied for one or more network interfaces.");
            }

            if (IsStarted)
            {
                Writer.Write_Server("TCP server started Success (" + Listeners.Count + " IP)", ConsoleColor.Green);
                return true;
            }
            else
            {
                Writer.Write("FAIL", ConsoleColor.Red);
                return false;
            }
        }

        /// <summary>
        /// Stop the server
        /// </summary>
        public void Stop()
        {
            if (Interlocked.Exchange(ref stopStarted, 1) != 0)
                return;

            serverStopCancellation?.Cancel();
            try { Statistics?.Stop(); }
            catch (Exception ex) { Writer.Write("Statistics worker shutdown failed: " + ex, ConsoleColor.DarkYellow); }

            bool listenersStopped = true;
            foreach (TcpListener listener in Listeners)
            {
                try { listenersStopped &= listener.Stop(); }
                catch (Exception ex)
                {
                    listenersStopped = false;
                    Writer.Write("TCP listener shutdown failed: " + ex, ConsoleColor.DarkYellow);
                }
            }
            if (!listenersStopped)
                Writer.Write("One or more TCP listeners exceeded their shutdown timeout.", ConsoleColor.DarkYellow);
            NotifyClientsDisconnecting(new DisconnectInfo(DisconnectReason.ServerShutdown));
            try
            {
                if (MessageQueueManager != null && !MessageQueueManager.StopQueues())
                    Writer.Write("One or more message workers exceeded their shutdown timeout.", ConsoleColor.DarkYellow);
            }
            catch (Exception ex) { Writer.Write("Message queue shutdown failed: " + ex, ConsoleColor.DarkYellow); }
            Thread threadToJoin = updateThread;
            if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
            {
                int configuredTimeout = NetSquareConfigurationManager
                    .Get<NetSquareConfiguration>().WorkerStopTimeoutMilliseconds;
                int timeout = configuredTimeout > 0 ? configuredTimeout : 5000;
                if (!threadToJoin.Join(timeout))
                    Writer.Write("Server update worker did not stop in time.", ConsoleColor.DarkYellow);
            }
            updateThread = null;
            DisconnectAllClientsWithoutNotice();
            Listeners.Clear();
        }

        /// <summary>
        /// Disconnects a client with the default server-request reason.
        /// </summary>
        /// <param name="clientID">Client ID.</param>
        public void DisconnectClient(uint clientID)
        {
            DisconnectClient(clientID, DisconnectReason.ServerRequest);
        }

        /// <summary>
        /// Disconnects a client with a typed reason.
        /// </summary>
        /// <param name="clientID">Client ID.</param>
        /// <param name="reason">Reason sent before closing the socket.</param>
        public void DisconnectClient(uint clientID, DisconnectReason reason)
        {
            DisconnectClient(clientID, new DisconnectInfo(reason));
        }

        /// <summary>
        /// Disconnects a client with complete typed feedback.
        /// </summary>
        /// <param name="clientID">Client ID.</param>
        /// <param name="info">Feedback sent before closing the socket.</param>
        public void DisconnectClient(uint clientID, DisconnectInfo info)
        {
            ConnectedClient client;
            if (Clients.TryGetValue(clientID, out client))
                DisconnectClient(client, info);
        }

        /// <summary>
        /// Disconnects a client with the default server-request reason.
        /// </summary>
        /// <param name="client">Client to disconnect.</param>
        public void DisconnectClient(ConnectedClient client)
        {
            DisconnectClient(client, DisconnectReason.ServerRequest);
        }

        /// <summary>
        /// Disconnects a client with a typed reason.
        /// </summary>
        /// <param name="client">Client to disconnect.</param>
        /// <param name="reason">Reason sent before closing the socket.</param>
        public void DisconnectClient(ConnectedClient client, DisconnectReason reason)
        {
            DisconnectClient(client, new DisconnectInfo(reason));
        }

        /// <summary>
        /// Disconnects a client with complete typed feedback.
        /// </summary>
        /// <param name="client">Client to disconnect.</param>
        /// <param name="info">Feedback sent before closing the socket.</param>
        public void DisconnectClient(ConnectedClient client, DisconnectInfo info)
        {
            DisconnectClientInternal(client, true, info);
        }
        #endregion

        #region Disconnection Notices
        /// <summary>
        /// Disconnects a client and optionally sends typed feedback first.
        /// </summary>
        /// <param name="client">Client to disconnect.</param>
        /// <param name="notifyRemote">If true, send a disconnect notice before closing.</param>
        /// <param name="info">Feedback sent to the client.</param>
        private void DisconnectClientInternal(ConnectedClient client, bool notifyRemote, DisconnectInfo info)
        {
            if (client == null)
                return;

            info = info ?? new DisconnectInfo(DisconnectReason.Unknown);
            if (notifyRemote && EnqueueDisconnectingNotice(client, info))
                client.WaitForPendingTCPMessages(DisconnectNoticeTimeoutMs);

            Server_ClientDisconnected(client);
        }

        /// <summary>
        /// Notifies all clients that the server is disconnecting.
        /// </summary>
        /// <param name="info">Feedback sent to every connected client.</param>
        private void NotifyClientsDisconnecting(DisconnectInfo info)
        {
            List<ConnectedClient> notifiedClients = new List<ConnectedClient>();
            foreach (ConnectedClient client in Clients.Values.ToList())
            {
                if (EnqueueDisconnectingNotice(client, info))
                    notifiedClients.Add(client);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (ConnectedClient client in notifiedClients)
            {
                int remainingMs = DisconnectNoticeTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    return;

                client.WaitForPendingTCPMessages(remainingMs);
            }
        }

        /// <summary>
        /// Enqueues typed disconnection feedback to a client.
        /// </summary>
        /// <param name="client">Client to notify.</param>
        /// <param name="info">Feedback sent before closure.</param>
        /// <returns>True when the notice was enqueued.</returns>
        private bool EnqueueDisconnectingNotice(ConnectedClient client, DisconnectInfo info)
        {
            if (client == null || client.TcpSocket == null || !client.TcpSocket.Connected)
                return false;

            try
            {
                client.AddTCPMessage(ConnectionFeedbackProtocol.CreateDisconnectMessage(info, client.ID));
                return true;
            }
            catch (Exception ex)
            {
                Writer.Write("Fail to notify client " + client.ID + " disconnection : " + ex.ToString(), ConsoleColor.Red);
                return false;
            }
        }

        /// <summary>
        /// Disconnect all clients without sending another notice.
        /// </summary>
        private void DisconnectAllClientsWithoutNotice()
        {
            foreach (ConnectedClient client in Clients.Values.ToList())
                DisconnectClientInternal(client, false, null);
        }
        #endregion

        #region Sending and Rep
        /// <summary>
        /// Prepare a reply
        /// </summary>
        /// <param name="messageFrom"> The message from </param>
        /// <param name="message"> The message </param>
        public void PrepareReply(NetworkMessage messageFrom, NetworkMessage message)
        {
            message.HeadID = messageFrom.HeadID;
            message.ClientID = messageFrom.ClientID;
            message.MsgType = (byte)NetSquareMessageType.Reply;
            message.ReplyID = messageFrom.ReplyID;
        }

        /// <summary>
        /// Reply to a message
        /// </summary>
        /// <param name="messageFrom"> The message from </param>
        /// <param name="message"> The message </param>
        public void Reply(NetworkMessage messageFrom, NetworkMessage message)
        {
            PrepareReply(messageFrom, message);
            messageFrom.Client.AddTCPMessage(message);
        }

        /// <summary>
        /// Send a message to a client
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="client"> The client </param>
        public void SendToClient(NetworkMessage message, ConnectedClient client)
        {
            client.AddTCPMessage(message);
        }

        /// <summary>
        /// Send a message to a client
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClient(byte[] message, uint clientID)
        {
            ConnectedClient client;
            if (Clients.TryGetValue(clientID, out client))
                client.AddTCPMessage(message);
        }

        /// <summary>
        /// Send a message to a client
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClient(NetworkMessage message, uint clientID)
        {
            ConnectedClient client;
            if (Clients.TryGetValue(clientID, out client))
                client.AddTCPMessage(message);
        }

        /// <summary>
        /// Send a message to some clients
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClients(NetworkMessage message, List<ConnectedClient> clients)
        {
            byte[] data = message.Serialize();
            lock (clients)
            {
                foreach (ConnectedClient client in clients)
                    client?.AddTCPMessage(data);
            }
        }

        /// <summary>
        /// Send a message to some clients
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClients(NetworkMessage message, IEnumerable<uint> clients)
        {
            byte[] data = message.Serialize();
            foreach (uint clientID in clients)
            {
                ConnectedClient client;
                if (Clients.TryGetValue(clientID, out client))
                    client.AddTCPMessage(data);
            }
        }

        /// <summary>
        /// Send a message to some clients
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClients(byte[] message, IEnumerable<uint> clients)
        {
            foreach (uint clientID in clients)
            {
                ConnectedClient client;
                if (Clients.TryGetValue(clientID, out client))
                    client.AddTCPMessage(message);
            }
        }

        /// <summary>
        /// Broadcast a message to all clients
        /// </summary>
        /// <param name="message"> The message </param>
        public void Broadcast(NetworkMessage message)
        {
            byte[] data = message.Serialize();
            foreach (KeyValuePair<uint, ConnectedClient> pair in Clients)
                pair.Value?.AddTCPMessage(data);
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClientsUnreliable(NetworkMessage message, IEnumerable<uint> clients)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (clients == null)
                throw new ArgumentNullException(nameof(clients));

            // One immutable serialized buffer is safe to share across every per-client UDP queue.
            ushort headID = message.HeadID;
            byte[] data = message.Serialize();
            foreach (uint clientID in clients)
            {
                ConnectedClient client;
                if (Clients.TryGetValue(clientID, out client))
                    client.AddUnreliableMessage(headID, data);
            }
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="client"> The client </param>
        public void SendToClientUnreliable(NetworkMessage message, ConnectedClient client)
        {
            message.Client = client;
            client.AddUnreliableMessage(message);
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="headID"> The head ID </param>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClientUnreliable(ushort headID, byte[] message, uint clientID)
        {
            ConnectedClient client;
            if (Clients.TryGetValue(clientID, out client))
                client.AddUnreliableMessage(headID, message);
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClientUnreliable(NetworkMessage message, uint clientID)
        {
            ConnectedClient client;
            if (!Clients.TryGetValue(clientID, out client))
                return;

            message.Client = client;
            client.AddUnreliableMessage(message);
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="headID"> The head ID </param>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClientsUnreliable(ushort headID, byte[] message, IEnumerable<uint> clients)
        {
            foreach (uint clientID in clients)
            {
                ConnectedClient client;
                if (Clients.TryGetValue(clientID, out client))
                    client.AddUnreliableMessage(headID, message);
            }
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="headID"> The head ID </param>
        /// <param name="message"> The message </param>
        /// <param name="client"> The client </param>
        public void SendToClientUnreliable(ushort headID, byte[] message, ConnectedClient client)
        {
            client.AddUnreliableMessage(headID, message);
        }
        #endregion

        #region ServerEvent
        /// <summary>
        /// Event when a client is disconnected
        /// </summary>
        /// <param name="client"> The client </param>
        internal void Server_ClientDisconnected(ConnectedClient client)
        {
            lock (Clients)
            {
                if (!Clients.ContainsKey(client.ID))
                    return;
                OnClientDisconnected?.Invoke(client.ID);
                // remove client from world
                Worlds?.ClientDisconnected(client.ID);
                // supprime des clients connectés
                ConnectedClient c = null;
                while (!Clients.TryRemove(client.ID, out c))
                {
                    if (!Clients.ContainsKey(client.ID))
                        return;
                    else
                        continue;
                }
                // unregister client event
                client.UDP?.UnregisterServerClient();
                client.OnMessageReceived -= MessageReceived;
                client.OnMessageSend -= MessageSended;
                // try clean disconnect if not already
                client.CloseTcpTransport();
                Writer.Write("Client " + client.ID + " disconnected", ConsoleColor.Green);
                //Writer.Write(Environment.StackTrace, ConsoleColor.Gray);
            }
        }


        /// <summary>
        /// Removes a client that failed transport validation before OnClientConnected was published.
        /// </summary>
        /// <param name="client">Pending client to remove.</param>
        internal void RemovePendingClient(ConnectedClient client)
        {
            // Pending clients must not emit a misleading public disconnection event.
            if (client == null)
                return;

            ConnectedClient removedClient;
            if (Clients.TryRemove(client.ID, out removedClient))
            {
                removedClient.UDP?.UnregisterServerClient();
                removedClient.OnMessageReceived -= MessageReceived;
                removedClient.OnMessageSend -= MessageSended;
                removedClient.OnDisconected -= Client_OnDisconected;
            }
            else
            {
                removedClient = client;
            }

            removedClient.CloseTcpTransport();
        }
        /// <summary>
        /// Event when a client is connected
        /// </summary>
        /// <param name="client"> The client </param>
        /// <param name="id"> The ID </param>
        internal void Server_ClientConnected(ConnectedClient client, uint id)
        {
            Writer.Write("New client connected !", ConsoleColor.Green);
            OnClientConnected?.Invoke(id);
        }

        /// <summary>
        /// Event when a client is connected
        /// </summary>
        /// <param name="clientID"> The client ID </param>
        private void Client_OnDisconected(uint clientID)
        {
            var client = SafeGetClient(clientID);
            if (client != null)
                Server_ClientDisconnected(client);
        }

        /// <summary>
        /// Event when a client announces it is disconnecting.
        /// </summary>
        /// <param name="message">The message.</param>
        private void ClientDisconnecting(NetworkMessage message)
        {
            ConnectedClient client = message.Client ?? SafeGetClient(message.ClientID);
            if (client != null)
                Server_ClientDisconnected(client);
        }

        /// <summary>
        /// Event when a message is received
        /// </summary>
        /// <param name="message"> The message </param>
        internal void MessageReceived(NetworkMessage message)
        {
            // The server-side connection is authoritative; never trust the sender-controlled header ID.
            if (message.Client != null)
                message.ClientID = message.Client.ID;

            MessageQueueManager.MessageReceived(message);
            OnMessageReceived?.Invoke(message);
        }

        /// <summary>
        /// Event when a message is sended
        /// </summary>
        /// <param name="data"> The data </param>
        private void MessageSended(byte[] data)
        {
            OnMessageSend?.Invoke(data);
        }
        #endregion

        #region Public Utils
        /// <summary>
        /// Replace a client ID
        /// </summary>
        /// <param name="oldID"> The old ID </param>
        /// <param name="newID"> The new ID </param>
        /// <returns> True if the client ID was replaced, false otherwise </returns>
        public bool ReplaceClientID(uint oldID, uint newID)
        {
            if (Clients.ContainsKey(oldID) && !Clients.ContainsKey(newID))
            {
                ConnectedClient client = Clients[oldID];
                client.UDP?.UnregisterServerClient();
                client.ID = newID;
                Clients.TryAdd(newID, client);
                Clients.TryRemove(oldID, out ConnectedClient oldClient);
                client.UDP?.RegisterServerClient();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Check if a client is connected
        /// </summary>
        /// <param name="clientID"> The client ID </param>
        /// <returns> True if the client is connected, false otherwise </returns>
        public bool IsClientConnected(uint clientID)
        {
            return Clients.ContainsKey(clientID);
        }

        /// <summary>
        /// Get a client
        /// </summary>
        /// <param name="clientID"> The client ID </param>
        /// <returns> The client </returns>
        public ConnectedClient GetClient(uint clientID)
        {
            return Clients[clientID];
        }

        /// <summary>
        /// Get a client safely
        /// </summary>
        /// <param name="clientID"> The client ID </param>
        /// <returns> The client </returns>
        public ConnectedClient SafeGetClient(uint clientID)
        {
            ConnectedClient client = null;
            Clients.TryGetValue(clientID, out client);
            return client;
        }

        /// <summary>
        /// Add a client
        /// </summary>
        /// <param name="client"> The client </param>
        /// <returns> The client ID </returns>
        public uint AddClient(ConnectedClient client)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            Func<uint> clientIDGenerator = GetNewClientID;
            if (clientIDGenerator == null)
                throw new InvalidOperationException("No client ID generator is configured.");

            const int maxGenerationAttempts = 1024;
            bool added = false;
            for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
            {
                // Use one stable generator for the complete allocation attempt.
                uint clientID = clientIDGenerator();
                if (clientID == 0)
                    continue;

                client.ID = clientID;
                if (Clients.TryAdd(clientID, client))
                {
                    added = true;
                    break;
                }
            }

            if (!added)
                throw new InvalidOperationException("Unable to allocate a unique non-zero client ID.");

            client.UDP?.RegisterServerClient();
            client.OnMessageReceived += MessageReceived;
            client.OnMessageSend += MessageSended;
            client.OnDisconected += Client_OnDisconected;
            return client.ID;
        }

        /// <summary>
        /// Get the number of clients that are verifying
        /// </summary>
        /// <returns> The number of clients that are verifying </returns>
        public int GetNbVerifyingClients()
        {
            int nb = 0;
            foreach (var listner in Listeners)
                nb += listner.VerifyingClients;
            return nb;
        }

        /// <summary>
        /// Get some connected clients from their IDs
        /// </summary>
        /// <param name="clientsIDs"> The clients IDs </param>
        /// <returns> The connected clients </returns>
        public List<ConnectedClient> GetTcpClientsFromIDs(IEnumerable<uint> clientsIDs)
        {
            List<ConnectedClient> clients = new List<ConnectedClient>();
            foreach (uint clientID in clientsIDs)
            {
                if (Clients.ContainsKey(clientID))
                    clients.Add(Clients[clientID]);
            }
            return clients;
        }

        /// <summary>
        /// Get all connected clients
        /// </summary>
        /// <returns> The connected clients </returns>
        public List<ConnectedClient> GetAllClients()
        {
            List<ConnectedClient> clients = new List<ConnectedClient>();
            foreach (var client in Clients)
                clients.Add(client.Value);
            return clients;
        }

        /// <summary>
        /// Get all IP addresses of the server
        /// </summary>
        /// <returns> The IP addresses </returns>
        public IEnumerable<IPAddress> GetIPAddresses()
        {
            List<IPAddress> ipAddresses = new List<IPAddress>();
            IEnumerable<NetworkInterface> enabledNetInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up);
            foreach (NetworkInterface netInterface in enabledNetInterfaces)
            {
                IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                    if (!ipAddresses.Contains(addr.Address))
                        ipAddresses.Add(addr.Address);
            }
            var ipSorted = ipAddresses.OrderByDescending(ip => RankIpAddress(ip)).ToList();
            return ipSorted;
        }

        /// <summary>
        /// Get all listening IPs of the server
        /// </summary>
        /// <returns> The listening IPs </returns>
        public List<IPAddress> GetListeningIPs()
        {
            List<IPAddress> listenIps = new List<IPAddress>();
            foreach (var l in Listeners)
                if (!listenIps.Contains(l.IPAddress))
                    listenIps.Add(l.IPAddress);
            return listenIps.OrderByDescending(ip => RankIpAddress(ip)).ToList();
        }
        #endregion

        #region private Utils
        /// <summary>
        /// Draw the header of the server in the console
        /// </summary>
        /// <param name="version"> The version </param>
        private void DrawHeader(string version)
        {
            if (DrawHeaderOverrideCallback != null)
                DrawHeaderOverrideCallback(version);
            else
            {
                Writer.Title("NetSquare Server " + version);
                Writer.Write(@"   _   _      _  ", ConsoleColor.White, false); Writer.Write(@" _____  ", ConsoleColor.Red);
                Writer.Write(@"  | \ | |    | | ", ConsoleColor.White, false); Writer.Write(@"/  ___| ", ConsoleColor.Red);
                Writer.Write(@"  |  \| | ___| |_", ConsoleColor.White, false); Writer.Write(@"\ `--.  __ _ _   _  __ _ _ __ ___ ", ConsoleColor.Red);
                Writer.Write(@"  | . ` |/ _ \ __|", ConsoleColor.White, false); Writer.Write(@"`--. \/ _` | | | |/ _` | '__/ _ \", ConsoleColor.Red);
                Writer.Write(@"  | |\  |  __/ |_", ConsoleColor.White, false); Writer.Write(@"/\__/ / (_| | |_| | (_| | | |  __/", ConsoleColor.Red);
                Writer.Write(@"  \_| \_/\___|\__", ConsoleColor.White, false); Writer.Write(@"\____/ \__, |\__,_|\__,_|_|  \___|", ConsoleColor.Red);
                Writer.Write(@"                 ", ConsoleColor.White, false); Writer.Write(@"          | |                    ", ConsoleColor.Red);
                Writer.Write(@"                 ", ConsoleColor.White, false); Writer.Write(@"          |_|                    ", ConsoleColor.Red);
                Writer.Write(@"                          by ", ConsoleColor.White, false);
                Writer.Write(@"Keks                                     ", ConsoleColor.Red, false);
                Writer.Write(version + "\n\n", ConsoleColor.White);
            }
        }

        /// <summary>
        /// Rank an IP address
        /// </summary>
        /// <param name="addr"> The IP address </param>
        /// <returns> The rank score </returns>
        private int RankIpAddress(IPAddress addr)
        {
            int rankScore = 1000;
            if (IPAddress.IsLoopback(addr))
                rankScore = 300;
            else if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                rankScore += 100;
                if (addr.GetAddressBytes().Take(2).SequenceEqual(new byte[] { 169, 254 }))
                    rankScore = 0;
            }
            if (rankScore > 500)
                foreach (var nic in TryGetCurrentNetworkInterfaces())
                {
                    var ipProps = nic.GetIPProperties();
                    if (ipProps.GatewayAddresses.Any())
                    {
                        if (ipProps.UnicastAddresses.Any(u => u.Address.Equals(addr)))
                            rankScore += 1000;
                        break;
                    }
                }
            return rankScore;
        }

        /// <summary>
        /// Try to get the current network interfaces
        /// </summary>
        /// <returns> The network interfaces </returns>
        private static IEnumerable<NetworkInterface> TryGetCurrentNetworkInterfaces()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().Where(ni => ni.OperationalStatus == OperationalStatus.Up);
            }
            catch (NetworkInformationException)
            {
                return Enumerable.Empty<NetworkInterface>();
            }
        }
        #endregion
    }
}
