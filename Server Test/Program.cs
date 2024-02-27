using NetSquare.Core;
using NetSquareCore;
using NetSquare.Server;
using NetSquare.Server.Server;
using NetSquare.Server.Utils;
using NetSquare.Server.Worlds;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Server_Test
{
    internal class Program
    {
        static NetSquareServer server;
        static ServerMonitor.Form1 monitor;

        [STAThread]
        static unsafe void Main(string[] args)
        {
            // Set configuration
            NetSquareConfiguration config = NetSquareConfigurationManager.Configuration;
            config.BlackListFilePath = @"[current]\blackList.bl";
            config.LockConsole = false;
            config.Port = 5050;
            config.NbSendingThreads = 4;
            config.NbQueueThreads = 4;
            config.SynchronizingFrequency = 10;
            config.UpdateFrequencyHz = 10;
            NetSquareConfigurationManager.SaveConfiguration(config);

            // Instantiate NetSquare Server
            server = new NetSquareServer(NetSquareProtocoleType.TCP);
            server.OnClientConnected += Server_OnClientConnected;
            server.Statistics.IntervalMs = 100;
            server.Statistics.OnGetStatistics += Statistics_OnGetStatistics;
            server.OnTimeLoop += Server_OnTimeLoop;

            NetSquareWorld world = server.Worlds.AddWorld("Default World", ushort.MaxValue);
            world.SetSpatializer(Spatializer.GetChunkedSpatializer(world, 5f, 4f, 100f, 0f, 0f, 1000f, 1000f));
            //world.SetSpatializer(Spatializer.GetSimpleSpatializer(world, 2f, 4f, 50f));
            world.Spatializer.SetAdaptiveSynchFrequency(10, 1000, 50, 5); // start adaptative synch frequency

            // Optionnal, set encryption and compression protocole
            //ProtocoleManager.SetEncryptor(NetSquare.Core.Encryption.eEncryption.OneToZeroBit);
            //ProtocoleManager.SetCompressor(NetSquare.Core.Compression.eCompression.DeflateCompression);

            // Start Server
            //Writer.StartRecordingLog();
            server.Start(allowLocalIP: true);
            //Writer.StopRecordingLog();
            Writer.StartDisplayTitle();

            // Start Server Monitor
            Application.EnableVisualStyles();
            monitor = new ServerMonitor.Form1();
            monitor.Initialize(600, 10);
            Application.Run(monitor);
        }

        private static ServerStatistics currentStatistics;
        private static void Server_OnTimeLoop(float obj)
        {
            if (!Writer.DisplayTitle)
                return;
            string humanReadableTime = TimeSpan.FromMilliseconds(server.Time * 1000f).ToString(@"hh\:mm\:ss");
            Writer.Title("T:" + server.Time + " " + humanReadableTime + " " + currentStatistics.ToString());
        }

        #region Server Events
        private static void Statistics_OnGetStatistics(ServerStatistics statistics)
        {
            if (!Writer.DisplayTitle)
                return;
            currentStatistics = statistics;
            string humanReadableTime = TimeSpan.FromMilliseconds(server.Time * 1000f).ToString(@"hh\:mm\:ss");
            Writer.Title("T:" + server.Time + " " + humanReadableTime + " " + statistics.ToString());

            // monitor form
            if (monitor == null || monitor.IsDisposed)
            {
                return;
            }
            monitor.Clear();
            monitor.Write("T:" + server.Time + " " + humanReadableTime + " " + currentStatistics.ToString());
            monitor.UpdateWorldData(server.Worlds.Worlds[1]);
            monitor.UpdateStatistics(currentStatistics);
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