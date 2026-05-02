using System;
using System.Text;
using System.Threading;

#region Source
namespace NetSquare.Server.Server
{
    /// <summary>
    /// Represents the server statistics value.
    /// </summary>
    public struct ServerStatistics
    {
        /// <summary>
        /// Stores the nb listeners value.
        /// </summary>
        public int NbListeners;
        /// <summary>
        /// Stores the nb clients connected value.
        /// </summary>
        public int NbClientsConnected;
        /// <summary>
        /// Stores the nb processing messages value.
        /// </summary>
        public int NbProcessingMessages;
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
        /// Stores the nb messages dropped value.
        /// </summary>
        public long NbMessagesDropped;
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
        /// Executes the to string operation.
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("IPs : ").Append(NbListeners)
                .Append(" | Clients: ").Append(NbClientsConnected)

                .Append(" - Down : ")
                .Append(Downloading.ToString("f2")).Append(" ko/s | ")
                .Append(NbMessagesReceiving).Append(" msg/s | (")
                .Append(NbMessagesReceived).Append(" msg)")

                .Append(" - Up : ")
                .Append(Uploading.ToString("f2")).Append(" ko/s | ")
                .Append(NbMessagesSending).Append(" msg/s | (")
                .Append(NbMessagesSended).Append(" msg)")

                .Append(" - Processing : ").Append(NbProcessingMessages)
                .Append(" - ToSend : ").Append(NbMessagesToSend)
                .Append(" - Dropped : ").Append(NbMessagesDropped);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents the server statistics manager component.
    /// </summary>
    public class ServerStatisticsManager
    {
        /// <summary>
        /// Stores the server value.
        /// </summary>
        private NetSquareServer server;
        /// <summary>
        /// Stores the stop order value.
        /// </summary>
        private bool stopOrder = false;
        /// <summary>
        /// Occurs when get statistics is raised.
        /// </summary>
        public event Action<ServerStatistics> OnGetStatistics;
        /// <summary>
        /// Gets or sets the running value.
        /// </summary>
        public bool Running { get; private set; }
        /// <summary>
        /// Gets or sets the current statistics value.
        /// </summary>
        public ServerStatistics CurrentStatistics { get; private set; }
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
                if (intervalMs < 10)
                    intervalMs = 10;
                if (intervalMs > 1000)
                    intervalMs = 1000;
            }
        }

        /// <summary>
        /// Get NetSquare server statistics
        /// </summary>
        /// <param name="_server">Server instance to get statistics on</param>
        /// <param name="intervalMs">intervals (in ms) for getting statistics</param>
        public void StartReceivingStatistics(NetSquareServer _server)
        {
            server = _server;
            stopOrder = false;
            Running = true;
            Thread statisticsThread = new Thread(() => { GetStatisticsLoop(); });
            statisticsThread.IsBackground = true;
            statisticsThread.Start();
        }

        /// <summary>
        /// Stop the statictics process
        /// </summary>
        public void Stop()
        {
            stopOrder = true;
        }

        /// <summary>
        /// Executes the get statistics loop operation.
        /// </summary>
        private void GetStatisticsLoop()
        {
            while (!stopOrder)
            {
                int toSend = 0;
                long sended = 0;
                long received = 0;
                long dropped = 0;
                long bytesSended = 0;
                long bytesReceived = 0;
                foreach (var client in server.Clients)
                {
                    toSend += client.Value.NbMessagesToSend;
                    sended += client.Value.NbMessagesSended;
                    received += client.Value.NbMessagesReceived;
                    dropped += client.Value.NbMessagesDropped;
                    bytesSended += client.Value.SendedBytes;
                    bytesReceived += client.Value.ReceivedBytes;
                    client.Value.SendedBytes = 0;
                    client.Value.ReceivedBytes = 0;
                }

                long receivedThisTick = received - lastProcessReceived;
                lastProcessReceived = received;
                if (receivedThisTick < 0)
                    receivedThisTick = 0;
                long sendedThisTick = sended - lastProcessSended;
                lastProcessSended = sended;
                if (sendedThisTick < 0)
                    sendedThisTick = 0;

                int nbMessages = 0;
                foreach (var queue in server.MessageQueueManager.Queues)
                    nbMessages += queue.NbMessages;
                //sended += server.UdpListener.NbMessageSended;
                //received += server.UdpListener.NbMessageReceived;

                CurrentStatistics = new ServerStatistics()
                {
                    NbClientsConnected = server.Clients.Count,
                    NbListeners = server.Listeners.Count,
                    NbProcessingMessages = nbMessages,
                    NbMessagesToSend = toSend,
                    NbMessagesSended = sended,
                    NbMessagesReceived = received,
                    NbMessagesDropped = dropped,
                    Downloading = (float)bytesReceived / 1024f * ((1f / (float)intervalMs) * 1000f),
                    Uploading = (float)bytesSended / 1024f * ((1f / (float)intervalMs) * 1000f),
                    NbMessagesReceiving = (int)((float)receivedThisTick * ((1f / (float)intervalMs) * 1000f)),
                    NbMessagesSending = (int)((float)sendedThisTick * ((1f / (float)intervalMs) * 1000f)),
                };
                OnGetStatistics?.Invoke(CurrentStatistics);
                Thread.Sleep(intervalMs);
            }
            Running = false;
            stopOrder = false;
        }
    }
}
#endregion
