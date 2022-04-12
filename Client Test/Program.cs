using NetSquare.Core;
using NetSquareClient;
using System;
using System.Threading;

namespace Client_Test
{
    internal class Program
    {
        static NetSquare_Client client;
        static void Main(string[] args)
        {
            Console.WriteLine("How many clients : ");
            string clients = Console.ReadLine();
            int nbCLients = 10;
            int.TryParse(clients, out nbCLients);
            for (int i = 0; i < nbCLients; i++)
            {
                Thread t = new Thread(() =>
               {
                   ClientRoutine routine = new ClientRoutine();
                   routine.Start();
               });
                t.Start();
                Thread.Sleep(100);
            }
            //client = new NetSquare_Client();
            //Console.WriteLine(client.Dispatcher.Count + " action registered");
            //client.LobbiesManager.OnClientJoinLobby += LobbiesManager_OnClientJoinLobby;
            //client.LobbiesManager.OnClientLeaveLobby += LobbiesManager_OnClientLeaveLobby;
            //client.Connect("192.168.8.101", 5050);
            //client.Connected += Client_Connected;
            //Console.ReadKey();
        }

        private static void LobbiesManager_OnClientJoinLobby(uint clientID)
        {
            Console.WriteLine("Client Join my lobby [" + clientID + "]");
        }

        private static void LobbiesManager_OnClientLeaveLobby(uint clientID)
        {
            Console.WriteLine("Client Leave my lobby [" + clientID + "]");
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