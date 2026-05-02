using NetSquare.Core;
using NetSquare.Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Threading;

#region Source
namespace NetSquare.Server.Server
{
    /// <summary>
    /// Represents the message queue component.
    /// </summary>
    public class MessageQueue
    {
        /// <summary>
        /// Stores the queue value.
        /// </summary>
        public ConcurrentQueue<NetworkMessage> Queue = new ConcurrentQueue<NetworkMessage>();
        /// <summary>
        /// Gets or sets the queue id value.
        /// </summary>
        public int QueueID { get; private set; }
        /// <summary>
        /// Gets or sets the started value.
        /// </summary>
        public bool Started { get; private set; }
        /// <summary>
        /// Gets or sets the nb messages value.
        /// </summary>
        public int NbMessages { get { return Queue.Count; } }
        /// <summary>
        /// Gets or sets the process queue thread value.
        /// </summary>
        public Thread ProcessQueueThread { get; private set; }
        /// <summary>
        /// Stores the server value.
        /// </summary>
        private NetSquareServer server;
        /// <summary>
        /// Stores the current message value.
        /// </summary>
        private NetworkMessage currentMessage = null;
        /// <summary>
        /// Stores the queue signal value.
        /// </summary>
        private SemaphoreSlim queueSignal = new SemaphoreSlim(0);

        /// <summary>
        /// Initializes a new instance of the message queue class.
        /// </summary>
        public MessageQueue(int queueID, NetSquareServer _server)
        {
            server = _server;
            Started = false;
            QueueID = queueID;
        }

        /// <summary>
        /// Executes the add message operation.
        /// </summary>
        public void AddMessage(NetworkMessage msg)
        {
            Queue.Enqueue(msg);
            queueSignal.Release();
        }

        /// <summary>
        /// Executes the stop queue operation.
        /// </summary>
        public void StopQueue()
        {
            Started = false;
            queueSignal.Release();
        }

        /// <summary>
        /// Executes the clear queue operation.
        /// </summary>
        public void ClearQueue()
        {
            Queue = new ConcurrentQueue<NetworkMessage>();
            queueSignal = new SemaphoreSlim(0);
        }

        /// <summary>
        /// Executes the start queue operation.
        /// </summary>
        public void StartQueue()
        {
            Started = true;
            ProcessQueueThread = new Thread(() =>
            {
                while (Started)
                {
                    try
                    {
                        queueSignal.Wait(100);
                        while (Queue.TryDequeue(out currentMessage))
                        {
                            if (currentMessage == null)
                            {
                                // don't worry, it's surely due to a client disconnection
                                Writer.Write("NullQueuedMessageException on queue " + QueueID, ConsoleColor.DarkYellow);
                                continue;
                            }

                            if (currentMessage.Client == null || !currentMessage.Client.TcpSocket.Connected)
                            {
                                // don't worry, it's surely due to a client disconnection
                                Writer.Write("NullOrDisconectedClientQueuedMessageException on queue" + QueueID, ConsoleColor.DarkYellow);
                                continue;
                            }

                            switch ((NetSquareMessageType)currentMessage.MsgType)
                            {
                                // It's a default message (may be a Reply, we don't want to handle this on server side, it will be handled on client side), we need to dispatch it to the right action
                                default:
                                case NetSquareMessageType.Default:
                                    if (!server.Dispatcher.DispatchMessage(currentMessage))
                                    {
                                        Writer.Write("Trying to Process message with head '" + currentMessage.HeadID.ToString() + "' but no action related... Message skipped.", ConsoleColor.DarkMagenta);
                                        server.Reply(currentMessage, new NetworkMessage(0, currentMessage.ClientID).Set(false));
                                    }
                                    break;

                                // It's a broadcast message, we need to broadcast it to all clients in the current world
                                case NetSquareMessageType.BroadcastCurrentWorld:
                                    server.Worlds.BroadcastToWorld(currentMessage);
                                    break;

                                // It's a synchronization message, we need to synchronize it to all clients in the current world
                                case NetSquareMessageType.SynchronizeMessageCurrentWorld:
                                    server.Worlds.ReceiveSyncronizationMessage(currentMessage);
                                    break;
                            }
                            currentMessage = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Writer.Write("Receiving Queue fail : \n\r" + ex.ToString(), ConsoleColor.Red);
                    }
                }
            });
            ProcessQueueThread.IsBackground = true;
            ProcessQueueThread.Start();
        }
    }

    /// <summary>
    /// Represents the message queue manager component.
    /// </summary>
    public class MessageQueueManager
    {
        /// <summary>
        /// Stores the queues value.
        /// </summary>
        public MessageQueue[] Queues;
        /// <summary>
        /// Stores the server value.
        /// </summary>
        private NetSquareServer server;
        /// <summary>
        /// Gets or sets the nb queues value.
        /// </summary>
        public int NbQueues { get; private set; }
        /// <summary>
        /// Gets or sets the queues started value.
        /// </summary>
        public bool QueuesStarted { get; private set; }
        /// <summary>
        /// Gets or sets the emptiest queue id value.
        /// </summary>
        public int EmptiestQueueID { get; private set; }

        /// <summary>
        /// Executes the message queue manager operation.
        /// </summary>
        public MessageQueueManager(NetSquareServer _server, int nbQueues)
        {
            server = _server;
            NbQueues = Math.Max(1, nbQueues);
            Queues = new MessageQueue[NbQueues];
            for (int i = 0; i < NbQueues; i++)
                Queues[i] = new MessageQueue(i, server);
        }

        /// <summary>
        /// Executes the start queues operation.
        /// </summary>
        public void StartQueues()
        {
            QueuesStarted = true;
            EmptiestQueueID = 0;
            foreach (MessageQueue queue in Queues)
            {
                queue.ClearQueue();
                queue.StartQueue();
            }
        }

        /// <summary>
        /// Executes the stop queues operation.
        /// </summary>
        public void StopQueues()
        {
            QueuesStarted = false;
            foreach (MessageQueue queue in Queues)
            {
                queue.StopQueue();
                queue.ClearQueue();
            }
            EmptiestQueueID = 0;
        }

        /// <summary>
        /// Executes the message received operation.
        /// </summary>
        public void MessageReceived(NetworkMessage message)
        {
            int queueID = (int)(message.ClientID % (uint)NbQueues);
            EmptiestQueueID = queueID;
            Queues[queueID].AddMessage(message);
        }
    }
}
#endregion
