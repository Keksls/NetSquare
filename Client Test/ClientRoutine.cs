using NetSquare.Core;
using NetSquareClient;
using NetSquareCore;
using System;
using System.Runtime.Serialization;
using System.Threading;

namespace Client_Test
{
    public class ClientRoutine
    {
        public NetSquare_Client client;
        bool readyToSync = false;
        float x = 0, y = 0, z = 0;
        Random rnd = new Random();

        public void Start()
        {
            client = new NetSquare_Client(eProtocoleType.TCP, false);
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;
            //client.WorldsManager.OnClientMove += WorldsManager_OnClientMove;

            //ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            // ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);
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

        private void Client_Connected(uint ID)
        {
            client.SyncTime(10, 1000);

            Console.WriteLine("Connected with ID " + ID);
            GetNextTargetPoint();
            client.WorldsManager.OnClientJoinWorld += WorldsManager_OnClientJoinWorld;
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

        private void WorldsManager_OnClientJoinWorld(NetworkMessage obj)
        {
            Console.WriteLine("Client Join World at pos : " + obj.GetFloat() + ", " + obj.GetFloat() + ", " + obj.GetFloat() + " - rot : " + obj.GetByte());
            //throw new NotImplementedException();
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
            client.WorldsManager.SendTransformFrame(currentPos);
        }

        int nbStep = 100;
        int index = -1;
        NetsquareTransformFrame startPos = NetsquareTransformFrame.zero;
        NetsquareTransformFrame targetPos;
        NetsquareTransformFrame currentPos = NetsquareTransformFrame.zero;
        private void GetNextTargetPoint()
        {
            index++;
            if (index >= nbStep)
                index = 0;
            if (index == 0)
            {
                x = (float)rnd.Next(0, 100);
                y = 1f;
                z = (float)rnd.Next(0, 100);
                targetPos = new NetsquareTransformFrame(x, y, z, (byte)rnd.Next(0, 255), 0, client.Time);
                if (currentPos.Equals(NetsquareTransformFrame.zero))
                    currentPos.Set(targetPos);
                startPos.Set(currentPos);
            }

            currentPos = NetsquareTransformFrame.Lerp(startPos, targetPos, (float)index / (float)nbStep);
        }
    }

    enum MessagesTypes
    {
        WelcomeMessage = 0
    }
}