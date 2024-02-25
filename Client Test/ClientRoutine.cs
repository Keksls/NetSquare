using NetSquare.Core;
using NetSquareClient;
using NetSquareCore;
using System;

namespace Client_Test
{
    public class ClientRoutine
    {
        public NetSquare_Client client;
        bool readyToSync = false;
        float speed = 1.5f;
        private static float serverOffset = 0;
        public static float Time { get { return EnlapsedTime + serverOffset; } }
        public static float EnlapsedTime { get { return (float)(DateTime.Now - startTime).TotalSeconds; } }
        private static DateTime startTime;
        public int LineIndex = 0;
        NetsquareTransformFrame targetPos;
        NetsquareTransformFrame currentPos;
        private float xOffset = 50;
        private float zOffset = 50;
        private static bool timeSynced = false;

        public void Start(float _speed, float x, float y, float z, float startLineX, float startLineZ)
        {
            startTime = DateTime.Now;
            client = new NetSquare_Client(eProtocoleType.TCP, false);
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;
            client.WorldsManager.OnClientMove += WorldsManager_OnClientMove;
            speed = _speed;
            currentPos = new NetsquareTransformFrame(x + xOffset, y, z + zOffset, 0, 0, 0, 1f, 0, 0);
            SetNextTargetPoint(startLineX, startLineZ);
            client.Connect("127.0.0.1", 5050);
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
                timeSynced = true;
                client.SyncTime(5, 1000, (serverTime) =>
                {
                    serverOffset = serverTime - EnlapsedTime;
                });
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

        public void Update(bool send, float deltaTime)
        {
            if (!readyToSync)
                return;
            if (NetsquareTransformFrame.Distance(targetPos, currentPos) < 0.1f)
            {
                SetNextTargetPoint(targetPos.x, targetPos.z);
            }

            currentPos = currentPos.MoveToward(targetPos, speed * deltaTime);
            currentPos.Time = Time;
            client.WorldsManager.StoreTransformFrame(currentPos);
            if (send)
            {
                client.WorldsManager.SendFrames();
            }
        }

        private void SetNextTargetPoint(float _x, float _z)
        {
            float x = _x - xOffset;
            float z = _z - zOffset;
            // we are on the top left corner
            if (x < 0f && z > 0f)
            {
                // go to the top right corner
                targetPos = new NetsquareTransformFrame(LineIndex + xOffset, 1f, LineIndex + xOffset);
            }
            // we are on the top right corner
            else if (x > 0f && z > 0f)
            {
                // go to the bottom right corner
                targetPos = new NetsquareTransformFrame(LineIndex + xOffset, 1f, -LineIndex + zOffset);
            }
            // we are on the bottom right corner
            else if (x > 0f && z < 0f)
            {
                // go to the bottom left corner
                targetPos = new NetsquareTransformFrame(-LineIndex + xOffset, 1f, -LineIndex + zOffset);
            }
            // we are on the bottom left corner
            else if (x < 0f && z < 0f)
            {
                // go to the top left corner
                targetPos = new NetsquareTransformFrame(-LineIndex + xOffset, 1f, LineIndex + zOffset);
            }
        }
    }

    enum MessagesTypes
    {
        WelcomeMessage = 0
    }
}