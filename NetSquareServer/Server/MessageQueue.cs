using NetSquare.Core;
using NetSquare.Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NetSquare.Server.Server
{
    public class MessageQueue
    {
        public ConcurrentQueue<NetworkMessage> Queue = new ConcurrentQueue<NetworkMessage>();
        public int QueueID { get; private set; }
        public bool Started { get; private set; }
        public int NbMessages { get { return Queue.Count; } }
        public Thread ProcessQueueThread { get; private set; }
        private NetSquareServer server;
        private NetworkMessage currentMessage = null;

        public MessageQueue(int queueID, NetSquareServer _server)
        {
            server = _server;
            Started = false;
            QueueID = queueID;
        }

        public void AddMessage(NetworkMessage msg)
        {
            Queue.Enqueue(msg);
        }

        public void StopQueue()
        {
            Started = false;
        }

        public void ClearQueue()
        {
            Queue = new ConcurrentQueue<NetworkMessage>();
        }

        public void StartQueue()
        {
            Started = true;
            ProcessQueueThread = new Thread(() =>
            {
                while (Started)
                {
                    try
                    {
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
                        Thread.Sleep(1);
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

    public class MessageQueueManager
    {
        public MessageQueue[] Queues;
        private NetSquareServer server;
        public int NbQueues { get; private set; }
        public bool QueuesStarted { get; private set; }
        public int EmptiestQueueID { get; private set; }

        public MessageQueueManager(NetSquareServer _server, int nbQueues)
        {
            server = _server;
            NbQueues = nbQueues;
            Queues = new MessageQueue[nbQueues];
            for (int i = 0; i < nbQueues; i++)
                Queues[i] = new MessageQueue(i, server);
        }

        public void StartQueues()
        {
            QueuesStarted = true;
            EmptiestQueueID = 0;
            min = int.MaxValue;
            Thread processEmptiestQueueIDThread = new Thread(ProcessEmptiestQueueID);
            processEmptiestQueueIDThread.Start();
            foreach (MessageQueue queue in Queues)
            {
                queue.ClearQueue();
                queue.StartQueue();
            }
        }

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

        public void MessageReceived(NetworkMessage message)
        {
            Queues[EmptiestQueueID].AddMessage(message);
        }

        int min;
        private void ProcessEmptiestQueueID()
        {
            while (QueuesStarted)
            {
                min = int.MaxValue;
                for (int i = 0; i < NbQueues; i++)
                {
                    if (Queues[i].NbMessages < min)
                    {
                        min = Queues[i].NbMessages;
                        EmptiestQueueID = i;
                    }
                }
                Thread.Sleep(1);
            }
        }
    }
}