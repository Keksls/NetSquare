using NetSquare.Core;
using NetSquareServer.Utils;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareServer.Server
{
    public class MessageReceiver
    {
        public List<ConnectedClient> Clients = new List<ConnectedClient>();
        public int ReceiverID { get; private set; }
        public bool Started { get; private set; }
        public int NbClients { get { return Clients.Count; } }
        public Thread ReceiverThread { get; private set; }
        private NetSquare_Server server;

        public MessageReceiver(NetSquare_Server _server, int receiverID, int bufferSize)
        {
            server = _server;
            Started = false;
            ReceiverID = receiverID;
        }

        public void AddClient(ConnectedClient client)
        {
            Clients.Add(client);
            client.OnMessageReceived += Client_OnMessageReceived;
        }

        private void Client_OnMessageReceived(NetworkMessage message)
        {
            server.MessageReceive(message);
        }

        public void StopReceiver()
        {
            Started = false;
        }

        public void ClearQueue()
        {
            Clients = new List<ConnectedClient>();
        }

        public void StartReceiver()
        {
            Started = true;
            ReceiverThread = new Thread(ReceiveClientMessages);
            ReceiverThread.IsBackground = true;
            ReceiverThread.Start();
        }

        private void ReceiveClientMessages()
        {
            while (Started)
            {
                try
                {
                    for (int clientIndex = 0; clientIndex < Clients.Count; clientIndex++)
                    {
                        // get current client
                        ConnectedClient c = Clients[clientIndex];
                        if (c == null)
                            continue;

                        // Handle Disconnect
                        if (!c.Socket.IsConnected())
                        {
                            c.OnMessageReceived += Client_OnMessageReceived;
                            Clients.Remove(c);
                            server.Server_ClientDisconnected(c);
                            continue;
                        }

                        c.ReceiveMessage();
                    }

                    Thread.Sleep(1);
                }
                catch (SocketException)
                {
                    Writer.Write("Receiver switch disconnected client", ConsoleColor.Red);
                }
                catch (Exception ex)
                {
                    Writer.Write("Receiver fail : \n\r" + ex.ToString(), ConsoleColor.Red);
                }
            }
            Writer.Write("End of Receiver work (ID " + ReceiverID + ")", ConsoleColor.Red);
        }
    }

    public class MessageReceiverManager
    {
        private MessageReceiver[] receivers;
        public int NbReceivers { get; private set; }
        public bool ReceiversStarted { get; private set; }
        public int EmptiestReceiverID { get; private set; }
        public int NbClients { get; private set; }

        public MessageReceiverManager(NetSquare_Server server, int nbSenders, int bufferSize)
        {
            NbReceivers = nbSenders;
            receivers = new MessageReceiver[nbSenders];
            for (int i = 0; i < nbSenders; i++)
                receivers[i] = new MessageReceiver(server, i, bufferSize);
        }

        public void StartReceivers()
        {
            ReceiversStarted = true;
            EmptiestReceiverID = 0;
            min = int.MaxValue;
            Thread processEmptiestQueueIDThread = new Thread(ProcessEmptiestReceiverID);
            processEmptiestQueueIDThread.Start();
            foreach (MessageReceiver receiver in receivers)
            {
                receiver.ClearQueue();
                receiver.StartReceiver();
            }
        }

        public void StopReceivers()
        {
            ReceiversStarted = false;
            foreach (MessageReceiver receiver in receivers)
            {
                receiver.StopReceiver();
                receiver.ClearQueue();
            }
            EmptiestReceiverID = 0;
        }

        public void AddClient(ConnectedClient client)
        {
            receivers[EmptiestReceiverID].AddClient(client);
        }

        int min;
        private void ProcessEmptiestReceiverID()
        {
            while (ReceiversStarted)
            {
                NbClients = 0;
                min = int.MaxValue;
                for (int i = 0; i < NbReceivers; i++)
                {
                    NbClients += receivers[i].NbClients;
                    if (receivers[i].NbClients < min)
                    {
                        min = receivers[i].NbClients;
                        EmptiestReceiverID = i;
                    }
                }
                Thread.Sleep(100);
            }
        }
    }
}