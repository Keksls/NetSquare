using NetSquare.Client;
using NetSquare.Core;
using System;

namespace Client_Test
{
    public class SlyvekClientRoutine
    {
        NetSquareClient client;

        public void Start()
        {
            client = new NetSquareClient();
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;
            client.WorldsManager.OnClientJoinWorld += WorldsManager_OnClientJoinWorld;
            client.Connect("127.0.0.1", 5555);
        }

        private void WorldsManager_OnClientJoinWorld(uint clientID, NetsquareTransformFrame transform, NetworkMessage message)
        {
            Console.WriteLine(message.ClientID + " join my world at pos : " + transform.x + " " + transform.y + " " + transform.z);
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
                if (response.Serializer.GetBool())
                {
                    Console.WriteLine("Bot connected");
                    client.WorldsManager.TryJoinWorld(1, NetsquareTransformFrame.zero, (success) =>
                    {
                        Console.WriteLine("rejoin le monde.");
                    });
                }
                else // create account fail
                    Console.WriteLine(response.Serializer.GetString());
            });
        }
    }
}