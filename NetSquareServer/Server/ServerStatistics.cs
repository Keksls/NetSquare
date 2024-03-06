using System;
using System.Text;
using System.Threading;

namespace NetSquare.Server.Server
{
    public struct ServerStatistics
    {
        public int NbListeners;
        public int NbClientsConnected;
        public int NbProcessingMessages;
        public int NbMessagesToSend;
        public long NbMessagesSended;
        public long NbMessagesReceived;
        public float Downloading;
        public float Uploading;
        public int NbMessagesSending;
        public int NbMessagesReceiving;

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
                .Append(" - ToSend : ").Append(NbMessagesToSend);
            return sb.ToString();
        }
    }

    public class ServerStatisticsManager
    {
        private NetSquareServer server;
        private bool stopOrder = false;
        public event Action<ServerStatistics> OnGetStatistics;
        public bool Running { get; private set; }
        public ServerStatistics CurrentStatistics { get; private set; }
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

        private void GetStatisticsLoop()
        {
            while (!stopOrder)
            {
                int toSend = 0;
                long sended = 0;
                long received = 0;
                long bytesSended = 0;
                long bytesReceived = 0;
                foreach (var client in server.Clients)
                {
                    toSend += client.Value.NbMessagesToSend;
                    sended += client.Value.NbMessagesSended;
                    received += client.Value.NbMessagesReceived;
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