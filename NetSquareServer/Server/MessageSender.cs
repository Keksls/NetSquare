using NetSquare.Core;
using NetSquareServer.Utils;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareServer.Server
{
    public class MessageSender
    {
        public List<ConnectedClient> Clients = new List<ConnectedClient>();
        public int SenderID { get; private set; }
        public bool Started { get; private set; }
        public int NbClients { get { return Clients.Count; } }
        public Thread ProcessQueueThread { get; private set; }
        private MessageSenderManager manager;

        public MessageSender(int senderID, MessageSenderManager _manager)
        {
            manager = _manager;
            Started = false;
            SenderID = senderID;
        }

        public void StopSender()
        {
            Started = false;
        }

        public void ClearClients()
        {
            lock (Clients)
                Clients = new List<ConnectedClient>();
        }

        public void AddClient(ConnectedClient client)
        {
            lock (Clients)
                Clients.Add(client);
        }

        public void RemoveClient(ConnectedClient client)
        {
            lock (Clients)
                Clients.Remove(client);
        }

        public void StartSender()
        {
            Started = true;
            ProcessQueueThread = new Thread(ProcessSendQueue);
            ProcessQueueThread.IsBackground = true;
            ProcessQueueThread.Start();
        }

        private void ProcessSendQueue()
        {
            while (Started)
            {
                lock (Clients)
                {
                    for (int i = 0; i < Clients.Count; i++)
                    {
                        try
                        {
                            if (Clients[i].ProcessSendingQueue())
                                manager.NbSended++;
                        }
                        catch (SocketException)
                        {
                            Writer.Write("Sending Queue switch disconnected client", ConsoleColor.Red);
                        }
                        catch (Exception ex)
                        {
                            Writer.Write("Sending Queue fail : \n\r" + ex.ToString(), ConsoleColor.Red);
                        }
                    }
                }
                Thread.Sleep(1);
            }
            Writer.Write("End of Sending Queue  " + SenderID, ConsoleColor.Red);
        }
    }

    public class MessageSenderManager
    {
        public int NbSenders { get; private set; }
        public bool SendersStarted { get; private set; }
        public int EmptiestSenderID { get; private set; }
        public int NbClients { get; private set; }
        public long NbSended { get; internal set; }
        private MessageSender[] senders;
        private Dictionary<uint, int> ClientSenders = new Dictionary<uint, int>();

        public MessageSenderManager(int nbSenders)
        {
            NbSenders = nbSenders;
            senders = new MessageSender[nbSenders];
            for (int i = 0; i < nbSenders; i++)
                senders[i] = new MessageSender(i, this);
        }

        public void StartSenders()
        {
            NbSended = 0;
            SendersStarted = true;
            EmptiestSenderID = 0;
            min = int.MaxValue;
            Thread processEmptiestQueueIDThread = new Thread(ProcessEmptiestSenderID);
            processEmptiestQueueIDThread.Start();
            foreach (MessageSender sender in senders)
            {
                sender.ClearClients();
                sender.StartSender();
            }
        }

        public void StopSenders()
        {
            SendersStarted = false;
            foreach (MessageSender sender in senders)
            {
                sender.StopSender();
                sender.ClearClients();
            }
            EmptiestSenderID = 0;
        }

        public void AddClient(ConnectedClient client)
        {
            int senderID = EmptiestSenderID;
            lock (ClientSenders)
            {
                ClientSenders.Add(client.ID, senderID);
                senders[senderID].AddClient(client);
            }
        }

        public void RemoveClient(ConnectedClient client)
        {
            lock (ClientSenders)
            {
                int senderID = ClientSenders[client.ID];
                senders[senderID].RemoveClient(client);
            }
        }

        public void SendMessage(byte[] message, ConnectedClient client)
        {
            client.AddMessage(message);
        }

        public void SendMessage(byte[] message, List<ConnectedClient> clients)
        {
            foreach (ConnectedClient client in clients)
                client.AddMessage(message);
        }

        int min;
        private void ProcessEmptiestSenderID()
        {
            while (SendersStarted)
            {
                NbClients = 0;
                min = int.MaxValue;
                for (int i = 0; i < NbSenders; i++)
                {
                    NbClients += senders[i].NbClients;
                    if (senders[i].NbClients < min)
                    {
                        min = senders[i].NbClients;
                        EmptiestSenderID = i;
                    }
                }
                Thread.Sleep(10);
            }
        }

        public int GetNbMessagesToSend()
        {
            int nbMessagesToSend = 0;
            for (int i = 0; i < NbSenders; i++)
                for (int j = 0; j < senders[i].Clients.Count; j++)
                    nbMessagesToSend += senders[i].Clients[j].NbMessagesToSend;
            return nbMessagesToSend;
        }
    }
}