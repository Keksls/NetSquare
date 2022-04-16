using NetSquare.Core;
using NetSquareServer;
using NetSquareServer.Server;
using NetSquareServer.Utils;
using System;

namespace Server_Test
{
    internal class Program
    {
        static NetSquare_Server server;
        static void Main(string[] args)
        {
            //ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.LZ4Compression);
            //SerializationPerformances sp = new SerializationPerformances();
            //sp.TestPerformances(10000);
            //Console.ReadKey();

            // Set configuration
            NetSquareConfiguration config = NetSquareConfigurationManager.Configuration;
            config.BlackListFilePath = @"[current]\blackList.bl";
            config.LockConsole = false;
            config.Port = 5050;
            config.NbSendingThreads = 4;
            config.NbQueueThreads = 4;
            config.SynchronizingFrequency = 10;
            NetSquareConfigurationManager.SaveConfiguration(config);

            // Instantiate NetSquare Server
            server = new NetSquare_Server();
            server.OnClientConnected += Server_OnClientConnected;
            server.OnClientDisconnected += Server_OnClientDisconnected;
            server.Statistics.OnGetStatistics += Statistics_OnGetStatistics;
            server.Worlds.AddWorld("Default World", 1024);
            server.Worlds.OnClientJoinWorld += OnClientJoinWorld;

            // Optionnal, set encryption and compression protocole
            //ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            //ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);

            // Start Server
            //Writer.StartRecordingLog();
            server.Start(allowLocalIP: true);
            //Writer.StopDisplayLog();
            Writer.StartDisplayTitle();
        }

        #region Server Events
        private static void OnClientJoinWorld(ushort lobbyID, uint clientID, NetworkMessage message)
        {
            // add random color to network message that will be sended on client join lobby
            message.Set(GetRandomColorArray());
        }

        private static void Statistics_OnGetStatistics(ServerStatistics statistics)
        {
            if (!Writer.DisplayTitle)
                return;
            Writer.Title(statistics.ToString());
        }

        private static void Server_OnClientDisconnected(uint clientID)
        {
            Writer.Write("Client " + clientID + " disconnected");
        }

        private static void Server_OnClientConnected(uint clientID)
        {
            server.SendToClient(new NetworkMessage(0).Set("Welcome to my NetSquare server, client " + clientID), clientID);
        }
        #endregion

        /// <summary>
        /// This method must be public and static for being binded by the dispatcher.
        /// You can use non static method and do not set the attribut NetSquareAction by calling server.Dispatcher.Add
        /// </summary>
        /// <param name="message">Message received from client that Invoke this method</param>
        [NetSquareAction(0)]
        public static void ClientSentTextMessage(NetworkMessage message)
        {
            Writer.Write("Client " + message.ClientID + " say : " + message.GetString(), ConsoleColor.White);
        }

        #region Private Utils
        static Random random = new Random();
        private static byte[] GetRandomColorArray()
        {
            byte[] array = new byte[16];
            float rnd = (float)random.NextDouble();
            Buffer.BlockCopy(BitConverter.GetBytes(rnd), 0, array, 0, 4);
            rnd = (float)random.NextDouble();
            Buffer.BlockCopy(BitConverter.GetBytes(rnd), 0, array, 4, 4);
            rnd = (float)random.NextDouble();
            Buffer.BlockCopy(BitConverter.GetBytes(rnd), 0, array, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, array, 12, 4);
            return array;
        }
        #endregion
    }
}