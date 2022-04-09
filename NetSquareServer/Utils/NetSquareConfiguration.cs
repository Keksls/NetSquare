using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace NetSquareServer
{
    public class NetSquareConfiguration
    {
        /// <summary>
        /// nb of miliseconds to wait before two message handling
        /// </summary>
        public int ProcessOffsetTime { get; set; }
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
        public byte NbQueueThreads { get; set; }
        /// <summary>
        /// Number of threads for TcpListners message reception
        /// </summary>
        public byte NbReceivingThreads { get; set; }

        public NetSquareConfiguration()
        {
            ProcessOffsetTime = 10;
            Port = 5555;
            NbReceivingThreads = 1;
            LockConsole = false;
            BlackListFilePath = Environment.CurrentDirectory + @"\BlackListedIP.json";
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
                Configuration = JsonConvert.DeserializeObject<NetSquareConfiguration>(File.ReadAllText(configurationPath));
                Configuration.BlackListFilePath = Configuration.BlackListFilePath.Replace("[current]", Environment.CurrentDirectory);
            }
        }

        /// <summary>
        /// nb of miliseconds to wait before two message handling
        /// </summary>
        public static void SetProcessOffsetTime(int ProcessOffsetTime)
        {
            Configuration.ProcessOffsetTime = ProcessOffsetTime;
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
        /// umber of threads for message action handling
        /// </summary>
        public static void SetNbQueueThreads(byte NbThreads)
        {
            Configuration.NbQueueThreads = NbThreads;
            SaveConfiguration(Configuration);
        }

        /// <summary>
        /// Number of threads for TcpListners message reception
        /// </summary>
        public static void SetNbReceivingThreads(byte NbThreads)
        {
            Configuration.NbReceivingThreads = NbThreads;
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
            File.WriteAllText(configurationPath, JsonConvert.SerializeObject(configuration));
        }
    }
}