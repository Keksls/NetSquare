using System;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace NetSquare.Core
{
    public class ConnectedClient
    {
        public uint ID { get; set; }
        public Socket Socket { get; set; }
        public int NbMessagesToSend { get { return sendingQueue.Count; } }
        public event Action<NetworkMessage> OnMessageReceived;
        private ConcurrentQueue<byte[]> sendingQueue;
        private byte[] receivingMessageBuffer;
        private byte[] currentSendingMessage;
        private NetworkMessage receivingMessage;
        private bool isReceivingMessage = false;
        private bool isSendingMessage = false;

        public ConnectedClient()
        {
            sendingQueue = new ConcurrentQueue<byte[]>();
        }

        public void AddMessage(NetworkMessage msg)
        {
            sendingQueue.Enqueue(msg.Serialize());
        }

        public void AddMessage(byte[] msg)
        {
            sendingQueue.Enqueue(msg);
        }

        public bool ProcessSendingQueue()
        {
            if (isSendingMessage)
                return false;

            if (sendingQueue.TryDequeue(out currentSendingMessage))
            {
                isSendingMessage = true;
                Socket.BeginSend(currentSendingMessage, 0, currentSendingMessage.Length, SocketFlags.None, new AsyncCallback(MessageSended), Socket);
                return true;
            }
            return false;
        }

        private void MessageSended(IAsyncResult res)
        {
            Socket.EndSend(res);
            isSendingMessage = false;
        }

        public void ReceiveMessage()
        {
            if (isReceivingMessage)
                return;

            if (Socket.Available > 0)
            {
                isReceivingMessage = true;
                receivingMessageBuffer = new byte[12];
                Socket.BeginReceive(receivingMessageBuffer, 0, 12, SocketFlags.None, new AsyncCallback(MessageLenghtReceived), Socket);
            }
        }

        private void MessageLenghtReceived(IAsyncResult res)
        {
            Socket.EndReceive(res);

            receivingMessage = new NetworkMessage();
            receivingMessage.SetHead(receivingMessageBuffer);
            receivingMessageBuffer = new byte[receivingMessage.Length - 12];
            Socket.BeginReceive(receivingMessageBuffer, 0, receivingMessageBuffer.Length, SocketFlags.None, new AsyncCallback(MessageDataReceived), Socket);
        }

        private void MessageDataReceived(IAsyncResult res)
        {
            Socket.EndReceive(res);
            receivingMessage.Client = this;
            receivingMessage.SetData(receivingMessageBuffer);
            OnMessageReceived?.Invoke(receivingMessage);
            receivingMessage = null;
            isReceivingMessage = false;
        }
    }
}