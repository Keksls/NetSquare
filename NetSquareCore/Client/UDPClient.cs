using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetSquare.Core
{
    /// <summary>
    /// Represents the udp connection component.
    /// </summary>
    public class UDPConnection
    {
        /// <summary>
        /// Stores the connection value.
        /// </summary>
        public UdpClient connection;
        /// <summary>
        /// Stores the remote end point value.
        /// </summary>
        public IPEndPoint RemoteEndPoint;
        /// <summary>
        /// Stores the nb sending messages value.
        /// </summary>
        public int NbSendingMessages;
        /// <summary>
        /// Stores the nb messages sended value.
        /// </summary>
        public int NbMessagesSended;
        /// <summary>
        /// Stores the nb messages dropped value.
        /// </summary>
        private long nbMessagesDropped;
        /// <summary>
        /// Gets or sets the nb messages dropped value.
        /// </summary>
        public long NbMessagesDropped { get { return Interlocked.Read(ref nbMessagesDropped); } }
        /// <summary>
        /// Stores the sended bytes value.
        /// </summary>
        internal long sendedBytes = 0;
        /// <summary>
        /// Stores the received bytes value.
        /// </summary>
        internal long receivedBytes = 0;
        /// <summary>
        /// Stores the udp sending queue value.
        /// </summary>
        private Dictionary<ushort, byte[]> UDPSendingQueue;
        /// <summary>
        /// Stores the queued udp messages value.
        /// </summary>
        private int queuedUdpMessages;
        /// <summary>
        /// Stores the related client value.
        /// </summary>
        private ConnectedClient relatedClient;
        /// <summary>
        /// Stores the is sending udp message value.
        /// </summary>
        private bool isSendingUDPMessage = false;
        /// <summary>
        /// Stores the is server value.
        /// </summary>
        private bool isServer = false;
        /// <summary>
        /// Stores the message types array value.
        /// </summary>
        private ushort[] messageTypesArray;
        /// <summary>
        /// Stores the last message type index sended value.
        /// </summary>
        private int lastMessageTypeIndexSended;
        /// <summary>
        /// Stores the current sending message value.
        /// </summary>
        private byte[] currentSendingMessage = null;
        /// <summary>
        /// Stores the send lock value.
        /// </summary>
        private readonly object sendLock = new object();
        /// <summary>
        /// Signals the UDP send pump when a connection has queued work.
        /// </summary>
        private readonly SemaphoreSlim sendSignal = new SemaphoreSlim(0);
        /// <summary>
        /// Stores whether the UDP send pump has been started.
        /// </summary>
        private int sendPumpStarted;
        /// <summary>
        /// Stores the server hub value.
        /// </summary>
        private ServerUdpHub serverHub;
        /// <summary>
        /// Stores the server hubs value.
        /// </summary>
        private static readonly ConcurrentDictionary<string, ServerUdpHub> ServerHubs = new ConcurrentDictionary<string, ServerUdpHub>();

        /// <summary>
        /// Executes the udp connection operation.
        /// </summary>
        public UDPConnection()
        {
            UDPSendingQueue = new Dictionary<ushort, byte[]>();
            messageTypesArray = new ushort[0];
            lastMessageTypeIndexSended = 0;
        }

        /// <summary>
        /// Create new Client Side UDP Connection
        /// </summary>
        /// <param name="_relatedClient">ConnectedClient owner</param>
        /// <param name="relatedTcpClient">TCP socket equivalent</param>
        public void CreateClientConnection(ConnectedClient _relatedClient, Socket relatedTcpClient)
        {
            isServer = false;
            relatedClient = _relatedClient;
            IPEndPoint localTcpEndPoint = (IPEndPoint)relatedTcpClient.LocalEndPoint;
            IPEndPoint remoteTcpEndPoint = (IPEndPoint)relatedTcpClient.RemoteEndPoint;
            RemoteEndPoint = new IPEndPoint(remoteTcpEndPoint.Address, remoteTcpEndPoint.Port + 1);
            connection = new UdpClient(new IPEndPoint(localTcpEndPoint.Address, localTcpEndPoint.Port + 1));
            connection.Connect(RemoteEndPoint);
            StartClientReceiveLoop();
        }

        /// <summary>
        /// Create new Server Side UDP Connection
        /// </summary>
        /// <param name="_relatedClient">ConnectedClient owner</param>
        /// <param name="relatedTcpClient">TCP socket equivalent</param>
        public void CreateServerConnection(ConnectedClient _relatedClient, Socket relatedTcpClient)
        {
            isServer = true;
            relatedClient = _relatedClient;
            IPEndPoint localTcpEndPoint = (IPEndPoint)relatedTcpClient.LocalEndPoint;
            IPEndPoint remoteTcpEndPoint = (IPEndPoint)relatedTcpClient.RemoteEndPoint;
            RemoteEndPoint = new IPEndPoint(remoteTcpEndPoint.Address, remoteTcpEndPoint.Port + 1);
            IPEndPoint localUdpEndPoint = new IPEndPoint(localTcpEndPoint.Address, localTcpEndPoint.Port + 1);
            serverHub = GetServerHub(localUdpEndPoint);
            connection = serverHub.Connection;
            RegisterServerClient();
        }

        /// <summary>
        /// Executes the register server client operation.
        /// </summary>
        public void RegisterServerClient()
        {
            if (isServer && serverHub != null && relatedClient.ID != 0)
                serverHub.Register(this);
        }

        /// <summary>
        /// Executes the unregister server client operation.
        /// </summary>
        public void UnregisterServerClient()
        {
            if (isServer && serverHub != null && relatedClient.ID != 0)
                serverHub.Unregister(relatedClient.ID);
        }

        /// <summary>
        /// Executes the send message operation.
        /// </summary>
        public void SendMessage(NetworkMessage msg)
        {
            SendMessage(msg.HeadID, msg.Serialize());
        }

        /// <summary>
        /// Executes the send message operation.
        /// </summary>
        public void SendMessage(ushort headID, byte[] msg)
        {
            if (msg == null || msg.Length == 0)
                return;

            bool shouldStartSend = false;
            lock (sendLock)
            {
                // add message index for HeadID
                if (!UDPSendingQueue.ContainsKey(headID))
                {
                    UDPSendingQueue.Add(headID, null);
                    Array.Resize(ref messageTypesArray, messageTypesArray.Length + 1);
                    messageTypesArray[messageTypesArray.Length - 1] = headID;
                }

                // already sending datagram, so let's save it for furture send
                if (isSendingUDPMessage)
                {
                    if (UDPSendingQueue[headID] != null)
                    {
                        Interlocked.Increment(ref nbMessagesDropped);
                    }
                    else
                    {
                        queuedUdpMessages++;
                    }
                    // set current message as last for this headID in the  sending queue
                    UDPSendingQueue[headID] = msg;
                }
                else
                {
                    isSendingUDPMessage = true;
                    currentSendingMessage = msg;
                    shouldStartSend = true;
                }
                RefreshSendingCountLocked();
            }

            if (shouldStartSend)
                BeginSendMessage(msg);
        }

        #region UDP
        /// <summary>
        /// Starts the client receive loop.
        /// </summary>
        private void StartClientReceiveLoop()
        {
            _ = Task.Run(ClientReceiveLoopAsync);
        }

        /// <summary>
        /// Receives UDP datagrams asynchronously on the client side.
        /// </summary>
        private async Task ClientReceiveLoopAsync()
        {
            while (connection != null)
            {
                try
                {
                    UdpReceiveResult result = await connection.ReceiveAsync().ConfigureAwait(false);
                    byte[] datagram = result.Buffer;
                    RemoteEndPoint = result.RemoteEndPoint;
                    receivedBytes += datagram.Length;

                    NetworkMessage message = new NetworkMessage();
                    if (message.SafeSetDatagram(datagram))
                    {
                        relatedClient.NbMessagesReceived++;
                        message.Client = relatedClient;
                        relatedClient.Fire_OnMessageReceived(message);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Executes the on receive udp operation.
        /// </summary>
        private void OnReceiveUDP(IAsyncResult res)
        {
            try
            {
                byte[] datagram = connection.EndReceive(res, ref RemoteEndPoint);
                receivedBytes += datagram.Length;
                // convert datagram into networkMessage, if success, send it to server for processing
                NetworkMessage message = new NetworkMessage();
                if (message.SafeSetDatagram(datagram))
                {
                    relatedClient.NbMessagesReceived++;
                    message.Client = relatedClient;
                    relatedClient.Fire_OnMessageReceived(message);
                }

                //Start over receiving data
                connection.BeginReceive(OnReceiveUDP, null);
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        /// <summary>
        /// Executes the begin send message operation.
        /// </summary>
        private void BeginSendMessage(byte[] message)
        {
            EnsureSendPumpStarted();
            sendSignal.Release();
        }

        /// <summary>
        /// Starts the UDP send pump once for this connection.
        /// </summary>
        private void EnsureSendPumpStarted()
        {
            if (Interlocked.CompareExchange(ref sendPumpStarted, 1, 0) == 0)
                _ = Task.Run(SendPumpAsync);
        }

        /// <summary>
        /// Sends UDP messages through one async pump instead of creating one task per datagram.
        /// </summary>
        private async Task SendPumpAsync()
        {
            while (connection != null)
            {
                try
                {
                    await sendSignal.WaitAsync().ConfigureAwait(false);

                    while (true)
                    {
                        byte[] message;
                        lock (sendLock)
                            message = currentSendingMessage;

                        if (message == null)
                        {
                            lock (sendLock)
                            {
                                if (currentSendingMessage == null)
                                {
                                    isSendingUDPMessage = false;
                                    RefreshSendingCountLocked();
                                    break;
                                }
                            }
                            continue;
                        }

                        sendedBytes += message.Length;
                        if (isServer)
                            await connection.SendAsync(message, message.Length, RemoteEndPoint).ConfigureAwait(false);
                        else
                            await connection.SendAsync(message, message.Length).ConfigureAwait(false);

                        NbMessagesSended++;

                        byte[] nextMessage = null;
                        lock (sendLock)
                        {
                            currentSendingMessage = null;
                            if (GetNextSendingMessage(ref nextMessage))
                                currentSendingMessage = nextMessage;
                            else
                                isSendingUDPMessage = false;

                            RefreshSendingCountLocked();
                        }

                        if (nextMessage == null)
                            break;
                    }
                }
                catch (SocketException)
                {
                    ResetSendingState();
                    return;
                }
                catch (ObjectDisposedException)
                {
                    ResetSendingState();
                    return;
                }
            }
        }

        /// <summary>
        /// Executes the message sended operation.
        /// </summary>
        private void MessageSended(IAsyncResult res)
        {
            try
            {
                connection.EndSend(res);
                NbMessagesSended++;

                // send other message if there is some
                byte[] nextMessage = null;
                lock (sendLock)
                {
                    currentSendingMessage = null;
                    if (GetNextSendingMessage(ref nextMessage))
                    {
                        currentSendingMessage = nextMessage;
                    }
                    else
                    {
                        isSendingUDPMessage = false;
                    }
                    RefreshSendingCountLocked();
                }

                if (nextMessage != null)
                    BeginSendMessage(nextMessage);
            }
            catch (SocketException)
            {
                ResetSendingState();
            }
            catch (ObjectDisposedException)
            {
                ResetSendingState();
            }
        }

        /// <summary>
        /// Resets the UDP send state after socket failure.
        /// </summary>
        private void ResetSendingState()
        {
            lock (sendLock)
            {
                currentSendingMessage = null;
                isSendingUDPMessage = false;
                queuedUdpMessages = 0;
                ClearQueuedMessagesLocked();
                RefreshSendingCountLocked();
            }
        }

        /// <summary>
        /// Executes the get next sending message operation.
        /// </summary>
        private bool GetNextSendingMessage(ref byte[] message)
        {
            if (messageTypesArray.Length == 0)
                return false;

            // switch to next index
            lastMessageTypeIndexSended++;
            lastMessageTypeIndexSended %= messageTypesArray.Length;

            int nbTry = 0;
            while (nbTry < messageTypesArray.Length)
            {
                if (UDPSendingQueue[messageTypesArray[lastMessageTypeIndexSended]] != null)
                {
                    message = UDPSendingQueue[messageTypesArray[lastMessageTypeIndexSended]];
                    UDPSendingQueue[messageTypesArray[lastMessageTypeIndexSended]] = null;
                    queuedUdpMessages--;
                    return true;
                }
                // switch to next index
                lastMessageTypeIndexSended++;
                lastMessageTypeIndexSended %= messageTypesArray.Length;
                nbTry++;
            }

            return false;
        }

        /// <summary>
        /// Executes the refresh sending count locked operation.
        /// </summary>
        private void RefreshSendingCountLocked()
        {
            NbSendingMessages = queuedUdpMessages + (isSendingUDPMessage ? 1 : 0);
        }

        /// <summary>
        /// Executes the clear queued messages locked operation.
        /// </summary>
        private void ClearQueuedMessagesLocked()
        {
            for (int i = 0; i < messageTypesArray.Length; i++)
                UDPSendingQueue[messageTypesArray[i]] = null;
        }

        /// <summary>
        /// Executes the get server hub operation.
        /// </summary>
        private static ServerUdpHub GetServerHub(IPEndPoint localEndPoint)
        {
            return ServerHubs.GetOrAdd(localEndPoint.ToString(), key => new ServerUdpHub(localEndPoint));
        }

        /// <summary>
        /// Represents the server udp hub component.
        /// </summary>
        private class ServerUdpHub
        {
            /// <summary>
            /// Gets or sets the connection value.
            /// </summary>
            public UdpClient Connection { get; private set; }
            /// <summary>
            /// Stores the clients value.
            /// </summary>
            private ConcurrentDictionary<uint, UDPConnection> clients = new ConcurrentDictionary<uint, UDPConnection>();

            /// <summary>
            /// Executes the server udp hub operation.
            /// </summary>
            public ServerUdpHub(IPEndPoint localEndPoint)
            {
                Connection = new UdpClient(localEndPoint);
                StartReceive();
            }

            /// <summary>
            /// Executes the register operation.
            /// </summary>
            public void Register(UDPConnection connection)
            {
                clients[connection.relatedClient.ID] = connection;
            }

            /// <summary>
            /// Executes the unregister operation.
            /// </summary>
            public void Unregister(uint clientID)
            {
                UDPConnection removed;
                clients.TryRemove(clientID, out removed);
            }

            /// <summary>
            /// Executes the start receive operation.
            /// </summary>
            private void StartReceive()
            {
                _ = Task.Run(ReceiveLoopAsync);
            }

            /// <summary>
            /// Receives UDP datagrams asynchronously on the server shared socket.
            /// </summary>
            private async Task ReceiveLoopAsync()
            {
                while (true)
                {
                    try
                    {
                        UdpReceiveResult result = await Connection.ReceiveAsync().ConfigureAwait(false);
                        byte[] datagram = result.Buffer;
                        NetworkMessage message = new NetworkMessage();
                        if (message.SafeSetDatagram(datagram))
                        {
                            UDPConnection clientConnection;
                            if (clients.TryGetValue(message.ClientID, out clientConnection))
                            {
                                clientConnection.RemoteEndPoint = result.RemoteEndPoint;
                                clientConnection.receivedBytes += datagram.Length;
                                clientConnection.relatedClient.NbMessagesReceived++;
                                message.Client = clientConnection.relatedClient;
                                clientConnection.relatedClient.Fire_OnMessageReceived(message);
                            }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        return;
                    }
                }
            }

            /// <summary>
            /// Executes the on receive udp operation.
            /// </summary>
            private void OnReceiveUDP(IAsyncResult res)
            {
                try
                {
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] datagram = Connection.EndReceive(res, ref remoteEndPoint);
                    NetworkMessage message = new NetworkMessage();
                    if (message.SafeSetDatagram(datagram))
                    {
                        UDPConnection clientConnection;
                        if (clients.TryGetValue(message.ClientID, out clientConnection))
                        {
                            clientConnection.RemoteEndPoint = remoteEndPoint;
                            clientConnection.receivedBytes += datagram.Length;
                            clientConnection.relatedClient.NbMessagesReceived++;
                            message.Client = clientConnection.relatedClient;
                            clientConnection.relatedClient.Fire_OnMessageReceived(message);
                        }
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException) { }
                finally
                {
                    try { StartReceive(); } catch { }
                }
            }
        }
        #endregion
    }
}
