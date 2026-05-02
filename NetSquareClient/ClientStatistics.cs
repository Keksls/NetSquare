using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

#region Source
namespace NetSquare.Client
{
    /// <summary>
    /// Represents the client statistics value.
    /// </summary>
    public struct ClientStatistics
    {
        /// <summary>
        /// Stores the nb clients value.
        /// </summary>
        public int NbClients;
        /// <summary>
        /// Stores the nb messages to send value.
        /// </summary>
        public int NbMessagesToSend;
        /// <summary>
        /// Stores the nb messages sended value.
        /// </summary>
        public long NbMessagesSended;
        /// <summary>
        /// Stores the nb messages received value.
        /// </summary>
        public long NbMessagesReceived;
        /// <summary>
        /// Stores the downloading value.
        /// </summary>
        public float Downloading;
        /// <summary>
        /// Stores the uploading value.
        /// </summary>
        public float Uploading;
        /// <summary>
        /// Stores the nb messages sending value.
        /// </summary>
        public int NbMessagesSending;
        /// <summary>
        /// Stores the nb messages receiving value.
        /// </summary>
        public int NbMessagesReceiving;
        /// <summary>
        /// Stores the nb processing messages value.
        /// </summary>
        public int NbProcessingMessages;

        /// <summary>
        /// Executes the to string operation.
        /// </summary>
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

    /// <summary>
    /// Represents the client statistics manager component.
    /// </summary>
    public class ClientStatisticsManager
    {
        /// <summary>
        /// Stores the clients value.
        /// </summary>
        private List<NetSquareClient> clients;
        /// <summary>
        /// Stores the stop order value.
        /// </summary>
        private bool stopOrder = false;
        /// <summary>
        /// Occurs when get statistics is raised.
        /// </summary>
        public event Action<ClientStatistics> OnGetStatistics;
        /// <summary>
        /// Gets or sets the running value.
        /// </summary>
        public bool Running { get; private set; }
        /// <summary>
        /// Gets or sets the current statistics value.
        /// </summary>
        public ClientStatistics CurrentStatistics { get; private set; }
        /// <summary>
        /// Stores the last process received value.
        /// </summary>
        private long lastProcessReceived = 0;
        /// <summary>
        /// Stores the last process sended value.
        /// </summary>
        private long lastProcessSended = 0;
        /// <summary>
        /// Stores the interval ms value.
        /// </summary>
        private int intervalMs = 100;
        /// <summary>
        /// Stores the interval ms value.
        /// </summary>
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

        /// <summary>
        /// Executes the client statistics manager operation.
        /// </summary>
        public ClientStatisticsManager()
        {
            clients = new List<NetSquareClient>();
        }

        /// <summary>
        /// Executes the add client operation.
        /// </summary>
        public void AddClient(NetSquareClient client)
        {
            lock (clients)
            {
                clients.Add(client);
            }
        }

        /// <summary>
        /// Executes the add clients operation.
        /// </summary>
        public void AddClients(List<NetSquareClient> clients)
        {
            lock (clients)
            {
                this.clients.AddRange(clients);
            }
        }

        /// <summary>
        /// Executes the remove client operation.
        /// </summary>
        public void RemoveClient(NetSquareClient client)
        {
            lock (clients)
            {
                clients.Remove(client);
            }
        }

        /// <summary>
        /// Executes the clear clients operation.
        /// </summary>
        public void ClearClients()
        {
            lock (clients)
            {
                clients.Clear();
            }
        }

        /// <summary>
        /// Executes the remove clients operation.
        /// </summary>
        public void RemoveClients(List<NetSquareClient> clients)
        {
            lock (clients)
            {
                foreach (var client in clients)
                    this.clients.Remove(client);
            }
        }

        /// <summary>
        /// Executes the start operation.
        /// </summary>
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

        /// <summary>
        /// Executes the stop operation.
        /// </summary>
        public void Stop()
        {
            stopOrder = true;
            Running = false;
        }
    }
}
#endregion
