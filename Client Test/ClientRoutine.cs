using NetSquare.Core;
using NetSquareClient;
using System;
using System.Threading;

namespace Client_Test
{
    public class ClientRoutine
    {
        NetSquare_Client client;
        bool connected = false;
        public void Start()
        {
            client = new NetSquare_Client();
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;

            //ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            //ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);
            client.Connect("127.0.0.1", 5050);
        }

        private void Client_Disconected()
        {
            Console.WriteLine("Client Disconnected");
            connected = false;
        }

        private void Client_ConnectionFail()
        {
            Console.WriteLine("Client Connection fail");
        }

        private void Client_Connected(uint ID)
        {
            float x = 0, y = 0, z = 0;
            Random rnd = new Random();

            connected = true;
            Console.WriteLine("Connected with ID " + ID);
            Thread.Sleep(10);
            client.WorldsManager.TryJoinWorld(1, (success) =>
            {
                if (success)
                {
                    Console.WriteLine("Client " + ID + " Join lobby");
                    Thread t = new Thread(() =>
                    {
                        while (connected)
                        {
                            x = ((float)rnd.Next(-1000, 1000)) / 20f;
                            y = 1f;
                            z = ((float)rnd.Next(-1000, 1000)) / 20f;
                            client.WorldsManager.Synchronize(new NetworkMessage(10).Set(x).Set(y).Set(z));
                            client.WorldsManager.Synchronize(new NetworkMessage(11).Set(x));
                            client.WorldsManager.Synchronize(new NetworkMessage(12).Set(y));
                            client.WorldsManager.Synchronize(new NetworkMessage(13).Set(z));
                            Thread.Sleep(10);
                        }
                    });
                    t.Start();
                }
                else
                {
                    Console.WriteLine("Client " + ID + " Fail lobby");
                    client.Disconnect();
                }
            });
        }

        [NetSquareAction((ushort)MessagesTypes.WelcomeMessage)]
        public static void ReceivingDebugMessageFromServer(NetworkMessage message)
        {
            string text = "";
            message.Get(ref text);
            Console.WriteLine(text);
        }
    }

    enum MessagesTypes
    {
        WelcomeMessage = 0
    }
}