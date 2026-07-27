using NetSquare.Core;
using NetSquare.Core.Collections;
using NetSquare.Server.Utils;
using System;
using System.Threading;

#region Source
namespace NetSquare.Server.Server
{
    /// <summary>
    /// Processes one bounded partition of received messages.
    /// </summary>
    public class MessageQueue
    {
        #region Fields
        private readonly object lifecycleLock = new object();
        private readonly NetSquareServer server;
        private readonly int queueCapacity;
        private BoundedConcurrentQueue<NetworkMessage> queue;
        private CancellationTokenSource workerCancellation;
        private int started;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the queue identifier.
        /// </summary>
        public int QueueID { get; private set; }

        /// <summary>
        /// Gets whether the worker currently accepts messages.
        /// </summary>
        public bool Started { get { return Volatile.Read(ref started) != 0; } }

        /// <summary>
        /// Gets the number of messages waiting to be processed.
        /// </summary>
        public int NbMessages
        {
            get
            {
                BoundedConcurrentQueue<NetworkMessage> currentQueue = queue;
                return currentQueue == null ? 0 : currentQueue.Count;
            }
        }

        /// <summary>
        /// Gets the processing thread for diagnostics.
        /// </summary>
        public Thread ProcessQueueThread { get; private set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a bounded message queue worker.
        /// </summary>
        /// <param name="queueID">Stable queue partition identifier.</param>
        /// <param name="server">Owning server.</param>
        /// <param name="capacity">Maximum retained messages.</param>
        public MessageQueue(int queueID, NetSquareServer server, int capacity)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            this.server = server;
            queueCapacity = capacity;
            QueueID = queueID;
        }
        #endregion

        #region Lifecycle
        /// <summary>
        /// Starts a fresh worker and queue.
        /// </summary>
        /// <returns>True when started, or false when already running.</returns>
        public bool StartQueue()
        {
            lock (lifecycleLock)
            {
                if (Started)
                    return false;

                queue = new BoundedConcurrentQueue<NetworkMessage>(queueCapacity);
                workerCancellation = new CancellationTokenSource();
                BoundedConcurrentQueue<NetworkMessage> workerQueue = queue;
                CancellationToken workerToken = workerCancellation.Token;
                ProcessQueueThread = new Thread(() => ProcessQueueLoop(workerQueue, workerToken));
                ProcessQueueThread.IsBackground = true;
                ProcessQueueThread.Name = "NetSquare message queue " + QueueID;
                Volatile.Write(ref started, 1);
                ProcessQueueThread.Start();
                return true;
            }
        }

        /// <summary>
        /// Stops producers, drains accepted messages and waits for the worker.
        /// </summary>
        /// <param name="timeoutMilliseconds">Maximum graceful drain duration.</param>
        /// <returns>True when the worker terminated before the timeout.</returns>
        public bool StopQueue(int timeoutMilliseconds)
        {
            Thread threadToJoin;
            BoundedConcurrentQueue<NetworkMessage> queueToComplete;
            CancellationTokenSource cancellation;

            lock (lifecycleLock)
            {
                queueToComplete = queue;
                threadToJoin = ProcessQueueThread;
                cancellation = workerCancellation;
                Volatile.Write(ref started, 0);
                queueToComplete?.CompleteAdding();
            }

            if (threadToJoin == null)
                return true;
            if (threadToJoin == Thread.CurrentThread)
                return false;

            int timeout = Math.Max(1, timeoutMilliseconds);
            if (threadToJoin.Join(timeout))
                return true;

            // Forced cancellation only applies after the graceful drain budget is exhausted.
            cancellation?.Cancel();
            return threadToJoin.Join(Math.Min(250, timeout));
        }
        #endregion

        #region Queue processing
        /// <summary>
        /// Enqueues a received message and blocks only while the bounded queue is full.
        /// </summary>
        /// <param name="message">Received message.</param>
        /// <returns>True when accepted, or false while stopping.</returns>
        public bool AddMessage(NetworkMessage message)
        {
            if (message == null || !Started)
                return false;

            BoundedConcurrentQueue<NetworkMessage> currentQueue = queue;
            return currentQueue != null && currentQueue.Enqueue(message);
        }

        /// <summary>
        /// Drains one queue until adding completes or forced cancellation is requested.
        /// </summary>
        /// <param name="workerQueue">Queue instance owned by this worker generation.</param>
        /// <param name="cancellationToken">Forced-stop token.</param>
        private void ProcessQueueLoop(
            BoundedConcurrentQueue<NetworkMessage> workerQueue,
            CancellationToken cancellationToken)
        {
            try
            {
                NetworkMessage message;
                while (workerQueue.TryDequeue(out message, cancellationToken))
                {
                    try
                    {
                        ProcessMessage(message);
                    }
                    catch (Exception ex)
                    {
                        // A faulty user handler must not terminate the queue worker.
                        Writer.Write("Receiving queue fail : \n\r" + ex, ConsoleColor.Red);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Forced cancellation is expected only when graceful draining timed out.
            }
            finally
            {
                // Release only the generation owned by this worker before a later restart.
                CancellationTokenSource cancellationToDispose = null;
                Volatile.Write(ref started, 0);
                lock (lifecycleLock)
                {
                    if (ProcessQueueThread == Thread.CurrentThread)
                    {
                        ProcessQueueThread = null;
                        cancellationToDispose = workerCancellation;
                        workerCancellation = null;
                    }
                }
                cancellationToDispose?.Dispose();
            }
        }

        /// <summary>
        /// Dispatches one validated message according to its transport-level type.
        /// </summary>
        /// <param name="message">Message to process.</param>
        private void ProcessMessage(NetworkMessage message)
        {
            if (message == null)
            {
                Writer.Write("Null queued message on queue " + QueueID, ConsoleColor.DarkYellow);
                return;
            }

            if (message.Client == null || !message.Client.IsConnected())
            {
                Writer.Write("Disconnected client message on queue " + QueueID, ConsoleColor.DarkYellow);
                return;
            }

            switch ((NetSquareMessageType)message.MsgType)
            {
                default:
                case NetSquareMessageType.Default:
                    if (!server.Dispatcher.DispatchMessage(message))
                    {
                        Writer.Write(
                            "No action registered for head '" + message.HeadID + "'. Message skipped.",
                            ConsoleColor.DarkMagenta);
                        server.Reply(message, new NetworkMessage(0, message.ClientID).Set(false));
                    }
                    break;

                case NetSquareMessageType.BroadcastCurrentWorld:
                    server.Worlds.BroadcastToWorld(message);
                    break;

                case NetSquareMessageType.BroadcastCurrentWorldUnreliable:
                    server.Worlds.BroadcastToWorldUnreliable(message);
                    break;

                case NetSquareMessageType.SynchronizeMessageCurrentWorld:
                    server.Worlds.ReceiveSyncronizationMessage(message);
                    break;
            }
        }
        #endregion
    }

    /// <summary>
    /// Routes each client to a stable bounded message-processing partition.
    /// </summary>
    public class MessageQueueManager
    {
        #region Fields
        private readonly int workerStopTimeoutMilliseconds;
        #endregion

        #region Properties
        /// <summary>
        /// Gets all processing queues.
        /// </summary>
        public MessageQueue[] Queues { get; private set; }

        /// <summary>
        /// Gets the number of queue partitions.
        /// </summary>
        public int NbQueues { get; private set; }

        /// <summary>
        /// Gets whether queue workers accept messages.
        /// </summary>
        public bool QueuesStarted { get; private set; }

        /// <summary>
        /// Gets the partition selected for the latest message.
        /// </summary>
        public int EmptiestQueueID { get; private set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes the message queue manager.
        /// </summary>
        /// <param name="server">Owning server.</param>
        /// <param name="nbQueues">Number of stable client partitions.</param>
        /// <param name="queueCapacity">Capacity of each partition.</param>
        /// <param name="workerStopTimeoutMilliseconds">Graceful worker drain timeout.</param>
        public MessageQueueManager(
            NetSquareServer server,
            int nbQueues,
            int queueCapacity,
            int workerStopTimeoutMilliseconds)
        {
            if (server == null)
                throw new ArgumentNullException(nameof(server));

            NbQueues = Math.Max(1, nbQueues);
            this.workerStopTimeoutMilliseconds = Math.Max(1, workerStopTimeoutMilliseconds);
            int capacity = Math.Max(1, queueCapacity);
            Queues = new MessageQueue[NbQueues];
            for (int i = 0; i < NbQueues; i++)
                Queues[i] = new MessageQueue(i, server, capacity);
        }
        #endregion

        #region Lifecycle
        /// <summary>
        /// Starts all bounded processing queues.
        /// </summary>
        public void StartQueues()
        {
            if (QueuesStarted)
                return;

            foreach (MessageQueue queue in Queues)
                queue.StartQueue();

            EmptiestQueueID = 0;
            QueuesStarted = true;
        }

        /// <summary>
        /// Completes and waits for every processing queue.
        /// </summary>
        /// <returns>True when every worker stopped within its timeout.</returns>
        public bool StopQueues()
        {
            QueuesStarted = false;
            bool allStopped = true;
            foreach (MessageQueue queue in Queues)
                allStopped &= queue.StopQueue(workerStopTimeoutMilliseconds);

            EmptiestQueueID = 0;
            return allStopped;
        }
        #endregion

        #region Routing
        /// <summary>
        /// Routes a message to the stable partition assigned to its authenticated client.
        /// </summary>
        /// <param name="message">Received message.</param>
        /// <returns>True when accepted, or false during shutdown.</returns>
        public bool MessageReceived(NetworkMessage message)
        {
            if (message == null || !QueuesStarted)
                return false;

            int queueID = (int)(message.ClientID % (uint)NbQueues);
            EmptiestQueueID = queueID;
            return Queues[queueID].AddMessage(message);
        }
        #endregion
    }
}
#endregion
