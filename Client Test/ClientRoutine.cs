using NetSquare.Core;
using NetSquareClient;
using System;
using System.Collections.Generic;
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

            ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);
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
            Thread.Sleep(1);
            client.SendMessage(3);
            for (int i = 0; i < 10; i++)
            {
                client.SendMessage(new NetworkMessage(0).Set("coucou grand bg de la cité &é\"'(-è_çà'")); // send coucou as text
                client.SendMessage(new NetworkMessage(1)); // ping server
                client.SendMessage(new NetworkMessage(2)
                    .SetObject(new List<string>() { "coucou", "grand", "bg"})
                    .Set("ouaip")
                    .SetObject(new HashSet<long>() { long.MinValue, long.MaxValue}));
                Thread.Sleep(1);
            }
            client.SendMessage(4);
            client.Disconnect();
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
