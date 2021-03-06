using NetSquare.Core;
using NetSquareClient;
using NetSquareCore;
using System;
using System.Threading;

namespace Client_Test
{
    public class ClientRoutine
    {
        NetSquare_Client client;
        bool readyToSync = false;
        float x = 0, y = 0, z = 0;
        Random rnd = new Random();

        public void Start()
        {
            client = new NetSquare_Client();
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;
            //client.WorldsManager.OnClientMove += WorldsManager_OnClientMove;

            ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);
            client.Connect("127.0.0.1", 5050);
        }

        private void WorldsManager_OnClientMove(UInt24 clientID, float x, float y, float z)
        {
            Console.WriteLine(client.ClientID + " : Client " + clientID + " move to : " + x + ", " + y + ", " + z);
        }

        private void Client_Disconected()
        {
            Console.WriteLine("Client Disconnected");
        }

        private void Client_ConnectionFail()
        {
            Console.WriteLine("Client Connection fail");
        }

        private void Client_Connected(UInt24 ID)
        {
            Console.WriteLine("Connected with ID " + ID);
            Thread.Sleep(10);
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

        public void TestSync()
        {
            if (!readyToSync)
                return;
            GetNextTargetPoint();
            client.WorldsManager.SetPosition(currentPos);
        }

        int nbStep = 100;
        int index = -1;
        Position startPos = Position.zero;
        Position targetPos;
        Position currentPos = Position.zero;
        private void GetNextTargetPoint()
        {
            index++;
            if (index >= nbStep)
                index = 0;
            if (index == 0)
            {
                x = ((float)rnd.Next(-1000, 1000)) / 20f;
                y = 1f;
                z = ((float)rnd.Next(-1000, 1000)) / 20f;
                targetPos = new Position(x, y, z);
                if (currentPos.Equals(Position.zero))
                    currentPos.Set(targetPos);
                startPos.Set(currentPos);
            }

            currentPos = Position.Lerp(startPos, targetPos, (float)index / (float)nbStep);
        }
    }

    enum MessagesTypes
    {
        WelcomeMessage = 0
    }
}