using NetSquare.Server.Utils;
using System;
using System.Text;
using System.Threading;

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
    /// Periodically captures aggregate server network statistics.
    /// </summary>
    public class ServerStatisticsManager
    {
        /// <summary>
        /// Stores the server value.
        /// </summary>
        private NetSquareServer server;
        /// <summary>
        /// Coordinates statistics worker lifecycle transitions.
        /// </summary>
        private readonly object lifecycleLock = new object();
        /// <summary>
        /// Cancels the active statistics worker.
        /// </summary>
        private CancellationTokenSource stopCancellation;
        /// <summary>
        /// Stores the active statistics worker thread.
        /// </summary>
        private Thread statisticsThread;
        /// <summary>
        /// Stores the stop order value.
        /// </summary>
        private int running;
        /// <summary>
        /// Occurs when get statistics is raised.
        /// </summary>
        public event Action<ServerStatistics> OnGetStatistics;
        /// <summary>
        /// Gets or sets the running value.
        /// </summary>
        public bool Running { get { return Volatile.Read(ref running) != 0; } }
        /// <summary>
        /// Gets whether the statistics worker thread has fully terminated.
        /// </summary>
        internal bool HasLiveWorker
        {
            get
            {
                lock (lifecycleLock)
                    return statisticsThread != null && statisticsThread.IsAlive;
            }
        }
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
            if (_server == null)
                throw new ArgumentNullException(nameof(_server));

            lock (lifecycleLock)
            {
                if (Running)
                    return;

                server = _server;
                lastProcessReceived = 0;
                lastProcessSended = 0;
                stopCancellation = new CancellationTokenSource();
                CancellationToken workerToken = stopCancellation.Token;
                statisticsThread = new Thread(() => GetStatisticsLoop(workerToken));
                statisticsThread.IsBackground = true;
                statisticsThread.Name = "NetSquare server statistics";
                Volatile.Write(ref running, 1);
                statisticsThread.Start();
            }
        }

        /// <summary>
        /// Stop the statictics process
        /// </summary>
        public void Stop()
        {
            Thread threadToJoin;
            lock (lifecycleLock)
            {
                Volatile.Write(ref running, 0);
                stopCancellation?.Cancel();
                threadToJoin = statisticsThread;
            }

            if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
            {
                int configuredTimeout = NetSquareConfigurationManager
                    .Get<NetSquareConfiguration>().WorkerStopTimeoutMilliseconds;
                int timeout = configuredTimeout > 0 ? configuredTimeout : 5000;
                if (!threadToJoin.Join(timeout))
                    throw new TimeoutException("The server statistics worker did not stop in time.");
            }
        }

        /// <summary>
        /// Executes the get statistics loop operation.
        /// </summary>
        private void GetStatisticsLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
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
                        bytesSended += client.Value.TakeSendedBytes();
                        bytesReceived += client.Value.TakeReceivedBytes();
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
                    foreach (MessageQueue queue in server.MessageQueueManager.Queues)
                        nbMessages += queue.NbMessages;

                    CurrentStatistics = new ServerStatistics
                    {
                        NbClientsConnected = server.Clients.Count,
                        NbListeners = server.Listeners.Count,
                        NbProcessingMessages = nbMessages,
                        NbMessagesToSend = toSend,
                        NbMessagesSended = sended,
                        NbMessagesReceived = received,
                        NbMessagesDropped = dropped,
                        Downloading = (float)bytesReceived / 1024f * (1000f / intervalMs),
                        Uploading = (float)bytesSended / 1024f * (1000f / intervalMs),
                        NbMessagesReceiving = (int)(receivedThisTick * (1000f / intervalMs)),
                        NbMessagesSending = (int)(sendedThisTick * (1000f / intervalMs))
                    };

                    try { OnGetStatistics?.Invoke(CurrentStatistics); }
                    catch (Exception ex) { Writer.Write("Statistics callback failed: " + ex, ConsoleColor.DarkYellow); }
                    if (cancellationToken.WaitHandle.WaitOne(IntervalMs))
                        return;
                }
            }
            finally
            {
                CancellationTokenSource cancellationToDispose = null;
                Volatile.Write(ref running, 0);
                lock (lifecycleLock)
                {
                    if (statisticsThread == Thread.CurrentThread)
                    {
                        statisticsThread = null;
                        cancellationToDispose = stopCancellation;
                        stopCancellation = null;
                    }
                }
                cancellationToDispose?.Dispose();
            }
        }
    }
}