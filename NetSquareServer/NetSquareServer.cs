using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquare.Server.Server;
using NetSquare.Server.Utils;
using NetSquare.Server.Worlds;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetSquare.Server
{
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
        public uint ClientIDCounter { get; private set; }
        public event Action BeforeLoadConfiguration_StepOne;
        public event Action AfterLoadConfiguration_StepTwo;
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<NetworkMessage> OnMessageReceived;
        public event Action<byte[]> OnMessageSend;
        public event Action<float> OnTimeLoop;
        public Action<string> DrawHeaderOverrideCallback = null;
        #endregion

        #region Variables
        public float Time { get; private set; }
        private float serverTickRate = 1f / 60f;
        public bool IsStarted { get { return Listeners.Any(l => l.Listener.Active); } }
        public HashSet<string> ServerIPs { get; private set; }
        public List<TcpListener> Listeners = new List<TcpListener>();
        public NetSquareDispatcher Dispatcher;
        public NetSquareProtocoleType ProtocoleType { get; private set; }
        internal MessageQueueManager MessageQueueManager;
        public WorldsManager Worlds;
        public ServerStatisticsManager Statistics;
        public ConcurrentDictionary<uint, ConnectedClient> Clients = new ConcurrentDictionary<uint, ConnectedClient>(); // ID Client => ConnectedClient
        public Func<uint> GetNewClientID;
        #endregion

        /// <summary>
        /// Create a new NetSquareServer
        /// </summary>
        /// <param name="protocoleType"> The protocole type to use (TCP, UDP, Both) </param>
        public NetSquareServer(NetSquareProtocoleType protocoleType = NetSquareProtocoleType.TCP_AND_UDP)
        {
            ProtocoleType = protocoleType;
            Dispatcher = new NetSquareDispatcher();
            Worlds = new WorldsManager(this);
            MessageQueueManager = new MessageQueueManager(this, NetSquareConfigurationManager.Configuration.NbQueueThreads);
            Statistics = new ServerStatisticsManager();
            // register client sync time
            Dispatcher.AddHeadAction(NetSquareMessageID.ClientSynchronizeTime, "ClientSyncTime", (message) =>
            {
                message.Reply(new NetworkMessage().Set(Time));
            });
            // set default client ID generator, can be override later by user
            GetNewClientID = () => { return ++ClientIDCounter; };
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
            Writer.StartDisplayLog();
            // Start by drawing header
            DrawHeader("v" + Assembly.GetAssembly(typeof(NetSquareServer)).GetName().Version);
            BeforeLoadConfiguration_StepOne?.Invoke();

            // Load Configuration
            Writer.Write_Server("Loading Configuration...", ConsoleColor.DarkYellow, false);
            if (NetSquareConfigurationManager.Configuration != null)
            {
                // Lock console if wanted to prevent selection thread sleep
                if (NetSquareConfigurationManager.Configuration.LockConsole)
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
                Writer.Write(NetSquareConfigurationManager.Configuration.ToString(), ConsoleColor.Yellow, false);

                AfterLoadConfiguration_StepTwo?.Invoke();
                BlackListManager.Initialize();

                if (bindDispatcher)
                {
                    Writer.Write_Server("Loading Network Methods...", ConsoleColor.DarkYellow, false);
                    if (Dispatcher == null)
                        Dispatcher = new NetSquareDispatcher();
                    Dispatcher.AutoBindHeadActionsFromAttributes();
                    Writer.Write(Dispatcher.Count.ToString(), ConsoleColor.Green);
                }

                port = port > 0 ? port : NetSquareConfigurationManager.Configuration.Port;
                BindServerIP(allowLocalIP);

                // Start TCP server
                if (!StartTCPServer(port, CheckBlackList))
                {
                    Writer.Write("ERROR : Can't Start TCP Server...", ConsoleColor.Red);
                    return;
                }

                // start update loop
                Writer.Write_Server("Starting Update Loop...", ConsoleColor.DarkYellow, false);
                serverTickRate = 1f / NetSquareConfigurationManager.Configuration.UpdateFrequencyHz;
                Thread updateThread = new Thread(UpdateLoop);
                updateThread.Start();
                Writer.Write("Started", ConsoleColor.Green);

                // start message queue
                Writer.Write_Server("Starting Message Queues...", ConsoleColor.DarkYellow, false);
                MessageQueueManager.StartQueues();
                Statistics.StartReceivingStatistics(this);
                Writer.Write("Started", ConsoleColor.Green);
            }
            else
            {
                NetSquareConfiguration Configuration = new NetSquareConfiguration();
                NetSquareConfigurationManager.SaveConfiguration(Configuration);
                Writer.Write("No configuration file finded.", ConsoleColor.Red);
                Writer.Write("A new configuration file have been created. Please restart the server.", ConsoleColor.DarkYellow);
            }
        }

        #region Update Loop
        /// <summary>
        /// Update loop of the server
        /// </summary>
        private void UpdateLoop()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            float lastTime = Time;
            while (IsStarted)
            {
                Time = sw.ElapsedMilliseconds / 1000f;
                if (Time - lastTime >= serverTickRate)
                {
                    lastTime = Time;
                    OnTimeLoop?.Invoke(Time);
                }
                Thread.Sleep(1);
            }
            sw.Stop();
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
            Listeners.ForEach(l => l.Stop());
            while (Listeners.Any(l => l.Listener.Active))
            {
                Thread.Sleep(100);
            };
            Listeners.Clear();
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
            if (Clients.ContainsKey(clientID))
                Clients[clientID].AddTCPMessage(message);
        }

        /// <summary>
        /// Send a message to a client
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClient(NetworkMessage message, uint clientID)
        {
            if (Clients.ContainsKey(clientID))
                Clients[clientID].AddTCPMessage(message);
        }

        /// <summary>
        /// Send a message to a client
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClient(NetworkMessage message, UInt24 clientID)
        {
            SendToClient(message, clientID.UInt32);
        }

        /// <summary>
        /// Send a message to some clients
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClients(NetworkMessage message, List<ConnectedClient> clients)
        {
            lock (clients)
            {
                foreach (ConnectedClient client in clients)
                    client?.AddTCPMessage(message);
            }
        }

        /// <summary>
        /// Send a message to some clients
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClients(NetworkMessage message, IEnumerable<uint> clients)
        {
            lock (clients)
            {
                foreach (uint clientID in clients)
                    if (Clients.ContainsKey(clientID))
                        Clients[clientID].AddTCPMessage(message);
            }
        }

        /// <summary>
        /// Send a message to some clients
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClients(byte[] message, IEnumerable<uint> clients)
        {
            lock (clients)
            {
                foreach (uint clientID in clients)
                    if (Clients.ContainsKey(clientID))
                        Clients[clientID].AddTCPMessage(message);
            }
        }

        /// <summary>
        /// Broadcast a message to all clients
        /// </summary>
        /// <param name="message"> The message </param>
        public void Broadcast(NetworkMessage message)
        {
            lock (Clients)
            {
                foreach (var pair in Clients)
                    if (Clients.ContainsKey(pair.Key))
                        Clients[pair.Key].AddTCPMessage(message);
            }
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClientsUDP(NetworkMessage message, IEnumerable<uint> clients)
        {
            lock (clients)
            {
                foreach (uint clientID in clients)
                    if (Clients.ContainsKey(clientID))
                        SendToClientUDP(message, Clients[clientID]);
            }
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="client"> The client </param>
        public void SendToClientUDP(NetworkMessage message, ConnectedClient client)
        {
            message.Client = client;
            client.AddUDPMessage(message);
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="headID"> The head ID </param>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClientUDP(ushort headID, byte[] message, uint clientID)
        {
            if (Clients.ContainsKey(clientID))
                GetClient(clientID).AddUDPMessage(headID, message);
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClientUDP(NetworkMessage message, uint clientID)
        {
            message.Client = GetClient(clientID);
            if (message.Client != null)
                message.Client.AddUDPMessage(message);
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="message"> The message </param>
        /// <param name="clientID"> The client ID </param>
        public void SendToClientUDP(NetworkMessage message, UInt24 clientID)
        {
            SendToClientUDP(message, clientID.UInt32);
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="headID"> The head ID </param>
        /// <param name="message"> The message </param>
        /// <param name="clients"> The clients </param>
        public void SendToClientsUDP(ushort headID, byte[] message, IEnumerable<uint> clients)
        {
            lock (clients)
            {
                foreach (uint clientID in clients)
                    if (Clients.ContainsKey(clientID))
                        SendToClientUDP(headID, message, Clients[clientID]);
            }
        }

        /// <summary>
        /// Send a message to some clients using UDP protocol
        /// </summary>
        /// <param name="headID"> The head ID </param>
        /// <param name="message"> The message </param>
        /// <param name="client"> The client </param>
        public void SendToClientUDP(ushort headID, byte[] message, ConnectedClient client)
        {
            client.AddUDPMessage(headID, message);
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
                Worlds.ClientDisconnected(client.ID);
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
                client.OnMessageReceived -= MessageReceived;
                client.OnMessageSend -= MessageSended;
                // try clean disconnect if not already
                try { client.TcpSocket.Disconnect(false); } catch { }
                Writer.Write("Client " + client.ID + " disconnected", ConsoleColor.Green);
                //Writer.Write(Environment.StackTrace, ConsoleColor.Gray);
            }
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
        /// Event when a message is received
        /// </summary>
        /// <param name="message"> The message </param>
        internal void MessageReceived(NetworkMessage message)
        {
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
            client.ID = GetNewClientID();
            while (!Clients.TryAdd(client.ID, client))
                Thread.Sleep(1);
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