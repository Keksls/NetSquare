using NetSquare.Client;
using NetSquare.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Client_Test
{
    internal class Program
    {
        static ClientsMonitor.Form1 monitor;
        static void Main(string[] args)
        {
            Console.WriteLine("How many clients ? : ");
            int numPoints = 6;
            int.TryParse(Console.ReadLine(), out numPoints);
            Console.WriteLine("radius ? : ");
            float radius = 1;
            float.TryParse(Console.ReadLine(), out radius);
            List<ClientRoutine> clients = new List<ClientRoutine>();
            bool clientsCreated = false;
            float oscilation = radius * 0.8f;
            radius *= 0.01f;

            ClientStatisticsManager clientStatisticsManager = new ClientStatisticsManager();
            clientStatisticsManager.IntervalMs = 1000;
            clientStatisticsManager.OnGetStatistics += (statistics) =>
            {
                monitor?.UpdateStatistics(statistics);
            };
            clientStatisticsManager.Start();
            float speed = 0.1f;

            Thread t = new Thread(() =>
            {
                // Generate points around a circle
                for (int i = 0; i < numPoints; i++)
                {
                    double angle = 2 * Math.PI * i / numPoints;
                    double x = radius * Math.Cos(angle);
                    double y = radius * Math.Sin(angle);
                    ClientRoutine routine = new ClientRoutine();
                    routine.LineIndex = i;
                    routine.Start((float)x, 1f, (float)y);
                    clients.Add(routine);
                    while (!routine.client.WorldsManager.IsInWorld)
                        Thread.Sleep(2);
                    clientStatisticsManager.AddClient(routine.client);
                }
                clientsCreated = true;
            });
            t.Start();

            Stopwatch stopwatch = new Stopwatch();
            Stopwatch sendWatch = new Stopwatch();
            sendWatch.Start();
            bool sendNow = false;
            long enlapsed = 0;
            float lastFrameTime = -1f;
            NetSquareScheduler.AddAction("Client_Bot_Loop", 10f, true, () =>
            {
                if (clients.Count == 0 || ClientRoutine.Time < 1f)
                {
                    return;
                }
                sendNow = sendWatch.ElapsedMilliseconds > 200;
                if (sendNow)
                    sendWatch.Restart();

                if (lastFrameTime == -1f)
                {
                    lastFrameTime = ClientRoutine.Time;
                }
                float deltaTime = ClientRoutine.Time - lastFrameTime;
                lastFrameTime = ClientRoutine.Time;

                float time = ClientRoutine.Time * speed;
                // Clear the console
                stopwatch.Restart();
                for (int i = 0; i < clients.Count; i++)
                {
                    //float angle = time + i * 0.5f; // Vary speed based on index
                    //float radiusOffset = oscilation * (float)Math.Sin(2 * angle); // Oscillate radius
                    //float newX = (radius + radiusOffset) * (float)Math.Cos(angle);
                    //float newY = (radius + radiusOffset) * (float)Math.Sin(angle);

                    //// Rotate points around their own center
                    //float rotationAngle = time * 2 + i * 0.5f; // Adjust rotation speed
                    //float rotatedX = newX * (float)Math.Cos(rotationAngle) - newY * (float)Math.Sin(rotationAngle);
                    //float rotatedY = newX * (float)Math.Sin(rotationAngle) + newY * (float)Math.Cos(rotationAngle);

                    //// Scale points periodically
                    //float scale = 1f + 0.3f * (float)Math.Sin(time * 2 + i * 0.5f); // Oscillate scale
                    //newX *= scale;
                    //newY *= scale;

                    // Circular motion
                    float angle = time + i * 0.5f;
                    float x = radius * (float)Math.Cos(angle);
                    float y = radius * (float)Math.Sin(angle);
                    // Horizontal oscillation
                    x += 25f * (float)Math.Sin(time * 2 + i * 0.3f);
                    // Vertical oscillation
                    y += 25f * (float)Math.Cos(time * 2.5f + i * 0.4f);
                    clients[i].Update(sendNow, x, y);
                }
                if (sendNow)
                    enlapsed = stopwatch.ElapsedMilliseconds;
                string humanReadableTime = TimeSpan.FromMilliseconds(ClientRoutine.Time * 1000f).ToString(@"hh\:mm\:ss");
                Console.Title = "Sent : " + (sendNow ? "True" : "False") + " - Duration : " + enlapsed + "ms - T:" + humanReadableTime + " - deltaTime : " + (deltaTime * 1000f).ToString("f0") + " ms";
            });
            NetSquareScheduler.StartAction("Client_Bot_Loop");

            // Start Server Monitor
            //Application.EnableVisualStyles();
            //monitor = new ClientsMonitor.Form1();
            //Application.Run(monitor);

            Console.ReadKey();
        }
    }
}