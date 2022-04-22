using System;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace NetSquare.Core
{
    public class ConnectedClient
    {
        public UInt24 ID { get; set; }
        public Socket TcpSocket { get; private set; }
        public int NbMessagesToSend { get { return SendingQueue.Count + UDP.NbSendingMessages; } }
        private int nbMessagesSended;
        public int NbMessagesSended { get { return nbMessagesSended + UDP.NbMessagesSended; } }
        public long NbMessagesReceived { get; internal set; }
        public event Action<NetworkMessage> OnMessageReceived;
        private ConcurrentQueue<byte[]> SendingQueue;
        private byte[] receivingTCPMessageBuffer;
        private byte[] currentSendingTCPMessage;
        private NetworkMessage receivingTCPMessage;
        private bool isSendingTCPMessage = false;
        public UDPConnection UDP;

        public ConnectedClient()
        {
            SendingQueue = new ConcurrentQueue<byte[]>();
            receivingTCPMessageBuffer = new byte[11];
        }

        public void AddTCPMessage(NetworkMessage msg)
        {
            AddTCPMessage(msg.Serialize());
        }

        public void AddTCPMessage(byte[] msg)
        {
            if (isSendingTCPMessage)
                SendingQueue.Enqueue(msg);
            else
                sendMessage(msg);
        }

        public void AddUDPMessage(NetworkMessage msg)
        {
            UDP.SendMessage(msg);
        }

        public void AddUDPMessage(ushort headID, byte[] msg)
        {
            UDP.SendMessage(headID, msg);
        }

        internal void Fire_OnMessageReceived(NetworkMessage message)
        {
            OnMessageReceived?.Invoke(message);
        }

        public void SetClient(Socket tcpClient, bool isClient)
        {
            TcpSocket = tcpClient;
            UDP = new UDPConnection();
            if (isClient)
                UDP.CreateClientConnection(this, tcpClient);
            else
                UDP.CreateServerConnection(this, tcpClient);
            nbMessagesSended = 0;
            StartReceivingMessages();
        }

        #region TCP
        // ==================================== Send
        private void sendMessage(byte[] message)
        {
            isSendingTCPMessage = true;
            try
            {
                TcpSocket.BeginSend(message, 0, message.Length, SocketFlags.None, MessageSended, TcpSocket);
            }
            // client disconnected
            catch (SocketException) { }
        }

        private void MessageSended(IAsyncResult res)
        {
            TcpSocket.EndSend(res);
            nbMessagesSended++;
            if (SendingQueue.Count > 0)
            {
                while (!SendingQueue.TryDequeue(out currentSendingTCPMessage))
                    continue;
                sendMessage(currentSendingTCPMessage);
            }
            else
                isSendingTCPMessage = false;
        }

        // ====================================== Receive
        private void StartReceivingMessages()
        {
            lock (receivingTCPMessageBuffer)
            {
                receivingTCPMessageBuffer = new byte[2];
                TcpSocket.BeginReceive(receivingTCPMessageBuffer, 0, 2, SocketFlags.None, MessageSizeReceived, TcpSocket);
            }
        }

        private void MessageSizeReceived(IAsyncResult res)
        {
            lock (receivingTCPMessageBuffer)
            {
                try
                {
                    TcpSocket.EndReceive(res);
                    int nsgSize = BitConverter.ToUInt16(receivingTCPMessageBuffer, 0);
                    if (nsgSize <= 10 && TcpSocket.Connected)
                    {
                        StartReceivingMessages();
                        return;
                    }

                    // no encryption, keep lenght into message
                    receivingTCPMessageBuffer = new byte[nsgSize];
                    receivingTCPMessageBuffer[0] = (byte)((ushort)nsgSize >> 8);
                    receivingTCPMessageBuffer[1] = (byte)(ushort)nsgSize;
                    TcpSocket.BeginReceive(receivingTCPMessageBuffer, 2, receivingTCPMessageBuffer.Length - 2, SocketFlags.None, MessageDataReceived, TcpSocket);
                }
                catch (SocketException)
                {
                    // client disconnected
                }
            }
        }

        private void MessageDataReceived(IAsyncResult res)
        {
            lock (receivingTCPMessageBuffer)
            {
                try
                {
                    TcpSocket.EndReceive(res);
                    NbMessagesReceived++;
                    receivingTCPMessage = new NetworkMessage();
                    receivingTCPMessage.Client = this;
                    receivingTCPMessage.SetData(receivingTCPMessageBuffer);
                    OnMessageReceived?.Invoke(receivingTCPMessage);
                    receivingTCPMessage = null;
                    StartReceivingMessages();
                }
                catch (SocketException)
                {
                    // client disconnected
                }
            }
        }
        #endregion
    }
}