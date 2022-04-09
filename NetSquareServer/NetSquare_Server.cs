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

        public static NetSquare_Server Instance;
        public uint ClientIDCounter { get; private set; }
        public event Action BeforeLoadConfiguration_StepOne;
        public event Action AfterLoadConfiguration_StepTwo;
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<NetworkMessage> OnMessageReceived;
        #region Variables
        private byte nbQueues = 1;
        public ConcurrentDictionary<byte, ConcurrentQueue<NetworkMessage>> MessagesQueues = new ConcurrentDictionary<byte, ConcurrentQueue<NetworkMessage>>();
        private TcpServer server;
        public NetSquareDispatcher Dispatcher;
        public ConcurrentDictionary<uint, ConnectedClient> Clients = new ConcurrentDictionary<uint, ConnectedClient>(); // ID Client => ConnectedClient
        public ConcurrentDictionary<TcpClient, uint> ClientsID = new ConcurrentDictionary<TcpClient, uint>(); // TcpClient => ID Client
        #endregion

        public NetSquare_Server()
        {
            Dispatcher = new NetSquareDispatcher();
            Instance = this;
        }

        public void Start()
        {
            Start(-1);
        }

        public void Start(int port)
        {
            if (Debugger.IsAttached)
                Routine(port);
            else
            {
                Loop:
                try
                {
                    Routine(port);
                }
                catch (Exception ex)
                {
                    Writer.Write(ex.Message + Environment.NewLine + Environment.NewLine + ex.StackTrace, ConsoleColor.Red);
                    goto Loop;
                }
            }
        }

        private void Routine(int port)
        {
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

                Initialize();
                if (StartTCPServer(port > 0 ? port : NetSquareConfigurationManager.Configuration.Port))
                {
                    Writer.Write_Server("Processing Message Queue...", ConsoleColor.DarkYellow, false);
                    Writer.Write("Started", ConsoleColor.Green);
                    StartProcessMessagesQueue(NetSquareConfigurationManager.Configuration.ProcessOffsetTime, NetSquareConfigurationManager.Configuration.NbQueueThreads);
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

        private void DrawHeader(string version)
        {
            Writer.Title("NetSquare Server " + version);
            Writer.Write("\n");
            Writer.Write(@"   _   _      _   _____  ", ConsoleColor.Cyan);
            Writer.Write(@"  | \ | |    | | /  ___| ", ConsoleColor.Magenta);
            Writer.Write(@"  |  \| | ___| |_\ `--.  __ _ _   _  __ _ _ __ ___ ", ConsoleColor.Cyan);
            Writer.Write(@"  | . ` |/ _ \ __|`--. \/ _` | | | |/ _` | '__/ _ \", ConsoleColor.Magenta);
            Writer.Write(@"  | |\  |  __/ |_/\__/ / (_| | |_| | (_| | | |  __/", ConsoleColor.Cyan);
            Writer.Write(@"  \_| \_/\___|\__\____/ \__, |\__,_|\__,_|_|  \___|", ConsoleColor.Magenta);
            Writer.Write(@"                           | |                    ", ConsoleColor.Cyan);
            Writer.Write(@"                           |_|                    ", ConsoleColor.Magenta);
            Writer.Write(@"                          by ", ConsoleColor.Cyan, false);
            Writer.Write(@"Keks                                     ", ConsoleColor.Magenta, false);
            Writer.Write(version + "\n\n", ConsoleColor.Cyan);
        }

        #region Start and Init TCP Server
        private void Initialize()
        {
            Writer.Write_Server("Loading Network Methods...", ConsoleColor.DarkYellow, false);
            if (Dispatcher == null)
                Dispatcher = new NetSquareDispatcher();
            Dispatcher.AutoBindHeadActionsFromAttributes();
            Writer.Write(Dispatcher.Count.ToString(), ConsoleColor.Green);
        }

        private bool StartTCPServer(int port)
        {
            Writer.Write_Server("Starting server on port " + port.ToString() + "...", ConsoleColor.DarkYellow);
            server = new TcpServer();
            server.Start(port, NetSquareConfigurationManager.Configuration.NbReceivingThreads);
            if (server.IsStarted)
            {
                Writer.Write_Server("started Success (" + server.Listeners.Count + " IP)", ConsoleColor.Green);
                server.ClientConnected += Server_ClientConnected;
                server.ClientDisconnected += Server_ClientDisconnected;
                server.DataReceived += Server_DataReceived;
                return true;
            }
            else
            {
                Writer.Write("FAIL", ConsoleColor.Red);
                return false;
            }
        }
        #endregion

        private void StartProcessMessagesQueue(int delay, byte nbThreads)
        {
            if (nbThreads < 1)
                nbThreads = 1;
            if (nbThreads > 255)
                nbThreads = 255;
            nbQueues = nbThreads;
            for (byte i = 0; i < nbQueues; i++)
            {
                byte threadID = i;

                Thread t = new Thread(() =>
                {
                    while (!MessagesQueues.TryAdd(threadID, new ConcurrentQueue<NetworkMessage>()))
                        Thread.Sleep(1);
                    while (true)
                    {
                        while (MessagesQueues[threadID].Count > 0)
                        {
                            server.UpdateTitle();
                            NetworkMessage currentMessage = null;
                            if (!MessagesQueues[threadID].TryDequeue(out currentMessage))
                            {
                                Thread.Sleep(1);
                                continue;
                            }
                            if (currentMessage == null)
                            {
                                Writer.Write("NullQueuedMessageException", ConsoleColor.Red);
                                continue;
                            }
                            Writer.Write(currentMessage.Head + " => [" + currentMessage.ClientID + "] " + currentMessage.Data.Length + " bytes ", ConsoleColor.DarkGray);

                            if (!Dispatcher.DispatchMessage(currentMessage))
                            {
                                Writer.Write("Trying to Process message with head '" + currentMessage.Head.ToString() + "' but no action related... Message skipped.", ConsoleColor.DarkMagenta);
                                Reply(currentMessage, new NetworkMessage(0, currentMessage.ClientID).Set(false));
                            }
                            currentMessage = null;
                        }
                        Thread.Sleep(delay);
                    }
                });
                t.Start();
            }
        }

        #region Sending and Rep
        public void Reply(NetworkMessage currentMessage, NetworkMessage msg)
        {
            msg.Head = currentMessage.Head;
            msg.ReplyTo(currentMessage.ReplyID);
            server.Reply(msg.Head, msg.Serialize(), currentMessage.TcpClient);
        }

        public void SendToClient(NetworkMessage msg, TcpClient Client)
        {
            server.SendToClient(msg.Head, msg.Serialize(), Client);
        }

        public void SendToClient(NetworkMessage msg, uint ClientID)
        {
            server.SendToClient(msg.Head, msg.Serialize(), Clients[ClientID].TCPClient);
        }

        public void SendToClients(NetworkMessage msg, List<TcpClient> Client)
        {
            server.SendToClients(msg.Head, msg.Serialize(), Client);
        }

        public void Broadcast(NetworkMessage msg)
        {
            server.Broadcast(msg.Head, msg.Serialize());
        }
        #endregion

        #region ServerEvent
        private void Server_ClientDisconnected(TcpClient e)
        {
            if (ClientsID.ContainsKey(e))
            {
                uint clientID = 0;
                while (!ClientsID.TryGetValue(e, out clientID))
                    Thread.Sleep(1);
                OnClientDisconnected?.Invoke(clientID);
                // supprime des clients connectés
                lock (Clients)
                {
                    ConnectedClient client = null;
                    while (!Clients.TryRemove(clientID, out client))
                        Thread.Sleep(1);
                }
                Writer.Write("Client disconnected Good!", ConsoleColor.Green);
            }
            else
                Writer.Write("Not connected client leave server.", ConsoleColor.Yellow);
        }

        private void Server_ClientConnected(TcpClient e, uint id)
        {
            Writer.Write("New client connected !", ConsoleColor.Green);
            OnClientConnected?.Invoke(id);
        }

        byte currentDispatchID = 0;
        private void Server_DataReceived(NetworkMessage msg)
        {
            if (msg == null)
                Writer.Write("le message est null.", ConsoleColor.Red);
            else
            {
                currentDispatchID++;
                currentDispatchID %= nbQueues;
                MessagesQueues[currentDispatchID].Enqueue(msg);
                OnMessageReceived?.Invoke(msg);
            }
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
            lock (Clients)
            {
                client.ID = id;
                while (!Clients.TryAdd(client.ID, client))
                    Thread.Sleep(1);
                while (!ClientsID.TryAdd(client.TCPClient, client.ID))
                    Thread.Sleep(1);
            }
            return id;
        }

        public int GetNbVerifyingClients()
        {
            int nb = 0;
            foreach (var listner in server.Listeners)
                nb += listner.VerifyingClientsCount;
            return nb;
        }
        #endregion
    }
}