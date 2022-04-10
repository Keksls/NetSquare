using NetSquare.Core;
using NetSquareClient;
using System;

namespace Client_Test
{
    internal class Program
    {
        static NetSquare_Client client;
        static void Main(string[] args)
        {
            client = new NetSquare_Client();
            client.Connect("192.168.8.101", 5050);
            client.Connected += Client_Connected;
            Console.ReadKey();
        }

        private static void Client_Connected(uint obj)
        {
            client.LobbiesManager.TryJoinLobby(1, (ok) =>
            {
                Console.WriteLine(ok ? "Join Lobby" : "Fail lobby");
                if (ok)
                    client.LobbiesManager.Broadcast(new NetworkMessage(666).Set("This is a lobby broadcast from client " + client.ClientID));
            });
        }

        [NetSquareAction(666)]
        public static void ClientSendBroadcast(NetworkMessage message)
        {
            Console.WriteLine(message.GetString());
        }
    }
}