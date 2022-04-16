using NetSquare.Core;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace NetSquareServer.Server
{
    public class UdpListener
    {
        public int NbMessageReceived { get; private set; }
        public int NbMessageSended { get; private set; }
        private UdpClient udpServer;
        private IPEndPoint receivingEndPoint;
        private NetSquare_Server server;
        private ConcurrentQueue<NetworkMessage> sendingQueue;
        private bool isSendingMessage;
        private NetworkMessage currentSendingMessage;

        public UdpListener(NetSquare_Server _server)
        {
            server = _server;
            sendingQueue = new ConcurrentQueue<NetworkMessage>();
        }

        public void Start(int port)
        {
            //The epSender identifies the incoming clients
            receivingEndPoint = new IPEndPoint(IPAddress.Any, port);
            //We are using UDP sockets
            udpServer = new UdpClient(receivingEndPoint);

            ////Assign the any IP of the machine and listen on port
            //IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse(IPAdress), port);
            ////Bind this address to the server
            //udpServer.Connect(ipEndPoint);

            //Start receiving data
            udpServer.BeginReceive(OnReceive, receivingEndPoint);
        }

        private void OnReceive(IAsyncResult res)
        {
            byte[] datagram = udpServer.EndReceive(res, ref receivingEndPoint);
            // convert datagram into networkMessage, if success, send it to server for processing
            NetworkMessage message = new NetworkMessage();
            if (message.SafeSetDatagram(datagram))
            {
                NbMessageReceived++;
                message.Client = server.GetClient(message.ClientID);
                server.MessageReceive(message);
            }

            //Start over receiving data
            udpServer.BeginReceive(OnReceive, receivingEndPoint);
        }

        public void SendMessage(NetworkMessage message)
        {
            if (!isSendingMessage)
            {
                isSendingMessage = true;
                byte[] sendingData = message.Serialize();
                udpServer.BeginSend(sendingData, sendingData.Length, message.Client.EndPoint, MessageSended, null);
            }
            else
                sendingQueue.Enqueue(message);
        }

        private void MessageSended(IAsyncResult res)
        {
            udpServer.EndSend(res);
            NbMessageSended++;
            // send other message if there is some
            if (sendingQueue.Count > 0)
            {
                isSendingMessage = true;
                while (!sendingQueue.TryDequeue(out currentSendingMessage))
                    continue;
                byte[] sendingData = currentSendingMessage.Serialize();
                udpServer.BeginSend(sendingData, sendingData.Length, currentSendingMessage.Client.EndPoint, MessageSended, null);
            }
            else
                isSendingMessage = false;
        }
    }
}