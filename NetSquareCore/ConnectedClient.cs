using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace NetSquare.Core
{
    public class ConnectedClient
    {
        public uint ID { get; set; }
        public Socket TcpSocket { get; private set; }
        public IPEndPoint EndPoint { get; private set; }
        public int NbMessagesToSend { get { return SendingQueue.Count; } }
        public int NbMessagesSended { get; private set; }
        public long NbMessagesReceived { get; private set; }
        public event Action<NetworkMessage> OnMessageReceived;
        private ConcurrentQueue<byte[]> SendingQueue;
        private byte[] receivingTCPMessageBuffer;
        private byte[] currentSendingTCPMessage;
        private NetworkMessage receivingTCPMessage;
        private bool isSendingMessage = false;

        public ConnectedClient()
        {
            SendingQueue = new ConcurrentQueue<byte[]>();
        }

        public void AddTCPMessage(NetworkMessage msg)
        {
            AddTCPMessage(msg.Serialize());
        }

        public void AddTCPMessage(byte[] msg)
        {
            if (isSendingMessage || SendingQueue.Count > 0)
                SendingQueue.Enqueue(msg);
            else
                sendMessage(msg);
        }

        public void SetClient(Socket tcpClient)
        {
            TcpSocket = tcpClient;
            EndPoint = (IPEndPoint)TcpSocket.RemoteEndPoint;
            NbMessagesSended = 0;
            StartReceivingMessages();
        }

        #region TCP
        // ==================================== Send
        private void sendMessage(byte[] message)
        {
            isSendingMessage = true;
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
            NbMessagesSended++;
            if (SendingQueue.Count > 0)
            {
                while (!SendingQueue.TryDequeue(out currentSendingTCPMessage))
                    continue;
                sendMessage(currentSendingTCPMessage);
            }
            else
                isSendingMessage = false;
        }

        // ====================================== Receive
        private void StartReceivingMessages()
        {
            receivingTCPMessageBuffer = new byte[12];
            TcpSocket.BeginReceive(receivingTCPMessageBuffer, 0, 12, SocketFlags.None, MessageHeaderReceived, TcpSocket);
        }

        private void MessageHeaderReceived(IAsyncResult res)
        {
            try
            {
                TcpSocket.EndReceive(res);
                receivingTCPMessage = new NetworkMessage();
                receivingTCPMessage.Client = this;
                receivingTCPMessage.SetHead(receivingTCPMessageBuffer);
                receivingTCPMessageBuffer = new byte[receivingTCPMessage.Length - 12];
                TcpSocket.BeginReceive(receivingTCPMessageBuffer, 0, receivingTCPMessageBuffer.Length, SocketFlags.None, MessageDataReceived, TcpSocket);
            }
            catch (SocketException)
            {
                // client disconnected
            }
        }

        private void MessageDataReceived(IAsyncResult res)
        {
            try
            {
                TcpSocket.EndReceive(res);
                NbMessagesReceived++;
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
        #endregion
    }
}