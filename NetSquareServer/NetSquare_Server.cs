using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using NetSquareServer.Utils;
using System.Collections.Generic;
using System.Net.Sockets;
using NetSquare.Core;
using System.Collections.Concurrent;
using System.Threading;
using NetSquareServer.Server;
using System.Net;
using System.Net.NetworkInformation;
using System.Linq;
using NetSquareServer.Lobbies;

namespace NetSquareServer
{
    public class NetSquare_Server
    {
        [DllImport("kernel32.dll")]
        static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        [DllImport("kernel32.dll")]
        static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int nStdHandle);

        public uint ClientIDCounter { get; private set; }
        public event Action BeforeLoadConfiguration_StepOne;
        public event Action AfterLoadConfiguration_StepTwo;
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        #region Variables
        public bool IsStarted { get { return Listeners.Any(l => l.Listener.Active); } }
        public List<TcpConnector> Listeners = new List<TcpConnector>();
        public NetSquareDispatcher Dispatcher;
        internal MessageQueueManager MessageQueueManager;
        internal MessageSenderManager MessageSenderManager;
        internal MessageReceiverManager MessageReceiverManager;
        internal Synchronizer Synchronizer;
        public WorldsManager Worlds;
        public ServerStatisticsManager Statistics;
        public ConcurrentDictionary<uint, ConnectedClient> Clients = new ConcurrentDictionary<uint, ConnectedClient>(); // ID Client => ConnectedClient
        #endregion

        public NetSquare_Server()
        {
            Dispatcher = new NetSquareDispatcher();
            Worlds = new WorldsManager(this);
            MessageQueueManager = new MessageQueueManager(this, NetSquareConfigurationManager.Configuration.NbQueueThreads);
            MessageSenderManager = new MessageSenderManager(NetSquareConfigurationManager.Configuration.NbSendingThreads);
            MessageReceiverManager = new MessageReceiverManager(this, NetSquareConfigurationManager.Configuration.NbReceivingThreads, NetSquareConfigurationManager.Configuration.ReceivingBufferSize);
            Synchronizer = new Synchronizer(this);
            Statistics = new ServerStatisticsManager();
        }

        private void ServerRoutine(int port, bool allowLocalIP)
        {
            Writer.StartDisplayLog();
            // Start by drawing header
            DrawHeader("v" + Assembly.GetAssembly(typeof(NetSquare_Server)).GetName().Version);
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
                        UInt32 consoleMode;
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

                Writer.Write_Server("Loading Network Methods...", ConsoleColor.DarkYellow, false);
                if (Dispatcher == null)
                    Dispatcher = new NetSquareDispatcher();
                Dispatcher.AutoBindHeadActionsFromAttributes();
                Writer.Write(Dispatcher.Count.ToString(), ConsoleColor.Green);

                if (StartTcpServer(port > 0 ? port : NetSquareConfigurationManager.Configuration.Port, allowLocalIP))
                {
                    Writer.Write_Server("Processing Message Queue...", ConsoleColor.DarkYellow, false);
                    MessageQueueManager.StartQueues();
                    MessageSenderManager.StartSenders();
                    MessageReceiverManager.StartReceivers();
                    Synchronizer.StartSynchronizing(NetSquareConfigurationManager.Configuration.SynchronizingFrequency);
                    Statistics.StartReceivingStatistics(this, 100);
                    Writer.Write("Started", ConsoleColor.Green);
                }
                else
                {
                    Writer.Write("ERROR : Can't Start Server...", ConsoleColor.Red);
                }
            }
            else
            {
                NetSquareConfiguration Configuration = new NetSquareConfiguration();
                NetSquareConfigurationManager.SaveConfiguration(Configuration);
                Writer.Write("No configuration file finded.", ConsoleColor.Red);
                Writer.Write("A new configuration file have been created. Please restart the server.", ConsoleColor.DarkYellow);
            }
        }

        #region Start/Stop Server
        public void Start(int port = -1, bool allowLocalIP = true)
        {
            if (Debugger.IsAttached)
                ServerRoutine(port, allowLocalIP);
            else
            {
                Loop:
                try
                {
                    ServerRoutine(port, allowLocalIP);
                }
                catch (Exception ex)
                {
                    Writer.Write(ex.Message + Environment.NewLine + Environment.NewLine + ex.StackTrace, ConsoleColor.Red);
                    goto Loop;
                }
            }
        }

        private bool StartTcpServer(int port, bool allowLocalIP)
        {
            Writer.Write_Server("Starting server on port " + port.ToString() + "...", ConsoleColor.DarkYellow);
            var ipSorted = GetIPAddresses();
            bool anyNicFailed = false;
            foreach (var ipAddr in ipSorted)
            {
                try
                {
                    if ((ipAddr.ToString() != "127.0.0.1" || allowLocalIP) && ipAddr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        startListener(ipAddr, port);
                        Writer.Write_Server("  - " + ipAddr.ToString(), ConsoleColor.Yellow);
                    }
                    else
                        Writer.Write_Server("  - Switch IP : " + ipAddr.ToString(), ConsoleColor.DarkGray);
                }
                catch (SocketException ex)
                {
                    Writer.Write(ex.ToString(), ConsoleColor.Red);
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
                Writer.Write_Server("started Success (" + Listeners.Count + " IP)", ConsoleColor.Green);
                return true;
            }
            else
            {
                Writer.Write("FAIL", ConsoleColor.Red);
                return false;
            }
        }

        private void startListener(IPAddress ipAddress, int port)
        {
            TcpConnector listener = new TcpConnector(this, ipAddress, port);
            Listeners.Add(listener);
        }

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
        public void Reply(NetworkMessage messageFrom, NetworkMessage message)
        {
            message.HeadID = messageFrom.HeadID;
            message.SetType(messageFrom.TypeID);
            MessageSenderManager.SendMessage(message.Serialize(), messageFrom.Client);
        }

        public void SendToClient(NetworkMessage message, ConnectedClient client)
        {
            MessageSenderManager.SendMessage(message.Serialize(), client);
        }

        public void SendToClient(NetworkMessage message, uint clientID)
        {
            MessageSenderManager.SendMessage(message.Serialize(), Clients[clientID]);
        }

        public void SendToClients(NetworkMessage message, List<ConnectedClient> clients)
        {
            MessageSenderManager.SendMessage(message.Serialize(), clients);
        }

        public void SendToClients(NetworkMessage message, IEnumerable<uint> clients)
        {
            MessageSenderManager.SendMessage(message.Serialize(), GetTcpClientsFromIDs(clients));
        }

        public void Broadcast(NetworkMessage message)
        {
            MessageSenderManager.SendMessage(message.Serialize(), GetAllClients());
        }

        public void SynchronizeMessage(NetworkMessage message)
        {
            Synchronizer.AddMessage(message);
        }
        #endregion

        #region ServerEvent
        public void Server_ClientDisconnected(ConnectedClient client)
        {
            OnClientDisconnected?.Invoke(client.ID);
            // supprime des clients connectés
            ConnectedClient c = null;
            while (!Clients.TryRemove(client.ID, out c))
                Thread.Sleep(1);
            MessageSenderManager.RemoveClient(client);
            Writer.Write("Client disconnected Good!", ConsoleColor.Green);
        }

        public void Server_ClientConnected(ConnectedClient client, uint id)
        {
            Writer.Write("New client connected !", ConsoleColor.Green);
            OnClientConnected?.Invoke(id);
        }

        public void MessageReceive(NetworkMessage message)
        {
            MessageQueueManager.MessageReceived(message);
        }
        #endregion

        #region Utils
        public bool IsClientConnected(uint clientID)
        {
            return Clients.ContainsKey(clientID);
        }

        public ConnectedClient GetClient(uint clientID)
        {
            return Clients[clientID];
        }

        public uint AddClient(ConnectedClient client)
        {
            uint id = ClientIDCounter++;
            client.ID = id;
            while (!Clients.TryAdd(client.ID, client))
                Thread.Sleep(1);
            MessageSenderManager.AddClient(client);
            return id;
        }

        public int GetNbVerifyingClients()
        {
            int nb = 0;
            foreach (var listner in Listeners)
                nb += listner.VerifyingClients;
            return nb;
        }

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

        public List<ConnectedClient> GetAllClients()
        {
            List<ConnectedClient> clients = new List<ConnectedClient>();
            foreach (var client in Clients)
                clients.Add(client.Value);
            return clients;
        }

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
        private void DrawHeader(string version)
        {
            Writer.Title("NetSquare Server " + version);
            //Writer.Write("\n");
            //Writer.Write(@"   _   _      _   _____  ", ConsoleColor.Cyan);
            //Writer.Write(@"  | \ | |    | | /  ___| ", ConsoleColor.Magenta);
            //Writer.Write(@"  |  \| | ___| |_\ `--.  __ _ _   _  __ _ _ __ ___ ", ConsoleColor.Cyan);
            //Writer.Write(@"  | . ` |/ _ \ __|`--. \/ _` | | | |/ _` | '__/ _ \", ConsoleColor.Magenta);
            //Writer.Write(@"  | |\  |  __/ |_/\__/ / (_| | |_| | (_| | | |  __/", ConsoleColor.Cyan);
            //Writer.Write(@"  \_| \_/\___|\__\____/ \__, |\__,_|\__,_|_|  \___|", ConsoleColor.Magenta);
            //Writer.Write(@"                           | |                    ", ConsoleColor.Cyan);
            //Writer.Write(@"                           |_|                    ", ConsoleColor.Magenta);
            //Writer.Write(@"                          by ", ConsoleColor.Cyan, false);
            //Writer.Write(@"Keks                                     ", ConsoleColor.Magenta, false);
            //Writer.Write(version + "\n\n", ConsoleColor.Cyan);


            Writer.Write(@"   _   _      _  ", ConsoleColor.White, false);Writer.Write(@" _____  ", ConsoleColor.Red);
            Writer.Write(@"  | \ | |    | | ", ConsoleColor.White, false);Writer.Write(@"/  ___| ", ConsoleColor.Red);
            Writer.Write(@"  |  \| | ___| |_", ConsoleColor.White, false);Writer.Write(@"\ `--.  __ _ _   _  __ _ _ __ ___ ", ConsoleColor.Red);
            Writer.Write(@"  | . ` |/ _ \ __|", ConsoleColor.White, false);Writer.Write(@"`--. \/ _` | | | |/ _` | '__/ _ \", ConsoleColor.Red);
            Writer.Write(@"  | |\  |  __/ |_", ConsoleColor.White, false);Writer.Write(@"/\__/ / (_| | |_| | (_| | | |  __/", ConsoleColor.Red);
            Writer.Write(@"  \_| \_/\___|\__", ConsoleColor.White, false);Writer.Write(@"\____/ \__, |\__,_|\__,_|_|  \___|", ConsoleColor.Red);
            Writer.Write(@"                 ", ConsoleColor.White, false);Writer.Write(@"          | |                    ", ConsoleColor.Red);
            Writer.Write(@"                 ", ConsoleColor.White, false);Writer.Write(@"          |_|                    ", ConsoleColor.Red);
            Writer.Write(@"                          by ", ConsoleColor.White, false);
            Writer.Write(@"Keks                                     ", ConsoleColor.Red, false);
            Writer.Write(version + "\n\n", ConsoleColor.White);
        }

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