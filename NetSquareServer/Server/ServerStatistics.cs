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
        public int NbSendingThreads;
        public long NbMessagesSended;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("-Listeners : ").Append(NbListeners)
                .Append(" - Clients : ").Append(NbClientsConnected)
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
                CurrentStatistics = new ServerStatistics()
                {
                    NbClientsConnected = server.Clients.Count,
                    NbListeners = server.Listeners.Count,
                    NbMessagesSended = server.MessageSenderManager.NbSended,
                    NbMessagesToSend = server.MessageSenderManager.GetNbMessagesToSend(),
                    NbProcessingMessages = server.MessageQueueManager.NbMessages,
                    NbSendingThreads = server.MessageSenderManager.NbSenders
                };
                OnGetStatistics?.Invoke(CurrentStatistics);
                Thread.Sleep(intervalMs);
            }
            Running = false;
            stopOrder = false;
        }
    }
}