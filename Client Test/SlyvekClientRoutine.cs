using NetSquare.Core;
using NetSquareClient;
using NetSquareCore;
using System;

namespace Client_Test
{
    public class SlyvekClientRoutine
    {
        NetSquare_Client client;

        public void Start()
        {
            client = new NetSquare_Client();
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;
            client.WorldsManager.OnClientJoinWorld += WorldsManager_OnClientJoinWorld;
            client.Connect("127.0.0.1", 5555);
        }

        private void WorldsManager_OnClientJoinWorld(NetSquare.Core.NetworkMessage obj)
        {
            Console.WriteLine(obj.ClientID + " join my world");
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
            Console.WriteLine("Connected with ID " + ID); client.SendMessage(new NetworkMessage(0));

            client.SendMessage(new NetworkMessage(2).Set("l").Set("l"),
            (response) => // Callback
            {
                if (response.GetBool())
                {
                    Console.WriteLine("Bot connected");
                    client.WorldsManager.TryJoinWorld(1, NetsquareTransformFrame.zero, (success) =>
                    {
                        Console.WriteLine("rejoin le monde.");
                    });
                }
                else // create account fail
                    Console.WriteLine(response.GetString());
            });
        }
    }
}