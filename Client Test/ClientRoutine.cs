using NetSquare.Core;
using NetSquare.Client;
using NetSquareCore;
using System;

namespace Client_Test
{
    public class ClientRoutine
    {
        public NetSquareClient client;
        bool readyToSync = false;
        private static float serverOffset = 0;
        public static float Time { get { return EnlapsedTime + serverOffset; } }
        public static float EnlapsedTime { get { return (float)(DateTime.Now - startTime).TotalSeconds; } }
        private static DateTime startTime;
        public int LineIndex = 0;
        NetsquareTransformFrame currentPos;
        private float xOffset = 100;
        private float zOffset = 100;
        private static bool timeSynced => nbTimeSynced > 1;
        private static int nbTimeSynced = 0;

        public void Start(float x, float y, float z)
        {
            client = new NetSquareClient();
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;
            //client.WorldsManager.OnClientMove += WorldsManager_OnClientMove;
            currentPos = new NetsquareTransformFrame(x + xOffset, y, z + zOffset, 0, 0, 0, 1f, 0, 0);
            client.Connect("127.0.0.1", 5050, NetSquareProtocoleType.TCP, false);
        }

        private void WorldsManager_OnClientMove(uint arg1, NetsquareTransformFrame[] arg2)
        {
            if (client.ClientID == 1)
                Console.WriteLine("Client " + arg1 + " move to " + arg2[0].x + ", " + arg2[0].y + ", " + arg2[0].z);
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
            if (!timeSynced)
            {
                startTime = DateTime.Now;
                client.SyncTime(5, 200, (serverTime) =>
                {
                    nbTimeSynced++;
                    serverOffset = serverTime - EnlapsedTime;
                });

                while (!timeSynced)
                {
                    System.Threading.Thread.Sleep(10);
                }
            }

            Console.WriteLine("Connected with ID " + ID);
            currentPos.Set(0, Time);
            client.WorldsManager.TryJoinWorld(1, currentPos, (success) =>
            {
                if (success)
                {
                    Console.WriteLine("Client " + ID + " Join lobby");
                    readyToSync = true;
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
            Console.WriteLine("from Server : " + text);
        }

        public void Update(bool send, float x, float y)
        {
            if (!readyToSync)
                return;
            currentPos = new NetsquareTransformFrame(x + xOffset, 1f, y + zOffset, 0, 0, 0, 1, 0, Time);
            client.WorldsManager.StoreTransformFrame(currentPos);
            if (send)
            {
                client.WorldsManager.SendFrames();
            }
        }
    }

    enum MessagesTypes
    {
        WelcomeMessage = 0
    }
}