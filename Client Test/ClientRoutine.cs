using NetSquare.Core;
using NetSquareClient;
using NetSquareCore;
using System;
using System.Numerics;

namespace Client_Test
{
    public class ClientRoutine
    {
        public NetSquare_Client client;
        bool readyToSync = false;
        Random rnd = new Random();
        float speed = 1.5f;
        private float serverOffset = 0;
        public float Time { get { return EnlapsedTime + serverOffset; } }
        public float EnlapsedTime { get { return (float)(DateTime.Now - startTime).TotalSeconds; } }
        DateTime startTime;

        public void Start(float maxSpeed)
        {
            startTime = DateTime.Now;
            client = new NetSquare_Client(eProtocoleType.TCP, false);
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;
            client.WorldsManager.OnClientMove += WorldsManager_OnClientMove;
            speed = maxSpeed;

            //ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            // ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);
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
            client.SyncTime(5, 1000, (serverTime) =>
            {
                serverOffset = serverTime - EnlapsedTime;
            });

            Console.WriteLine("Connected with ID " + ID);
            GetNextTargetPoint();
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

        public void TestSync(bool send)
        {
            if (!readyToSync)
                return;
            GetNextTargetPoint();
            client.WorldsManager.StoreTransformFrame(currentPos);
            if (send)
            {
                client.WorldsManager.SendFrames();
            }
        }

        NetsquareTransformFrame startPos = NetsquareTransformFrame.zero;
        NetsquareTransformFrame targetPos;
        NetsquareTransformFrame currentPos = NetsquareTransformFrame.zero;
        private void GetNextTargetPoint()
        {
            if (NetsquareTransformFrame.Distance(targetPos, currentPos) < 0.5f)
            {
                Quaternion q = Quaternion.Identity;
                targetPos = new NetsquareTransformFrame((float)rnd.Next(0, 200), 1f, (float)rnd.Next(0, 200), q.X, q.Y, q.Z, q.W, 0, Time);
                if (currentPos.Equals(NetsquareTransformFrame.zero))
                    currentPos.Set(targetPos);
                startPos.Set(currentPos);
            }

            currentPos = currentPos.MoveToward(targetPos, speed);
            currentPos.Time = Time;
        }
    }

    enum MessagesTypes
    {
        WelcomeMessage = 0
    }
}