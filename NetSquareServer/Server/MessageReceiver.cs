using NetSquare.Core;
using NetSquareServer.Utils;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareServer.Server
{
    public class MessageReceiver
    {
        public List<ConnectedClient> Clients = new List<ConnectedClient>();
        public int ReceiverID { get; private set; }
        public bool Started { get; private set; }
        public int NbClients { get { return Clients.Count; } }
        public Thread ReceiverThread { get; private set; }
        public int BufferSize { get; private set; }
        private int currentLenght = -1;
        private NetSquare_Server server;

        public MessageReceiver(NetSquare_Server _server, int receiverID, int bufferSize)
        {
            server = _server;
            BufferSize = bufferSize;
            Started = false;
            ReceiverID = receiverID;
        }

        public void AddClient(ConnectedClient client)
        {
            Clients.Add(client);
        }

        public void StopReceiver()
        {
            Started = false;
        }

        public void ClearQueue()
        {
            Clients = new List<ConnectedClient>();
        }

        public void StartReceiver()
        {
            Started = true;
            ReceiverThread = new Thread(ReceiveClientMessages);
            ReceiverThread.IsBackground = true;
            ReceiverThread.Start();
        }

        private void ReceiveClientMessages()
        {
            while (Started)
            {
                try
                {
                    byte[] bytesReceived = new byte[0];
                    for (int clientIndex = 0; clientIndex < Clients.Count; clientIndex++)
                    {
                        // get current client
                        ConnectedClient c = Clients[clientIndex];

                        // Handle Disconnect
                        if (!c.Socket.IsConnected())
                        {
                            Clients.Remove(c);
                            server.Server_ClientDisconnected(c);
                            continue;
                        }

                        // check if client send something
                        if (c.Socket.Available == 0)
                            continue;

                        // get message size
                        currentLenght = -1;
                        if (currentLenght == -1 && c.Socket.Available > 4)
                        {
                            byte[] nextByte = new byte[4];
                            lock (c.receiveSyncRoot)
                                c.Socket.Receive(nextByte, 0, 4, SocketFlags.None);
                            currentLenght = BitConverter.ToInt32(nextByte, 0);
                            bytesReceived = new byte[currentLenght];
                        }

                        int i = 0;
                        // get message Data
                        while (currentLenght != -1 && c.Socket.Available > 0 && c.Socket.Connected)
                        {
                            // get size of byte array to receive for this loop / message
                            int nbBytesToReceive = c.Socket.Available;
                            if (nbBytesToReceive > BufferSize)
                                nbBytesToReceive = BufferSize;
                            if (nbBytesToReceive > currentLenght)
                                nbBytesToReceive = currentLenght;
                            // lock the sync object for prevent thread lock if sending thread send on same socket durring reception
                            lock (c.receiveSyncRoot)
                                c.Socket.Receive(bytesReceived, i, nbBytesToReceive, SocketFlags.None);
                            // count how many bytes we receive for this message
                            i += nbBytesToReceive;

                            // all message recieved
                            if (i == currentLenght)
                            {
                                currentLenght = -1;
                                server.MessageReceive(bytesReceived, c);
                            }
                        }
                    }

                    Thread.Sleep(1);
                }
                catch (SocketException)
                {
                    Writer.Write("Receiver switch disconnected client", ConsoleColor.Red);
                }
                catch (Exception ex)
                {
                    Writer.Write("Receiver fail : \n\r" + ex.ToString(), ConsoleColor.Red);
                }
            }
            Writer.Write("End of Receiver work (ID " + ReceiverID + ")", ConsoleColor.Red);
        }
    }

    public class MessageReceiverManager
    {
        private MessageReceiver[] receivers;
        public int NbReceivers { get; private set; }
        public bool ReceiversStarted { get; private set; }
        public int EmptiestReceiverID { get; private set; }
        public int NbClients { get; private set; }

        public MessageReceiverManager(NetSquare_Server server, int nbSenders, int bufferSize)
        {
            NbReceivers = nbSenders;
            receivers = new MessageReceiver[nbSenders];
            for (int i = 0; i < nbSenders; i++)
                receivers[i] = new MessageReceiver(server, i, bufferSize);
        }

        public void StartReceivers()
        {
            ReceiversStarted = true;
            EmptiestReceiverID = 0;
            min = int.MaxValue;
            Thread processEmptiestQueueIDThread = new Thread(ProcessEmptiestReceiverID);
            processEmptiestQueueIDThread.Start();
            foreach (MessageReceiver receiver in receivers)
            {
                receiver.ClearQueue();
                receiver.StartReceiver();
            }
        }

        public void StopReceivers()
        {
            ReceiversStarted = false;
            foreach (MessageReceiver receiver in receivers)
            {
                receiver.StopReceiver();
                receiver.ClearQueue();
            }
            EmptiestReceiverID = 0;
        }

        public void AddClient(ConnectedClient client)
        {
            receivers[EmptiestReceiverID].AddClient(client);
        }

        int min;
        private void ProcessEmptiestReceiverID()
        {
            while (ReceiversStarted)
            {
                NbClients = 0;
                min = int.MaxValue;
                for (int i = 0; i < NbReceivers; i++)
                {
                    NbClients += receivers[i].NbClients;
                    if (receivers[i].NbClients < min)
                    {
                        min = receivers[i].NbClients;
                        EmptiestReceiverID = i;
                    }
                }
                Thread.Sleep(100);
            }
        }
    }
}