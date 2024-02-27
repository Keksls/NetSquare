using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace NetSquare.Client
{
    public struct ClientStatistics
    {
        public int NbClients;
        public int NbMessagesToSend;
        public long NbMessagesSended;
        public long NbMessagesReceived;
        public float Downloading;
        public float Uploading;
        public int NbMessagesSending;
        public int NbMessagesReceiving;
        public int NbProcessingMessages;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Clients: ")
                .Append(NbClients)

                .Append(" - Down : ")
                .Append(Downloading.ToString("f2")).Append(" ko/s | ")
                .Append(NbMessagesReceiving).Append(" msg/s | (")
                .Append(NbMessagesReceived).Append(" msg)")

                .Append(" - Up : ")
                .Append(Uploading.ToString("f2")).Append(" ko/s | ")
                .Append(NbMessagesSending).Append(" msg/s | (")
                .Append(NbMessagesSended).Append(" msg)");

            return sb.ToString();
        }
    }

    public class ClientStatisticsManager
    {
        private List<NetSquareClient> clients;
        private bool stopOrder = false;
        public event Action<ClientStatistics> OnGetStatistics;
        public bool Running { get; private set; }
        public ClientStatistics CurrentStatistics { get; private set; }
        private long lastProcessReceived = 0;
        private long lastProcessSended = 0;
        private int intervalMs = 100;
        public int IntervalMs
        {
            get
            {
                return intervalMs;
            }
            set
            {
                intervalMs = value;
            }
        }

        public ClientStatisticsManager()
        {
            clients = new List<NetSquareClient>();
        }

        public void AddClient(NetSquareClient client)
        {
            lock (clients)
            {
                clients.Add(client);
            }
        }

        public void AddClients(List<NetSquareClient> clients)
        {
            lock (clients)
            {
                this.clients.AddRange(clients);
            }
        }

        public void RemoveClient(NetSquareClient client)
        {
            lock (clients)
            {
                clients.Remove(client);
            }
        }

        public void ClearClients()
        {
            lock (clients)
            {
                clients.Clear();
            }
        }

        public void RemoveClients(List<NetSquareClient> clients)
        {
            lock (clients)
            {
                foreach (var client in clients)
                    this.clients.Remove(client);
            }
        }

        public void Start()
        {
            Running = true;
            ThreadPool.QueueUserWorkItem((sender) =>
            {
                while (!stopOrder)
                {
                    if (OnGetStatistics != null && clients != null)
                    {
                        int toSend = 0;
                        long sended = 0;
                        long received = 0;
                        long bytesSended = 0;
                        long bytesReceived = 0;
                        int nbProcessingMessages = 0;
                        lock (clients)
                        {
                            foreach (var client in clients)
                            {
                                toSend += client.Client.NbMessagesToSend;
                                sended += client.Client.NbMessagesSended;
                                received += client.Client.NbMessagesReceived;
                                bytesSended += client.Client.SendedBytes;
                                bytesReceived += client.Client.ReceivedBytes;
                                client.Client.SendedBytes = 0;
                                client.Client.ReceivedBytes = 0;
                                nbProcessingMessages += client.NbProcessingMessages;
                            }
                        }

                        long receivedThisTick = received - lastProcessReceived;
                        lastProcessReceived = received;
                        if (receivedThisTick < 0)
                            receivedThisTick = 0;
                        long sendedThisTick = sended - lastProcessSended;
                        lastProcessSended = sended;
                        if (sendedThisTick < 0)
                            sendedThisTick = 0;

                        CurrentStatistics = new ClientStatistics()
                        {
                            NbClients = clients.Count,
                            NbProcessingMessages = nbProcessingMessages,
                            NbMessagesToSend = toSend,
                            NbMessagesSended = sended,
                            NbMessagesReceived = received,
                            Downloading = (float)bytesReceived / 1024f * ((1f / (float)intervalMs) * 1000f),
                            Uploading = (float)bytesSended / 1024f * ((1f / (float)intervalMs) * 1000f),
                            NbMessagesReceiving = (int)((float)receivedThisTick * ((1f / (float)intervalMs) * 1000f)),
                            NbMessagesSending = (int)((float)sendedThisTick * ((1f / (float)intervalMs) * 1000f)),
                        };
                        OnGetStatistics(CurrentStatistics);
                    }
                    Thread.Sleep(intervalMs);
                }
            });
        }

        public void Stop()
        {
            stopOrder = true;
            Running = false;
        }
    }
}
