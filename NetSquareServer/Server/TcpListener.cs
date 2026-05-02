using NetSquare.Core;
using NetSquare.Server.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

#region Source
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
        public bool Started { get; private set; }
        /// <summary>
        /// Gets or sets the verifying clients value.
        /// </summary>
        public int VerifyingClients { get; private set; }
        /// <summary>
        /// Gets or sets the check black list value.
        /// </summary>
        public bool CheckBlackList { get; private set; }
        /// <summary>
        /// Stores the listen backlog value.
        /// </summary>
        public static int ListenBacklog = 1024;
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
            Started = true;
            server = _server;
            IPAddress = ipAddress;
            Port = port;
            _listener = new TcpListenerEx(ipAddress, port);
            _listener.Start(ListenBacklog);
            ThreadPool.QueueUserWorkItem((sender) => { HandleConnection(); });
            ThreadPool.QueueUserWorkItem((sender) => { HandleDisconnection(); });
        }

        /// <summary>
        /// Stop the listener
        /// </summary>
        public void Stop()
        {
            Started = false;
            try { _listener.Stop(); } catch { }
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
                    ThreadPool.QueueUserWorkItem((sender) => { AcceptConnection(newClient); });
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
        /// Accept a new connection
        /// </summary>
        /// <param name="sender"> The sender </param>
        private void AcceptConnection(object sender)
        {
            Socket newClient = (Socket)sender;
            if (CheckBlackList && BlackListManager.IsBlackListed(newClient))
                newClient.Close();
            else
                ValidateClient(newClient);
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
                        if (server.Clients.TryGetValue(clientID, out client) && !client.IsConnected())
                            server.Server_ClientDisconnected(client);
                    }
                    catch (Exception ex)
                    {
                        Writer.Write("Fail to disconnect client " + clientID + "  : " + ex.ToString(), ConsoleColor.Red);
                    }
                }

                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Validate a new client that want to connect
        /// </summary>
        /// <param name="client"> The client </param>
        private void ValidateClient(Socket client)
        {
            try
            {
                long timeEnd = DateTime.Now.AddSeconds(30).Ticks;
                VerifyingClients++;
                // send handShake
                int rnd1 = 0;
                int rnd2 = 0;
                int key = 0;
                byte[] handShake = HandShake.GetRandomHandShake(out rnd1, out rnd2, out key);
                client.Send(handShake, 0, handShake.Length, SocketFlags.None);
                bool isClientOK = false;
                //Writer.Write("HandShake client " + rnd1 + " " + rnd2 + " " + key, ConsoleColor.Cyan);

                // wait for client renspond correct hash
                while (client.Connected && DateTime.Now.Ticks < timeEnd)
                {
                    // client disconnect
                    if (!client.Connected)
                    {
                        Writer.Write("Client disconected before end handshake. Close his connection", ConsoleColor.Red);
                        break;
                    }

                    // get awnser
                    if (client.Available >= 4)
                    {
                        byte[] array = new byte[4];
                        client.Receive(array, 0, 4, SocketFlags.None);
                        int clientKey = BitConverter.ToInt32(array, 0);
                        if (clientKey == key)
                        {
                            isClientOK = true;
                        }
                        else
                            Writer.Write("Client awnser wrong handshake key.", ConsoleColor.Red);
                        break;
                    }

                    Thread.Sleep(1);
                }

                // client awnser good
                if (isClientOK)
                {
                    //Writer.Write("Client awnser good handshake key. Accept it.", ConsoleColor.Green);

                    ConnectedClient cClient = new ConnectedClient();
                    cClient.SetClient(client, false, server.ProtocoleType == NetSquareProtocoleType.TCP_AND_UDP);
                    uint clientID = server.AddClient(cClient);

                    // client disconnect
                    if (!cClient.IsConnected())
                    {
                        Writer.Write("Client disconected before end of intialization. Close his connection", ConsoleColor.Red);
                        VerifyingClients--;
                        return;
                    }

                    VerifyingClients--;
                    client.Send(new UInt24(clientID).GetBytes(), 0, 3, SocketFlags.None);
                    server.Server_ClientConnected(server.GetClient(clientID), clientID);
                }
                // no awnser, awnser error, disconnected or timeout
                else
                {
                    VerifyingClients--;
                    client.Close();
                    client.Dispose();
                }
            }
            catch (Exception ex)
            {
                Writer.Write("Fail to HandShake client : " + ex.ToString(), ConsoleColor.Red);
            }
        }
    }
}
#endregion
