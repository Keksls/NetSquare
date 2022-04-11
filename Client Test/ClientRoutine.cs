using NetSquare.Core;
using NetSquareClient;
using System;
using System.Threading;

namespace Client_Test
{
    public class ClientRoutine
    {
        NetSquare_Client client;
        public void Start()
        {
            client = new NetSquare_Client();
            client.Connected += Client_Connected;
            client.ConnectionFail += Client_ConnectionFail;
            client.Disconected += Client_Disconected;

            //ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            //ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);
            client.Connect("192.168.8.101", 5050);
        }

        private void Client_Disconected()
        {
            Console.WriteLine("Client Disconnected");
        }

        private void Client_ConnectionFail()
        {
            Console.WriteLine("Client Connection fail");
        }

        private void Client_Connected(uint ID)
        {
            Console.WriteLine("Connected with ID " + ID);
            Thread.Sleep(10);
            client.LobbiesManager.TryJoinLobby(1, (success) =>
            {
                if (success)
                {
                    Random rnd = new Random();
                    while (true)
                    {
                        client.LobbiesManager.Broadcast(new NetworkMessage(10).Set((float)rnd.Next(-1000, 1000) / 20f)
                            .Set(1f)
                            .Set((float)rnd.Next(-1000, 1000) / 20f));
                        Thread.Sleep(rnd.Next(10, 100));
                    }
                }
                else
                    client.Disconnect();
            });
        }

        [NetSquareAction(0)]
        public static void ReceivingDebugMessageFromServer(NetworkMessage message)
        {
            string text = "";
            message.Get(ref text);
            Console.WriteLine(text);
        }
    }
}
