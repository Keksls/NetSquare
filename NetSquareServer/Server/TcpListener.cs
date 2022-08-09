﻿using NetSquare.Core;
using NetSquareServer.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NetSquareServer
{
    public class TcpListener
    {
        public bool Started { get; private set; }
        public int VerifyingClients { get; private set; }
        public bool CheckBlackList { get; private set; }
        private TcpListenerEx _listener = null;
        private NetSquare_Server server = null;
        internal IPAddress IPAddress { get; private set; }
        internal int Port { get; private set; }
        internal TcpListenerEx Listener { get { return _listener; } }

        public TcpListener(NetSquare_Server _server, IPAddress ipAddress, int port, bool checkBlackList)
        {
            CheckBlackList = checkBlackList;
            Started = true;
            server = _server;
            IPAddress = ipAddress;
            Port = port;
            _listener = new TcpListenerEx(ipAddress, port);
            _listener.Start();
            ThreadPool.QueueUserWorkItem((sender) => { HandleConnection(); });
            ThreadPool.QueueUserWorkItem((sender) => { HandleDisconnection(); });
        }

        public void Stop()
        {
            Started = false;
        }

        private void HandleConnection()
        {
            while (Started)
            {
                // Handle new clients Connection
                if (_listener.Pending())
                {
                    ThreadPool.QueueUserWorkItem(AcceptConnection);
                }
                Thread.Sleep(1);
            }
        }

        private void AcceptConnection(object sender)
        {
            Socket newClient = _listener.AcceptTcpClient().Client;
            if (CheckBlackList && BlackListManager.IsBlackListed(newClient))
                newClient.Close();
            else
                ValidateClient(newClient);
        }

        private void HandleDisconnection()
        {
            while (Started)
            {
                // Handle Disconnect
                var ids = server.Clients.Keys;
                foreach (uint clientID in ids)
                {
                    try
                    {
                        ConnectedClient client;
                        if (server.Clients.TryGetValue(clientID, out client) && !client.IsConnected())
                            server.Server_ClientDisconnected(client);
                    }
                    catch (Exception ex)
                    {
                        Writer.Write("Fail to disconnect client " + clientID + "  : " + ex.ToString(), ConsoleColor.Red);
                    }
                }

                Thread.Sleep(1000);
            }
        }

        private void ValidateClient(Socket client)
        {
            try
            {
                long timeEnd = DateTime.Now.AddSeconds(30).Ticks;
                VerifyingClients++;
                // send handShake
                int rnd1 = 0;
                int rnd2 = 0;
                int key = 0;
                byte[] handShake = HandShake.GetRandomHandShake(out rnd1, out rnd2, out key);
                client.Send(handShake, 0, handShake.Length, SocketFlags.None);
                bool isClientOK = false;
                Writer.Write("HandShake client " + rnd1 + " " + rnd2 + " " + key, ConsoleColor.Cyan);

                // wait for client renspond correct hash
                while (client.Connected && DateTime.Now.Ticks < timeEnd)
                {
                    // client disconnect
                    if (!client.Connected)
                    {
                        Writer.Write("Client disconected before end handshake. Close his connection", ConsoleColor.Red);
                        break;
                    }

                    // get awnser
                    if (client.Available >= 4)
                    {
                        byte[] array = new byte[4];
                        client.Receive(array, 0, 4, SocketFlags.None);
                        int clientKey = BitConverter.ToInt32(array, 0);
                        if (clientKey == key)
                            isClientOK = true;
                        else
                            Writer.Write("Client awnser wrong handshake key.", ConsoleColor.Red);
                        break;
                    }
                }

                // client awnser good
                if (isClientOK)
                {
                    Writer.Write("Client awnser good handshake key. Accept it.", ConsoleColor.Green);

                    ConnectedClient cClient = new ConnectedClient();
                    cClient.SetClient(client, false);
                    uint clientID = server.AddClient(cClient);

                    // client disconnect
                    if (!cClient.IsConnected())
                    {
                        Writer.Write("Client disconected before end of intialization. Close his connection", ConsoleColor.Red);
                        VerifyingClients--;
                        return;
                    }

                    VerifyingClients--;
                    client.Send(new UInt24(clientID).GetBytes(), 0, 3, SocketFlags.None);
                    server.Server_ClientConnected(server.GetClient(clientID), clientID);
                }
                // no awnser, awnser error, disconnected or timeout
                else
                {
                    VerifyingClients--;
                    client.Close();
                    client.Dispose();
                }
            }
            catch (Exception ex)
            {
                Writer.Write("Fail to HandShake client : " + ex.Message, ConsoleColor.Red);
            }
        }
    }
}