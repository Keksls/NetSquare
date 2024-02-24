using NetSquare.Core;
using NetSquareClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace Client_Test
{
    internal class Program
    {
        static ClientsMonitor.Form1 monitor;
        static void Main(string[] args)
        {
            Console.WriteLine("How many clients : ");
            string nbClients = Console.ReadLine();
            int nbCLients = 10;
            int.TryParse(nbClients, out nbCLients);
            List<ClientRoutine> clients = new List<ClientRoutine>();

            ClientStatisticsManager clientStatisticsManager = new ClientStatisticsManager();
            clientStatisticsManager.IntervalMs = 1000;
            clientStatisticsManager.OnGetStatistics += (statistics) =>
            {
                monitor?.UpdateStatistics(statistics);
            };
            clientStatisticsManager.Start();

            Thread t = new Thread(() =>
            {
                for (int i = 0; i < nbCLients; i++)
                {
                    ClientRoutine routine = new ClientRoutine();
                    routine.Start(5f + (i % 10));
                    clients.Add(routine);
                    while (!routine.client.WorldsManager.IsInWorld)
                        Thread.Sleep(10);
                    clientStatisticsManager.AddClient(routine.client);
                }
            });
            t.Start();

            Stopwatch stopwatch = new Stopwatch();
            Stopwatch sendWatch = new Stopwatch();
            sendWatch.Start();
            bool sendNow = false;
            long enlapsed = 0;
            NetSquareScheduler.AddAction("Client_Bot_Loop", 10f, true, () =>
            {
                if (clients.Count == 0)
                {
                    return;
                }
                sendNow = sendWatch.ElapsedMilliseconds > 500;
                if (sendNow)
                    sendWatch.Restart();

                stopwatch.Restart();
                for (int i = 0; i < clients.Count; i++)
                {
                    clients[i].TestSync(sendNow);
                }
                if (sendNow)
                    enlapsed = stopwatch.ElapsedMilliseconds;
                string humanReadableTime = TimeSpan.FromMilliseconds(clients[0].Time * 1000f).ToString(@"hh\:mm\:ss");
                Console.Title = "Sent : " + (sendNow ? "True" : "False") + " - Duration : " + enlapsed + "ms - T:" + humanReadableTime;
            });
            NetSquareScheduler.StartAction("Client_Bot_Loop");

            // Start Server Monitor
            Application.EnableVisualStyles();
            monitor = new ClientsMonitor.Form1();
            Application.Run(monitor);

            Console.ReadKey();
        }
    }
}