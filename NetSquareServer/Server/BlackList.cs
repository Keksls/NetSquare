using NetSquare.Server.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace NetSquare.Server
{
    public static class BlackListManager
    {
        public static HashSet<string> IPBlackList = new HashSet<string>();

        public static void Initialize()
        {
            // Load Black list
            Writer.Write_Physical("Loading IP Blacklist...", ConsoleColor.DarkYellow, false);
            if (!File.Exists(NetSquareConfigurationManager.Configuration.BlackListFilePath))
            {
                IPBlackList = new HashSet<string>();
                if (!File.Exists(NetSquareConfigurationManager.Configuration.BlackListFilePath))
                    File.WriteAllText(NetSquareConfigurationManager.Configuration.BlackListFilePath, JsonConvert.SerializeObject(new HashSet<string>()));
                File.WriteAllText(NetSquareConfigurationManager.Configuration.BlackListFilePath, JsonConvert.SerializeObject(IPBlackList));
            }
            IPBlackList = JsonConvert.DeserializeObject<HashSet<string>>(File.ReadAllText(NetSquareConfigurationManager.Configuration.BlackListFilePath));
            Writer.Write(IPBlackList.Count.ToString(), ConsoleColor.Green);
        }

        public static void BlackListIP(string IP)
        {
            if (IPBlackList.Add(IP))
            {
                File.WriteAllText(NetSquareConfigurationManager.Configuration.BlackListFilePath, JsonConvert.SerializeObject(IPBlackList));
                Writer.Write("BlackList ID : " + IP, ConsoleColor.Red);
            }
            else
                Writer.Write("IP already in BlackList : " + IP, ConsoleColor.DarkRed);
        }

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
                WebClient client = new WebClient();
                string findedString = IP + "</span></b> was found in our database!";
                string rep = client.DownloadString("https://www.abuseipdb.com/check/" + IP);
                return rep.Contains(findedString);
            }
            catch (Exception ex)
            {
                Writer.Write("Error while checking AbuseIPDB : " + ex.Message, ConsoleColor.DarkRed);
                return false;
            }
        }

        public static void BlackList(TcpClient client)
        {
            string IP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            BlackListIP(IP);
        }

        public static bool IsLocalAddress(string IP)
        {
            return IP.StartsWith("127.0.0.1") || IP.StartsWith("192.168.");
        }
    }
}