using NetSquare.Core;
using NetSquareServer.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareServer
{
    public class ServerListener
    {
        private byte nbThreads = 1;
        private TcpListenerEx _listener = null;
        public ConcurrentDictionary<byte, List<TcpClient>> ConnectedClients = new ConcurrentDictionary<byte, List<TcpClient>>();
        private ConcurrentDictionary<byte, List<TcpClient>> verifiedClients = new ConcurrentDictionary<byte, List<TcpClient>>();
        private List<TcpClient> _verifyingClients = new List<TcpClient>();
        private List<TcpClient> _disconnectedClients = new List<TcpClient>();
        private TcpServer _parent = null;
        internal bool QueueStop { get; set; }
        internal IPAddress IPAddress { get; private set; }
        internal int Port { get; private set; }
        internal int ReadLoopIntervalMs { get; set; }
        internal TcpListenerEx Listener { get { return _listener; } }
        public int ConnectedClientsCount
        {
            get { return ConnectedClients.Values.Sum(c => c.Count()); }
        }
        public int VerifyingClientsCount
        {
            get { return _verifyingClients.Count; }
        }

        internal ServerListener(TcpServer parentServer, IPAddress ipAddress, int port, byte _nbThreads)
        {
            QueueStop = false;
            _parent = parentServer;
            IPAddress = ipAddress;
            Port = port;
            nbThreads = _nbThreads;
            ReadLoopIntervalMs = 10;
            _listener = new TcpListenerEx(ipAddress, port);
            _listener.Start();
            StartThreads();
            ThreadPool.QueueUserWorkItem((sender) => { ConnectionDisconnectionLoop(); });
        }

        private void StartThreads()
        {
            ConnectedClients = new ConcurrentDictionary<byte, List<TcpClient>>();
            verifiedClients = new ConcurrentDictionary<byte, List<TcpClient>>();
            for (byte i = 0; i < nbThreads; i++)
            {
                byte threadID = i;
                while (!ConnectedClients.TryAdd(threadID, new List<TcpClient>()))
                    Thread.Sleep(1);
                while (!verifiedClients.TryAdd(threadID, new List<TcpClient>()))
                    Thread.Sleep(1);

                Thread _rxThread = new Thread(() =>
                {
                    ListenerLoop(threadID);
                });
                _rxThread.IsBackground = true;
                _rxThread.Start();
            }
        }

        private void ListenerLoop(byte threadID)
        {
            while (!QueueStop)
            {
                try { RunLoopStep(threadID); }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write(ex.ToString());
                    Console.ForegroundColor = ConsoleColor.White;
                }
                Thread.Sleep(1);
            }
            _listener.Stop();
        }

        bool IsSocketConnected(Socket s)
        {
            // https://stackoverflow.com/questions/2661764/how-to-check-if-a-socket-is-connected-disconnected-in-c
            bool part1 = s.Poll(1000, SelectMode.SelectRead);
            bool part2 = (s.Available == 0);
            if ((part1 && part2) || !s.Connected)
                return false;
            else
                return true;
        }

        private void ConnectionDisconnectionLoop()
        {
            while (!QueueStop)
            {
                // HANDLE Disconnection
                lock (_disconnectedClients)
                {
                    if (_disconnectedClients.Count > 0)
                    {
                        // check if client is in this thread
                        var disconnectedClients = _disconnectedClients.ToArray();
                        _disconnectedClients.Clear();
                        foreach (var disC in disconnectedClients)
                        {
                            foreach (var clients in ConnectedClients)
                            {
                                if (clients.Value.Contains(disC))
                                {
                                    clients.Value.Remove(disC);
                                    _parent.Fire_ClientDisconnected(disC);
                                }
                            }
                        }
                        _parent.UpdateTitle();
                    }
                }

                // Handle conenctions
                if (_listener.Pending())
                {
                    Thread t = new Thread(() =>
                    {
                        var newClient = _listener.AcceptTcpClient();
                        _parent.UpdateTitle();
                        if (BlackListManager.IsBlackListed(newClient))
                            newClient.Close();
                        else
                        {
                            lock (_verifyingClients)
                            {
                                _verifyingClients.Add(newClient);
                            }
                            ValidateClient(newClient);
                        }
                    });
                    t.Start();
                }

                Thread.Sleep(1);
            }
        }

        private void RunLoopStep(byte threadID)
        {
            // Handle Add Clients
            List<TcpClient> verified;
            while (!verifiedClients.TryGetValue(threadID, out verified))
                Thread.Sleep(1);
            for(int i = 0; i < verified.Count; i++)
            {
                List<TcpClient> connected;
                while (!ConnectedClients.TryGetValue(threadID, out connected))
                    Thread.Sleep(1);
                connected.Add(verified[i]);
            }
            verified.Clear();


            List<TcpClient> clients;
            while (!ConnectedClients.TryGetValue(threadID, out clients))
                Thread.Sleep(1);

            // Handle clients packets
            int currentLenght = -1;
            byte[] bytesReceived = new byte[0];
            for(int clientIndex = 0; clientIndex < clients.Count; clientIndex++)
            {
                TcpClient c = clients[clientIndex];
                if (!IsSocketConnected(c.Client))
                {
                    lock (_disconnectedClients)
                    {
                        _disconnectedClients.Add(c);
                    }
                    continue;
                }

                // get size
                if (currentLenght == -1 && c.Available > 4)
                {
                    byte[] nextByte = new byte[4];
                    c.Client.Receive(nextByte, 0, 4, SocketFlags.None);
                    currentLenght = BitConverter.ToInt32(nextByte, 0);
                    bytesReceived = new byte[currentLenght];
                }

                int i = 0;
                while (c.Available > 0 && c.Connected && currentLenght != -1)
                {
                    int nbBytesToReceive = c.Available;
                    if (nbBytesToReceive > 255)
                        nbBytesToReceive = 255;
                    if (nbBytesToReceive > currentLenght)
                        nbBytesToReceive = currentLenght;
                    c.Client.Receive(bytesReceived, i, nbBytesToReceive, SocketFlags.None);
                    i += nbBytesToReceive;

                    // all message recieved
                    if (i == currentLenght)
                    {
                        currentLenght = -1;
                        _parent.Fire_DataReceived(c, bytesReceived);
                    }
                }
            }
        }

        private void ValidateClient(TcpClient client)
        {
            Thread t = new Thread(() =>
            {
                long timeEnd = DateTime.Now.AddSeconds(30).Ticks;

                // send handShake
                int rnd1 = 0;
                int rnd2 = 0;
                int key = 0;
                byte[] handShake = HandShake.GetRandomHandShake(out rnd1, out rnd2, out key);
                client.GetStream().Write(handShake, 0, handShake.Length);
                bool isClientOK = false;
                Writer.Write("HandShake client " + rnd1 + " " + rnd2 + " " + key, ConsoleColor.Cyan);

                // wait for client renspond correct hash
                while (client.Connected && DateTime.Now.Ticks < timeEnd)
                {
                    // client disconnect
                    if (!IsSocketConnected(client.Client))
                    {
                        Writer.Write("Client disconected before end handshake. Close his connection", ConsoleColor.Red);
                        break;
                    }

                    // get awnser
                    if (client.Available >= 4)
                    {
                        byte[] array = new byte[4];
                        client.Client.Receive(array, 0, 4, SocketFlags.None);
                        int clientKey = BitConverter.ToInt32(array, 0);
                        if (clientKey == key)
                            isClientOK = true;
                        else
                            Writer.Write("Client awnser wrong handshake key.", ConsoleColor.Red);
                        break;
                    }
                }

                // client awnser good
                if (isClientOK)
                {
                    Writer.Write("Client awnser good handshake key. Accept it.", ConsoleColor.Green);

                    lock (_verifyingClients)
                    {
                        uint clientID = NetSquare_Server.Instance.AddClient(new ConnectedClient() { ServerIP = IPAddress, TCPClient = client });

                        // client disconnect
                        if (!IsSocketConnected(client.Client))
                        {
                            Writer.Write("Client disconected before end of intialization. Close his connection", ConsoleColor.Red);
                            _verifyingClients.Remove(client);
                            return;
                        }
                        client.GetStream().Write(BitConverter.GetBytes(clientID), 0, 4);

                        _verifyingClients.Remove(client);
                        DispatchClientToThread(client);
                        _parent.Fire_ClientConnected(client, clientID);
                    }
                }
                // no awnser, awnser error, disconnected or timeout
                else
                {
                    _verifyingClients.Remove(client);
                    client.Close();
                    client.Dispose();
                }
            });
            t.Start();
        }

        void DispatchClientToThread(TcpClient client)
        {
            lock (ConnectedClients)
            {
                byte threadID = 0;
                int minClients = int.MaxValue;
                for (byte i = 0; i < nbThreads; i++)
                {
                    if (ConnectedClients[i].Count < minClients)
                    {
                        minClients = ConnectedClients[i].Count;
                        threadID = i;
                    }
                }

                List<TcpClient> verified;
                while (!verifiedClients.TryGetValue(threadID, out verified))
                    Thread.Sleep(1);
                verified.Add(client);
            }
        }
    }
}