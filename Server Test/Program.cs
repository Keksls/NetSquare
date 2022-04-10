using NetSquare.Core;
using NetSquareServer;
using NetSquareServer.Lobbies;
using NetSquareServer.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Server_Test
{
    internal class Program
    {
        static NetSquare_Server server;
        static ConcurrentDictionary<uint, ConcurrentDictionary<int, int>> nbMessagesReceives = new ConcurrentDictionary<uint, ConcurrentDictionary<int, int>>();

        static void Main(string[] args)
        {
            //int[] testArray = new int[1000];
            //for (int i = 0; i < testArray.Length; i++)
            //    testArray[i] = i;

            //StartStopWatch(null);
            //for (int i = 0; i < 10000; i++)
            //{
            //    NetworkMessage msg = new NetworkMessage();
            //    msg.Set(true).Set(float.MaxValue).SetObject(new List<string>() { "Hello", "World", "!" }).Set(uint.MaxValue).SetObject(testArray);
            //    byte[] array = msg.Serialize();

            //    byte[] arrayWithoutSize = new byte[array.Length - 4];
            //    Array.Copy(array, 4, arrayWithoutSize, 0, arrayWithoutSize.Length);
            //    NetworkMessage msg2 = new NetworkMessage(arrayWithoutSize);
            //    bool boolVal = msg2.GetBool();
            //    float floatVal = msg2.GetFloat();
            //    List<string> listVal = msg2.GetObject<List<string>>();
            //    uint uintVal = msg2.GetUInt();
            //    int[] intArrayVal = msg2.GetObject<int[]>();
            //}
            //EndStopWatch(null);

            // Optionnal, set configuration
            NetSquareConfiguration config = NetSquareConfigurationManager.Configuration;
            config.BlackListFilePath = @"[current]\blackList.bl";
            config.LockConsole = false;
            config.Port = 5050;
            config.ProcessOffsetTime = 1;
            config.NbReceivingThreads = 4;
            config.NbQueueThreads = 8;
            NetSquareConfigurationManager.SaveConfiguration(config);

            // Instantiate NetSquare Server
            server = new NetSquare_Server();
            server.Dispatcher.AddHeadAction(1, "Client Ping", ClientPingMe); // add callback to message ID 1
            server.OnClientConnected += Server_OnClientConnected;
            server.OnClientDisconnected += Server_OnClientDisconnected;

            // Optionnal, set encryption and compression protocole
            //ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            //ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);

            // Create a test lobby
            LobbiesManager.AddLobby();

            // Start Server
            //Writer.StartRecordingLog();
            Writer.StartDisplayTitle();
            server.Start();
            //Writer.StopDisplayLog();
        }

        private static void Server_OnClientDisconnected(uint clientID)
        {
            Writer.Write("Client " + clientID + " disconnected");
        }

        private static void Server_OnClientConnected(uint clientID)
        {
            server.SendToClient(new NetworkMessage(0).Set("Hey new client " + clientID + ". Welcome to my NetSquare server"), clientID);
        }

        /// <summary>
        /// This method must be public and static for being binded by the dispatcher.
        /// You can use non static method and do not set the attribut NetSquareAction by calling server.Dispatcher.Add
        /// </summary>
        /// <param name="message"></param>
        [NetSquareAction(0)]
        public static void ClientSentTextMessage(NetworkMessage message)
        {
            string clientMessage = "";
            message.Get(ref clientMessage);
            Writer.Write("Client " + message.ClientID + " say : " + clientMessage, ConsoleColor.White);
        }

        /// <summary>
        /// this method will no be binded by dispatcher, but we will add it manualy before start the server
        /// </summary>
        /// <param name="message"></param>
        static void ClientPingMe(NetworkMessage message)
        {
            Writer.Write("Client " + message.ClientID + " Ping", ConsoleColor.White);
        }

        [NetSquareAction(2)]
        public static void ClientSendComplexMessage(NetworkMessage message)
        {
            List<string> strings = message.GetObject<List<string>>();
            string text = message.GetString();
            HashSet<long> longs = message.GetObject<HashSet<long>>();
            Writer.Write("Client " + message.ClientID + " Complex Message", ConsoleColor.White);
        }

        static Stopwatch sw = new Stopwatch();
        [NetSquareAction(3)]
        public static void StartStopWatch(NetworkMessage message)
        {
            sw.Reset();
            sw.Start();
            Writer.Write("Client " + message.ClientID + " Start Stopwatch", ConsoleColor.White);
        }

        [NetSquareAction(4)]
        public static void EndStopWatch(NetworkMessage message)
        {
            sw.Stop();
            Writer.Write("Client " + message.ClientID + " End Stopwatch : " + sw.ElapsedMilliseconds + " ms", ConsoleColor.White);
        }
    }
}