using NetSquare.Core;
using NetSquare.Server.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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
            _ = Task.Run(HandleConnectionAsync);
            _ = Task.Run(HandleDisconnectionAsync);
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
        private async Task HandleConnectionAsync()
        {
            while (Started)
            {
                try
                {
                    Socket newClient = await _listener.AcceptSocketAsync().ConfigureAwait(false);
                    newClient.NoDelay = true;
                    _ = Task.Run(() => AcceptConnectionAsync(newClient));
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
        private async Task AcceptConnectionAsync(Socket newClient)
        {
            if (CheckBlackList && BlackListManager.IsBlackListed(newClient))
                newClient.Close();
            else
                await ValidateClientAsync(newClient).ConfigureAwait(false);
        }

        /// <summary>
        /// Loop to handle clients Disconnection
        /// </summary>
        private async Task HandleDisconnectionAsync()
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

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Validate a new client that want to connect
        /// </summary>
        /// <param name="client"> The client </param>
        private async Task ValidateClientAsync(Socket client)
        {
            try
            {
                VerifyingClients++;
                // send handShake
                int rnd1 = 0;
                int rnd2 = 0;
                int key = 0;
                byte[] handShake = HandShake.GetRandomHandShake(out rnd1, out rnd2, out key);
                await SendAllAsync(client, handShake, 0, handShake.Length).ConfigureAwait(false);
                bool isClientOK = false;
                //Writer.Write("HandShake client " + rnd1 + " " + rnd2 + " " + key, ConsoleColor.Cyan);

                // wait for client renspond correct hash
                byte[] array = new byte[4];
                if (await ReceiveExactAsync(client, array, 0, array.Length, 30000).ConfigureAwait(false))
                {
                    int clientKey = BitConverter.ToInt32(array, 0);
                    if (clientKey == key)
                    {
                        isClientOK = true;
                    }
                    else
                        Writer.Write("Client awnser wrong handshake key.", ConsoleColor.Red);
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
                    byte[] idBytes = new UInt24(clientID).GetBytes();
                    await SendAllAsync(client, idBytes, 0, idBytes.Length).ConfigureAwait(false);
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

        /// <summary>
        /// Receives the requested number of bytes or returns false on timeout/disconnect.
        /// </summary>
        private static async Task<bool> ReceiveExactAsync(Socket socket, byte[] buffer, int offset, int count, int timeoutMs)
        {
            int receivedTotal = 0;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (receivedTotal < count)
            {
                int remainingTimeout = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remainingTimeout <= 0)
                    return false;

                Task<int> receiveTask = socket.ReceiveAsync(new ArraySegment<byte>(buffer, offset + receivedTotal, count - receivedTotal), SocketFlags.None);
                Task completed = await Task.WhenAny(receiveTask, Task.Delay(remainingTimeout)).ConfigureAwait(false);
                if (completed != receiveTask)
                {
                    try { socket.Close(); } catch { }
                    try { await receiveTask.ConfigureAwait(false); } catch { }
                    return false;
                }

                int received = await receiveTask.ConfigureAwait(false);
                if (received <= 0)
                    return false;

                receivedTotal += received;
            }

            return true;
        }

        /// <summary>
        /// Sends the requested number of bytes.
        /// </summary>
        private static async Task SendAllAsync(Socket socket, byte[] buffer, int offset, int count)
        {
            int sentTotal = 0;
            while (sentTotal < count)
            {
                int sent = await socket.SendAsync(new ArraySegment<byte>(buffer, offset + sentTotal, count - sentTotal), SocketFlags.None).ConfigureAwait(false);
                if (sent <= 0)
                    throw new SocketException((int)SocketError.ConnectionReset);

                sentTotal += sent;
            }
        }
    }
}
#endregion
