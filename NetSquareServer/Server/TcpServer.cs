using NetSquare.Core;
using NetSquareServer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareServer
{
    public class TcpServer
    {
        public TcpServer() { }
        public List<ServerListener> Listeners = new List<ServerListener>();
        public event Action<TcpClient, uint> ClientConnected;
        public event Action<TcpClient> ClientDisconnected;
        public event Action<NetworkMessage> DataReceived;

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

        public void UpdateTitle()
        {
            if (!Writer.DisplayTitle)
                return;
            string title = "NetSquare Server - ";
            int nbConnected = 0;
            int nbVerifying = 0;
            foreach (var listner in Listeners)
            {
                nbConnected += listner.ConnectedClientsCount;
                nbVerifying += listner.VerifyingClientsCount;
            }
            int nbMessages = 0;
            foreach (var queue in NetSquare_Server.Instance.MessagesQueues)
                nbMessages += queue.Value.Count;
            title += "Listner : " + Listeners.Count + " - Connected : " + nbConnected + " - Verrigying : " + nbVerifying + " - nbMessages : " + nbMessages;
            Writer.Title(title);
        }

        #region Sending
        public void Broadcast(ushort head, byte[] msg)
        {
            foreach (var listener in Listeners)
                foreach (var pair in listener.ConnectedClients)
                    foreach (var client in pair.Value)
                    {
                        if (client == null)
                            continue;
                        client.GetStream().Write(msg, 0, msg.Length);
                    }
            Writer.Write(head + " <<<= Message Broadcasted. " + msg.Length + " bytes ", ConsoleColor.Magenta);
        }

        public void Reply(ushort head, byte[] msg, TcpClient _tcpClient)
        {
            if (_tcpClient == null)
                return;
            _tcpClient.GetStream().Write(msg, 0, msg.Length);
            Writer.Write(head + " <=> Reply " + msg.Length + " bytes ", ConsoleColor.DarkYellow);
        }

        public void SendToClient(ushort head, byte[] msg, TcpClient _tcpClient)
        {
            if (_tcpClient == null)
                return;
            _tcpClient.GetStream().Write(msg, 0, msg.Length);
            Writer.Write(head + " <= Message Sended. " + msg.Length + " bytes", ConsoleColor.DarkCyan);
        }

        public void SendToClients(ushort head, byte[] msg, List<TcpClient> _tcpClient)
        {
            if (_tcpClient.Count > 0)
            {
                foreach (var client in _tcpClient)
                {
                    if (client == null)
                        continue;
                    client.GetStream().Write(msg, 0, msg.Length);
                }
                Writer.Write(head + " <<= Message Sended on map.  [" + _tcpClient.Count + "] - " + msg.Length + " bytes", ConsoleColor.Cyan);
            }
        }
        #endregion

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

        public TcpServer Start(int port, byte nbThreads, bool ignoreNicsWithOccupiedPorts = true)
        {
            var ipSorted = GetIPAddresses();
            bool anyNicFailed = false;
            foreach (var ipAddr in ipSorted)
            {
                try
                {
                    if (ipAddr.ToString() != "127.0.0.1" && ipAddr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        Start(ipAddr, port, nbThreads);
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

            if (anyNicFailed && !ignoreNicsWithOccupiedPorts)
            {
                Stop();
                throw new InvalidOperationException("Port was already occupied for one or more network interfaces.");
            }

            return this;
        }

        public TcpServer Start(int port, AddressFamily addressFamilyFilter, byte nbThreads)
        {
            var ipSorted = GetIPAddresses().Where(ip => ip.AddressFamily == addressFamilyFilter);
            foreach (var ipAddr in ipSorted)
            {
                try
                {
                    Start(ipAddr, port, nbThreads);
                }
                catch { }
            }

            return this;
        }

        public bool IsStarted { get { return Listeners.Any(l => l.Listener.Active); } }

        public TcpServer Start(IPAddress ipAddress, int port, byte nbThreads)
        {
            ServerListener listener = new ServerListener(this, ipAddress, port, nbThreads);
            Listeners.Add(listener);
            return this;
        }

        public void Stop()
        {
            Listeners.All(l => l.QueueStop = true);
            while (Listeners.Any(l => l.Listener.Active))
            {
                Thread.Sleep(100);
            };
            Listeners.Clear();
        }

        public int ConnectedClientsCount
        {
            get
            {
                return Listeners.Sum(l => l.ConnectedClientsCount);
            }
        }

        #region Fire Events
        internal void Fire_DataReceived(TcpClient client, byte[] msg)
        {
            DataReceived?.Invoke(new NetworkMessage(msg, client));
        }

        internal void Fire_ClientConnected(TcpClient client, uint id)
        {
            ClientConnected?.Invoke(client, id);
        }

        internal void Fire_ClientDisconnected(TcpClient disconnectedClient)
        {
            ClientDisconnected?.Invoke(disconnectedClient);
        }
        #endregion
    }
}