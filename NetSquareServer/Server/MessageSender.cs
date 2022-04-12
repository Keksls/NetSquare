using NetSquare.Core;
using NetSquareServer.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareServer.Server
{
    public struct SendingMessage
    {
        public byte[] NetworkMessage { get; set; }
        public ConnectedClient Client { get; set; }

        public SendingMessage(byte[] message, ConnectedClient client)
        {
            Client = client;
            NetworkMessage = message;
        }
    }

    public class MessageSender
    {
        public ConcurrentQueue<SendingMessage> Queue = new ConcurrentQueue<SendingMessage>();
        public int SenderID { get; private set; }
        public bool Started { get; private set; }
        public int NbMessages { get { return Queue.Count; } }
        public Thread ProcessQueueThread { get; private set; }
        private readonly object sendSyncRoot = new object();
        private SendingMessage currentMessage;

        public MessageSender(int senderID)
        {
            Started = false;
            SenderID = senderID;
        }

        public void AddMessage(SendingMessage msg)
        {
            Queue.Enqueue(msg);
        }

        public void StopSender()
        {
            Started = false;
        }

        public void ClearQueue()
        {
            Queue = new ConcurrentQueue<SendingMessage>();
        }

        public void StartSender()
        {
            Started = true;
            ProcessQueueThread = new Thread(ProcessSendQueue);
            ProcessQueueThread.IsBackground = true;
            ProcessQueueThread.Start();
        }

        private void ProcessSendQueue()
        {
            while (Started)
            {
                while (Queue.TryDequeue(out currentMessage))
                {
                    try
                    {
                        if (currentMessage.Client != null && currentMessage.Client.Socket.Connected)
                        {
                            lock (currentMessage.Client.sendSyncRoot)
                                currentMessage.Client.Socket.Send(currentMessage.NetworkMessage);
                        }
                    }
                    catch (SocketException)
                    {
                        Writer.Write("Sending Queue switch disconnected client", ConsoleColor.Red);
                    }
                    catch (Exception ex)
                    {
                        Writer.Write("Sending Queue fail : \n\r" + ex.ToString(), ConsoleColor.Red);
                    }
                }
                Thread.Sleep(1);
            }
            Writer.Write("End of Sending Queue  " + SenderID, ConsoleColor.Red);
        }
    }

    public class MessageSenderManager
    {
        private MessageSender[] senders;
        public int NbSenders { get; private set; }
        public bool SendersStarted { get; private set; }
        public int EmptiestSenderID { get; private set; }
        public int NbMessages { get; private set; }

        public MessageSenderManager(int nbSenders)
        {
            NbSenders = nbSenders;
            senders = new MessageSender[nbSenders];
            for (int i = 0; i < nbSenders; i++)
                senders[i] = new MessageSender(i);
        }

        public void StartSenders()
        {
            SendersStarted = true;
            EmptiestSenderID = 0;
            min = int.MaxValue;
            Thread processEmptiestQueueIDThread = new Thread(ProcessEmptiestSenderID);
            processEmptiestQueueIDThread.Start();
            foreach (MessageSender sender in senders)
            {
                sender.ClearQueue();
                sender.StartSender();
            }
        }

        public void StopSenders()
        {
            SendersStarted = false;
            foreach (MessageSender sender in senders)
            {
                sender.StopSender();
                sender.ClearQueue();
            }
            EmptiestSenderID = 0;
        }

        public void SendMessage(byte[] message, ConnectedClient client)
        {
            senders[EmptiestSenderID].AddMessage(new SendingMessage(message, client));
        }

        public void SendMessage(byte[] message, List<ConnectedClient> clients)
        {
            foreach (ConnectedClient client in clients)
                senders[EmptiestSenderID].AddMessage(new SendingMessage(message, client));
        }

        int min;
        private void ProcessEmptiestSenderID()
        {
            while (SendersStarted)
            {
                NbMessages = 0;
                min = int.MaxValue;
                for (int i = 0; i < NbSenders; i++)
                {
                    NbMessages += senders[i].NbMessages;
                    if (senders[i].NbMessages < min)
                    {
                        min = senders[i].NbMessages;
                        EmptiestSenderID = i;
                    }
                }
                Thread.Sleep(1);
            }
        }
    }
}