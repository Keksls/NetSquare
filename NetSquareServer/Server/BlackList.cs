using NetSquare.Server.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

#region Source
namespace NetSquare.Server
{
    /// <summary>
    /// Represents the black list manager component.
    /// </summary>
    public static class BlackListManager
    {
        /// <summary>
        /// Stores the ip black list value.
        /// </summary>
        public static HashSet<string> IPBlackList = new HashSet<string>();
        /// <summary>
        /// Stores the HTTP client used for external blacklist checks.
        /// </summary>
        private static readonly HttpClient HttpClient = CreateHttpClient();

        /// <summary>
        /// Executes the initialize operation.
        /// </summary>
        public static void Initialize()
        {
            // Load Black list
            Writer.Write_Physical("Loading IP Blacklist...", ConsoleColor.DarkYellow, false);
            if (!File.Exists(NetSquareConfigurationManager.Configuration.BlackListFilePath))
            {
                IPBlackList = new HashSet<string>();
                if (!File.Exists(NetSquareConfigurationManager.Configuration.BlackListFilePath))
                    File.WriteAllText(NetSquareConfigurationManager.Configuration.BlackListFilePath, SerializeBlackList(new HashSet<string>()));
                File.WriteAllText(NetSquareConfigurationManager.Configuration.BlackListFilePath, SerializeBlackList(IPBlackList));
            }
            IPBlackList = DeserializeBlackList(File.ReadAllText(NetSquareConfigurationManager.Configuration.BlackListFilePath));
            Writer.Write(IPBlackList.Count.ToString(), ConsoleColor.Green);
        }

        /// <summary>
        /// Executes the black list ip operation.
        /// </summary>
        public static void BlackListIP(string IP)
        {
            if (IPBlackList.Add(IP))
            {
                File.WriteAllText(NetSquareConfigurationManager.Configuration.BlackListFilePath, SerializeBlackList(IPBlackList));
                Writer.Write("BlackList ID : " + IP, ConsoleColor.Red);
            }
            else
                Writer.Write("IP already in BlackList : " + IP, ConsoleColor.DarkRed);
        }

        /// <summary>
        /// Executes the is black listed operation.
        /// </summary>
        public static bool IsBlackListed(Socket client)
        {
            string IP = ((IPEndPoint)client.RemoteEndPoint).Address.ToString();
            //Writer.Write("[" + IP + "] Checking blacklist... (" + DateTime.Now.ToString() + ")", ConsoleColor.DarkRed);

            if (IsLocalAddress(IP))
            {
                //Writer.Write("[" + IP + "] Local IP, it's OK.", ConsoleColor.Green);
                return false;
            }

            if (IsBlackListed_Local(IP))
            {
                Writer.Write("[" + IP + "] Local blacklisted.", ConsoleColor.Red);
                return true;
            }

            if (IsBlackListed_AbuseIPDB(IP))
            {
                Writer.Write("[" + IP + "] AbuseIPDB blacklisted.", ConsoleColor.Red);
                return true;
            }

            //Writer.Write("[" + IP + "] Not blacklisted.", ConsoleColor.Green);
            return false;
        }

        /// <summary>
        /// Executes the is black listed local operation.
        /// </summary>
        public static bool IsBlackListed_Local(string IP)
        {
            return IPBlackList.Contains(IP);
        }

        /// <summary>
        /// Check if the IP is blacklisted on AbuseIPDB
        /// TODO : Fix captcha issue
        /// </summary>
        /// <param name="IP"> IP to check </param>
        /// <returns> True if the IP is blacklisted, false otherwise </returns>
        public static bool IsBlackListed_AbuseIPDB(string IP)
        {
            try
            {
                string findedString = IP + "</span></b> was found in our database!";
                string rep = HttpClient.GetStringAsync("https://www.abuseipdb.com/check/" + IP).GetAwaiter().GetResult();
                return rep.Contains(findedString);
            }
            catch (Exception ex)
            {
                Writer.Write("Error while checking AbuseIPDB : " + ex.Message, ConsoleColor.DarkRed);
                return false;
            }
        }

        /// <summary>
        /// Initializes a new instance of the black list class.
        /// </summary>
        public static void BlackList(TcpClient client)
        {
            string IP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            BlackListIP(IP);
        }

        /// <summary>
        /// Executes the is local address operation.
        /// </summary>
        public static bool IsLocalAddress(string IP)
        {
            return IP.StartsWith("127.0.0.1") || IP.StartsWith("192.168.");
        }

        /// <summary>
        /// Creates the HTTP client used for AbuseIPDB lookups.
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) NetSquare/1.1");
            client.DefaultRequestHeaders.Referrer = new Uri("https://www.abuseipdb.com/");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            return client;
        }

        /// <summary>
        /// Executes the serialize black list operation.
        /// </summary>
        private static string SerializeBlackList(HashSet<string> blackList)
        {
            return NetSquareJson.Serialize(blackList);
        }

        /// <summary>
        /// Executes the deserialize black list operation.
        /// </summary>
        private static HashSet<string> DeserializeBlackList(string json)
        {
            string[] addresses = NetSquareJson.Deserialize<string[]>(json);
            return new HashSet<string>(addresses ?? new string[0]);
        }
    }
}
#endregion
