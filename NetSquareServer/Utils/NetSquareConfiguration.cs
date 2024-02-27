using System;
using System.IO;
using System.Reflection;
using System.Text;
using Utf8Json;

namespace NetSquare.Server
{
    public class NetSquareConfiguration
    {
        /// <summary>
        /// the port to start server on
        /// </summary>
        public int Port { get; set; }
        /// <summary>
        /// If TRUE, the server consol will be lock to unselectable
        /// </summary>
        public bool LockConsole { get; set; }
        /// <summary>
        /// Path to the BlackListed IP list file
        /// </summary>
        public string BlackListFilePath { get; set; }
        /// <summary>
        /// Number of threads for message action handling
        /// </summary>
        public int NbQueueThreads { get; set; }
        /// <summary>
        /// Receiving buffer max size
        /// </summary>
        public int ReceivingBufferSize { get; set; }
        /// <summary>
        /// Number of threads for TcpListners message sending
        /// </summary>
        public int NbSendingThreads { get; set; }
        /// <summary>
        /// Frequency of var synchronization
        /// </summary>
        public int SynchronizingFrequency { get; set; }
        /// <summary>
        /// Frequency of loop time in Hz
        /// </summary>
        public float UpdateFrequencyHz { get; set; }

        public NetSquareConfiguration()
        {
            Port = 5555;
            NbSendingThreads = 1;
            NbQueueThreads = 1;
            ReceivingBufferSize = 1024;
            LockConsole = false;
            BlackListFilePath = Environment.CurrentDirectory + @"\BlackListedIP.json";
            UpdateFrequencyHz = 10;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (PropertyInfo Info in GetType().GetProperties())
            {
                sb.Append(" - ");
                sb.Append(Info.Name);
                sb.Append(" : ");
                sb.AppendLine(GetType().GetProperty(Info.Name).GetValue(this).ToString());
            }
            return sb.ToString();
        }
    }

    public static class NetSquareConfigurationManager
    {
        public static NetSquareConfiguration Configuration;
        private static string configurationPath;

        static NetSquareConfigurationManager()
        {
            configurationPath = Environment.CurrentDirectory + @"\config.json";
            if (File.Exists(configurationPath))
            {
                Configuration = JsonSerializer.Deserialize<NetSquareConfiguration>(File.ReadAllText(configurationPath));
                Configuration.BlackListFilePath = Configuration.BlackListFilePath.Replace("[current]", Environment.CurrentDirectory);
            }
            else
            {
                Configuration = new NetSquareConfiguration();
            }
        }

        /// <summary>
        /// Frequency of loop time in Hz
        /// </summary>
        /// <param name="UpdateFrequencyHz">frequency in Hz</param>
        public static void SetUpdateFrequencyHz(int UpdateFrequencyHz)
        {
            Configuration.UpdateFrequencyHz = UpdateFrequencyHz;
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// Frequency of var synchronization
        /// </summary>
        public static void SetSynchronizingFrequency(int SynchronizingFrequency)
        {
            Configuration.SynchronizingFrequency = SynchronizingFrequency;
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// number of threads for message sending
        /// </summary>
        public static void SetNbSendingThreadse(int NbSendingThreads)
        {
            Configuration.NbSendingThreads = NbSendingThreads;
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// Receiving buffer max size
        /// </summary>
        public static void SetReceivingBufferSize(int ReceivingBufferSize)
        {
            Configuration.ReceivingBufferSize = ReceivingBufferSize;
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// the port to start server on
        /// </summary>
        public static void SetPort(int Port)
        {
            Configuration.Port = Port;
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// number of threads for message action handling
        /// </summary>
        public static void SetNbQueueThreads(int NbThreads)
        {
            Configuration.NbQueueThreads = NbThreads;
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// If TRUE, the server consol will be lock to unselectable
        /// </summary>
        public static void SetLockConsole(bool LockConsole)
        {
            Configuration.LockConsole = LockConsole;
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// Path to the BlackListed IP list file
        /// </summary>
        public static void SetBlackListFilePath(string BlackListFilePath)
        {
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// Save the given configuration as json next to the server dll
        /// </summary>
        /// <param name="Configuration"></param>
        public static void SaveConfiguration(NetSquareConfiguration configuration)
        {
            Configuration = configuration;
            Configuration.BlackListFilePath = Configuration.BlackListFilePath.Replace("[current]", Environment.CurrentDirectory);
            File.WriteAllText(configurationPath, UTF8Encoding.UTF8.GetString(JsonSerializer.Serialize(configuration)));
        }
    }
}