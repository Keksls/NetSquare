﻿using NetSquare.Core;
using NetSquareServer.Worlds;
using NetSquareServer.Utils;
using System;
using System.Collections.Concurrent;
using System.Threading;
using NetSquare.Core.Messages;
using System.Windows.Forms;

namespace NetSquareServer.Server
{
    public class MessageQueue
    {
        public ConcurrentQueue<NetworkMessage> Queue = new ConcurrentQueue<NetworkMessage>();
        public int QueueID { get; private set; }
        public bool Started { get; private set; }
        public int NbMessages { get { return Queue.Count; } }
        public Thread ProcessQueueThread { get; private set; }
        private NetSquare_Server server;
        private NetworkMessage currentMessage = null;

        public MessageQueue(int queueID, NetSquare_Server _server)
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
                                Writer.Write("NullQueuedMessageException on queue " + QueueID, ConsoleColor.Red);
                                continue;
                            }

                            if (currentMessage.Client == null || !currentMessage.Client.TcpSocket.Connected)
                            {
                                Writer.Write("NullOrDisconectedClientQueuedMessageException on queue" + QueueID, ConsoleColor.Red);
                                continue;
                            }

                            // BroadcastMessage
                            if (currentMessage.TypeID == 1)
                                server.Worlds.BroadcastToWorld(currentMessage);
                            else if(currentMessage.TypeID == 2 && currentMessage.HeadID != (ushort)NetSquareMessageType.ClientSetPosition)
                                server.Worlds.ReceiveSyncronizationMessage(currentMessage);
                            else if (!server.Dispatcher.DispatchMessage(currentMessage))
                            {
                                Writer.Write("Trying to Process message with head '" + currentMessage.HeadID.ToString() + "' but no action related... Message skipped.", ConsoleColor.DarkMagenta);
                                server.Reply(currentMessage, new NetworkMessage(0, currentMessage.ClientID).Set(false));
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
        private NetSquare_Server server;
        public int NbQueues { get; private set; }
        public bool QueuesStarted { get; private set; }
        public int EmptiestQueueID { get; private set; }

        public MessageQueueManager(NetSquare_Server _server, int nbQueues)
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