using NetSquare.Client;
using NetSquare.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Client_Test
{
    /// <summary>
    /// Runs the interactive multi-client load test.
    /// </summary>
    internal class Program
    {
        private static ClientsMonitor.Form1 monitor;

        #region Entry Point
        /// <summary>
        /// Starts sequential clients and keeps their movement loop active until a key is pressed.
        /// </summary>
        /// <param name="args">Unused command-line arguments.</param>
        private static void Main(string[] args)
        {
            // Validate interactive inputs so an empty or malformed value keeps a useful default.
            Console.WriteLine("How many clients ? : ");
            int numPoints;
            if (!int.TryParse(Console.ReadLine(), out numPoints) || numPoints <= 0)
                numPoints = 6;

            Console.WriteLine("radius ? : ");
            float radius;
            if (!float.TryParse(Console.ReadLine(), out radius) || radius <= 0f)
                radius = 1f;
            radius *= 0.01f;

            List<ClientRoutine> clients = new List<ClientRoutine>();
            object clientsLock = new object();
            ManualResetEventSlim startupCompleted = new ManualResetEventSlim(false);
            ClientStatisticsManager clientStatisticsManager = new ClientStatisticsManager();
            clientStatisticsManager.IntervalMs = 1000;
            clientStatisticsManager.OnGetStatistics += statistics =>
            {
                monitor?.UpdateStatistics(statistics);
            };
            clientStatisticsManager.Start();

            Thread connectionThread = new Thread(() =>
            {
                // A failed bot is reported and skipped so later clients are still exercised.
                int connectedClients = 0;
                int failedClients = 0;
                for (int i = 0; i < numPoints; i++)
                {
                    double angle = 2d * Math.PI * i / numPoints;
                    double x = radius * Math.Cos(angle);
                    double z = radius * Math.Sin(angle);
                    ClientRoutine routine = new ClientRoutine
                    {
                        LineIndex = i
                    };

                    if (!routine.Start((float)x, 1f, (float)z))
                    {
                        failedClients++;
                        Console.WriteLine(
                            "Startup progress: " + connectedClients + " connected, " +
                            failedClients + " failed, " + (i + 1) + "/" + numPoints + " attempted.");
                        continue;
                    }

                    lock (clientsLock)
                        clients.Add(routine);
                    clientStatisticsManager.AddClient(routine.client);
                    connectedClients++;
                    Console.WriteLine(
                        "Startup progress: " + connectedClients + " connected, " +
                        failedClients + " failed, " + (i + 1) + "/" + numPoints + " attempted.");
                }

                Console.WriteLine(
                    "Startup completed: " + connectedClients + "/" + numPoints +
                    " clients connected, " + failedClients + " failed.");
                startupCompleted.Set();
            });
            connectionThread.IsBackground = true;
            connectionThread.Start();

            StartMovementLoop(clients, clientsLock, startupCompleted, radius);

            // Keep the interactive test alive while background clients exchange frames.
            Console.ReadKey();
        }
        #endregion

        #region Movement
        /// <summary>
        /// Starts the shared movement and synchronization scheduler action.
        /// </summary>
        /// <param name="clients">Successfully initialized clients.</param>
        /// <param name="clientsLock">Lock protecting list iteration and additions.</param>
        /// <param name="startupCompleted">Signals that every requested client has been attempted.</param>
        /// <param name="radius">Movement radius.</param>
        private static void StartMovementLoop(
            List<ClientRoutine> clients,
            object clientsLock,
            ManualResetEventSlim startupCompleted,
            float radius)
        {
            // One scheduler action updates every ready bot while list access remains race-free.
            const float speed = 0.1f;
            Stopwatch stopwatch = new Stopwatch();
            Stopwatch sendWatch = Stopwatch.StartNew();
            bool sendNow = false;
            long elapsed = 0;
            float lastFrameTime = -1f;

            NetSquareScheduler.AddAction("Client_Bot_Loop", 10f, true, () =>
            {
                // Keep connection measurements isolated from movement broadcast load.
                if (!startupCompleted.IsSet)
                    return;

                sendNow = sendWatch.ElapsedMilliseconds > 200;
                if (sendNow)
                    sendWatch.Restart();

                if (lastFrameTime < 0f)
                    lastFrameTime = ClientRoutine.Time;
                float deltaTime = ClientRoutine.Time - lastFrameTime;
                lastFrameTime = ClientRoutine.Time;
                float time = ClientRoutine.Time * speed;

                stopwatch.Restart();
                int activeClients;
                lock (clientsLock)
                {
                    activeClients = clients.Count;
                    for (int i = 0; i < clients.Count; i++)
                    {
                        // Circular movement plus two deterministic oscillations stresses transform traffic.
                        float angle = time + i * 0.5f;
                        float x = radius * (float)Math.Cos(angle);
                        float z = radius * (float)Math.Sin(angle);
                        x += 25f * (float)Math.Sin(time * 2f + i * 0.3f);
                        z += 25f * (float)Math.Cos(time * 2.5f + i * 0.4f);
                        clients[i].Update(sendNow, x, z);
                    }
                }

                if (sendNow)
                    elapsed = stopwatch.ElapsedMilliseconds;
                string humanReadableTime = TimeSpan
                    .FromMilliseconds(ClientRoutine.Time * 1000f)
                    .ToString(@"hh\:mm\:ss");
                Console.Title =
                    "Clients: " + activeClients +
                    " - Sent: " + (sendNow ? "True" : "False") +
                    " - Duration: " + elapsed + "ms" +
                    " - T: " + humanReadableTime +
                    " - deltaTime: " + (deltaTime * 1000f).ToString("f0") + " ms";
            });
            NetSquareScheduler.StartAction("Client_Bot_Loop");
        }
        #endregion
    }
}