using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace NetSquare.Core
{
    public class UDPConnection
    {
        public UdpClient connection;
        public IPEndPoint RemoteEndPoint;
        public int NbSendingMessages;
        public int NbMessagesSended;
        internal long sendedBytes = 0;
        internal long receivedBytes = 0;
        private ConcurrentDictionary<ushort, byte[]> UDPSendingQueue;
        private ConnectedClient relatedClient;
        private bool isSendingUDPMessage = false;
        private bool isServer = false;
        private ushort[] messageTypesArray;
        private int lastMessageTypeIndexSended;
        private byte[] currentSendingMessage = null;

        public UDPConnection()
        {
            UDPSendingQueue = new ConcurrentDictionary<ushort, byte[]>();
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
            RemoteEndPoint = new IPEndPoint(((IPEndPoint)relatedTcpClient.LocalEndPoint).Address, ((IPEndPoint)relatedTcpClient.LocalEndPoint).Port + 1);
            connection = new UdpClient();
            connection.Connect(RemoteEndPoint);
            connection.BeginReceive(OnReceiveUDP, RemoteEndPoint);
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
            //The RemoteEndPoint identifies the incoming client
            RemoteEndPoint = new IPEndPoint(((IPEndPoint)relatedTcpClient.RemoteEndPoint).Address, ((IPEndPoint)relatedTcpClient.RemoteEndPoint).Port + 1);
            //We are using UDP sockets
            connection = new UdpClient(RemoteEndPoint);
            //Start receiving data
            connection.BeginReceive(OnReceiveUDP, RemoteEndPoint);
        }

        public void SendMessage(NetworkMessage msg)
        {
            SendMessage(msg.HeadID, msg.Serialize());
        }

        public void SendMessage(ushort headID, byte[] msg)
        {
            // add message index for HeadID
            if (!UDPSendingQueue.ContainsKey(headID))
            {
                while (!UDPSendingQueue.TryAdd(headID, null))
                    continue;
                // add headID in message type array
                var messageTypesList = messageTypesArray.ToList();
                // converting as list to grow because it will be called very few times
                messageTypesList.Add(headID);
                // save it as array because it's faster and smaller for GC
                messageTypesArray = messageTypesList.ToArray();
            }

            // already sending datagram, so let's save it for furture send
            if (isSendingUDPMessage)
            {
                // set current message as last for this headID in the  sending queue
                UDPSendingQueue[headID] = msg;
            }
            else
                BeginSendMessage(msg);
        }

        #region UDP
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
                connection.BeginReceive(OnReceiveUDP, RemoteEndPoint);
            }
            catch (SocketException) { }
        }

        private void BeginSendMessage(byte[] message)
        {
            isSendingUDPMessage = true;
            sendedBytes += message.Length;
            if (isServer)
                connection.BeginSend(message, message.Length, RemoteEndPoint, MessageSended, null);
            else
                connection.BeginSend(message, message.Length, MessageSended, null);
        }

        private void MessageSended(IAsyncResult res)
        {
            try
            {
                connection.EndSend(res);
                currentSendingMessage = null;
                NbMessagesSended++;

                // send other message if there is some
                if (GetNextSendingMessage(ref currentSendingMessage))
                {
                    isSendingUDPMessage = true;
                    if (isServer)
                        connection.BeginSend(currentSendingMessage, currentSendingMessage.Length, RemoteEndPoint, MessageSended, null);
                    else
                        connection.BeginSend(currentSendingMessage, currentSendingMessage.Length, MessageSended, null);
                }
                else
                    isSendingUDPMessage = false;
            }
            catch (SocketException) { }
        }

        private bool GetNextSendingMessage(ref byte[] message)
        {
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
                    return true;
                }
                // switch to next index
                lastMessageTypeIndexSended++;
                lastMessageTypeIndexSended %= messageTypesArray.Length;
                nbTry++;
            }

            return false;
        }
        #endregion
    }
}