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
            Console.WriteLine("How many lines ? : ");
            int nbLines = 2;
            int.TryParse(Console.ReadLine(), out nbLines);
            List<ClientRoutine> clients = new List<ClientRoutine>();
            bool clientsCreated = false;

            ClientStatisticsManager clientStatisticsManager = new ClientStatisticsManager();
            clientStatisticsManager.IntervalMs = 1000;
            clientStatisticsManager.OnGetStatistics += (statistics) =>
            {
                monitor?.UpdateStatistics(statistics);
            };
            clientStatisticsManager.Start();

            Thread t = new Thread(() =>
            {
                for (int i = 1; i <= nbLines; i++)
                {
                    int nbByLines = i * 2;

                    // add up line clients
                    for (int j = 0; j < nbByLines; j++)
                    {
                        ClientRoutine routine = new ClientRoutine();
                        routine.LineIndex = i;
                        routine.Start(5f, -i + j, 1f, i, -1, 1);
                        clients.Add(routine);
                        while (!routine.client.WorldsManager.IsInWorld)
                            Thread.Sleep(2);
                        clientStatisticsManager.AddClient(routine.client);
                    }

                    // add right line clients
                    for (int j = 0; j < nbByLines; j++)
                    {
                        ClientRoutine routine = new ClientRoutine();
                        routine.LineIndex = i;
                        routine.Start(5f, i, 1f, i - j, 1, 1);
                        clients.Add(routine);
                        while (!routine.client.WorldsManager.IsInWorld)
                            Thread.Sleep(2);
                        clientStatisticsManager.AddClient(routine.client);
                    }

                    // add down line clients
                    for (int j = 0; j < nbByLines; j++)
                    {
                        ClientRoutine routine = new ClientRoutine();
                        routine.LineIndex = i;
                        routine.Start(5f, i - j, 1f, -i, 1, -1);
                        clients.Add(routine);
                        while (!routine.client.WorldsManager.IsInWorld)
                            Thread.Sleep(2);
                        clientStatisticsManager.AddClient(routine.client);
                    }

                    // add left line clients
                    for (int j = 0; j < nbByLines; j++)
                    {
                        ClientRoutine routine = new ClientRoutine();
                        routine.LineIndex = i;
                        routine.Start(5f, -i, 1f, -i + j, -1, -1);
                        clients.Add(routine);
                        while (!routine.client.WorldsManager.IsInWorld)
                            Thread.Sleep(2);
                        clientStatisticsManager.AddClient(routine.client);
                    }
                }
                clientsCreated = true;
            });
            t.Start();

            Stopwatch stopwatch = new Stopwatch();
            Stopwatch sendWatch = new Stopwatch();
            sendWatch.Start();
            bool sendNow = false;
            long enlapsed = 0;
            float lastLoopTime = -1f;
            NetSquareScheduler.AddAction("Client_Bot_Loop", 10f, true, () =>
            {
                if (clients.Count == 0 || !clientsCreated)
                {
                    return;
                }
                sendNow = sendWatch.ElapsedMilliseconds > 200;
                if (sendNow)
                    sendWatch.Restart();

                if (lastLoopTime == -1f)
                {
                    lastLoopTime = ClientRoutine.Time;
                }
                float deltaTime = ClientRoutine.Time - lastLoopTime;
                lastLoopTime = ClientRoutine.Time;

                stopwatch.Restart();
                for (int i = 0; i < clients.Count; i++)
                {
                    clients[i].Update(sendNow, deltaTime);
                }
                if (sendNow)
                    enlapsed = stopwatch.ElapsedMilliseconds;
                string humanReadableTime = TimeSpan.FromMilliseconds(ClientRoutine.Time * 1000f).ToString(@"hh\:mm\:ss");
                Console.Title = "Sent : " + (sendNow ? "True" : "False") + " - Duration : " + enlapsed + "ms - T:" + humanReadableTime + " - deltaTime : " + (deltaTime * 1000f).ToString("f0") + " ms";
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