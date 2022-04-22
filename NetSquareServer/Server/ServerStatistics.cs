using System;
using System.Text;
using System.Threading;

namespace NetSquareServer.Server
{
    public struct ServerStatistics
    {
        public int NbListeners;
        public int NbClientsConnected;
        public int NbProcessingMessages;
        public int NbMessagesToSend;
        public long NbMessagesSended;
        public long NbMessagesReceived;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("-Listeners : ").Append(NbListeners)
                .Append(" - Clients : ").Append(NbClientsConnected)
                .Append(" - Received : ").Append(NbMessagesReceived)
                .Append(" - Processing : ").Append(NbProcessingMessages)
                .Append(" - ToSend : ").Append(NbMessagesToSend)
                .Append(" - Sended : ").Append(NbMessagesSended);
            return sb.ToString();
        }
    }

    public class ServerStatisticsManager
    {
        private NetSquare_Server server;
        private bool stopOrder = false;
        public event Action<ServerStatistics> OnGetStatistics;
        public bool Running { get; private set; }
        public ServerStatistics CurrentStatistics { get; private set; }

        /// <summary>
        /// Get NetSquare server statistics
        /// </summary>
        /// <param name="_server">Server instance to get statistics on</param>
        /// <param name="intervalMs">intervals (in ms) for getting statistics</param>
        public void StartReceivingStatistics(NetSquare_Server _server, int intervalMs)
        {
            server = _server;
            stopOrder = false;
            Running = true;
            Thread statisticsThread = new Thread(() => { GetStatisticsLoop(intervalMs); });
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

        private void GetStatisticsLoop(int intervalMs)
        {
            while (!stopOrder)
            {
                int toSend = 0;
                long sended = 0;
                long received = 0;
                foreach (var client in server.Clients)
                {
                    toSend += client.Value.NbMessagesToSend;
                    sended += client.Value.NbMessagesSended;
                    received += client.Value.NbMessagesReceived;
                }
                //sended += server.UdpListener.NbMessageSended;
                //received += server.UdpListener.NbMessageReceived;

                CurrentStatistics = new ServerStatistics()
                {
                    NbClientsConnected = server.Clients.Count,
                    NbListeners = server.Listeners.Count,
                    NbProcessingMessages = server.MessageQueueManager.NbMessages,
                    NbMessagesToSend = toSend,
                    NbMessagesSended = sended,
                    NbMessagesReceived = received
                };
                OnGetStatistics?.Invoke(CurrentStatistics);
                Thread.Sleep(intervalMs);
            }
            Running = false;
            stopOrder = false;
        }
    }
}