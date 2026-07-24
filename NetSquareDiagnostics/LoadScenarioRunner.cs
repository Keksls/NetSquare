using NetSquare.Core;
using NetSquare.Server.Utils;
using NetSquare.Server.Worlds;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;

#region Source
namespace NetSquareDiagnostics
{
    /// <summary>
    /// Runs automated client load scenarios against a local NetSquare server.
    /// </summary>
    internal static class LoadScenarioRunner
    {
        #region Public Methods
        /// <summary>
        /// Runs a load scenario from command-line arguments.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static int Run(string[] args)
        {
            if (HasArg(args, "--spatial-matrix"))
                return RunSpatialMatrix(args);

            int clientCount = GetIntArgValue(args, "--clients", 16);
            int durationSeconds = GetIntArgValue(args, "--duration-seconds", 10);
            int tickMs = GetIntArgValue(args, "--tick-ms", 50);
            bool useUdp = HasArg(args, "--udp");
            int port = GetIntArgValue(args, "--port", useUdp ? GetFreeTcpUdpPortPair() : GetFreeTcpPort());
            ushort worldID = (ushort)Math.Min(ushort.MaxValue, GetIntArgValue(args, "--world-id", 1));
            bool simpleSpatializer = HasArg(args, "--simple-spatializer");

            NetSquare.Server.NetSquareServer server = null;
            Thread serverThread = null;
            List<NetSquare.Client.NetSquareClient> clients = new List<NetSquare.Client.NetSquareClient>();
            long sentFrames = 0;
            long receivedFrames = 0;
            int originalPerAddressHandshakeLimit = NetSquare.Server.TcpListener.MaxConcurrentHandshakesPerAddress;

            try
            {
                Writer.SetOutputAsNull();
                Writer.StartDisplayLog();
                Writer.StopDisplayTitle();

                // Local bots share one IP; keep the anti-DoS limit from contaminating spatialization measurements.
                NetSquare.Server.TcpListener.MaxConcurrentHandshakesPerAddress =
                    Math.Max(originalPerAddressHandshakeLimit, clientCount);
                server = new NetSquare.Server.NetSquareServer(useUdp ? NetSquareProtocoleType.TCP_AND_UDP : NetSquareProtocoleType.TCP, true);
                server.DrawHeaderOverrideCallback = delegate { };
                NetSquareWorld world = server.Worlds.AddWorld(worldID, "load-scenario", (ushort)Math.Min(ushort.MaxValue, Math.Max(128, clientCount + 8)));
                ConfigureSpatializer(world, simpleSpatializer);

                serverThread = new Thread(new ThreadStart(delegate { server.Start(port, true, false, false); }));
                serverThread.IsBackground = true;
                serverThread.Start();
                WaitUntil(delegate { return server.IsStarted; }, 5000, "load scenario server did not start");

                Console.WriteLine("Load scenario");
                Console.WriteLine("  port=" + port + " clients=" + clientCount + " duration=" + durationSeconds + "s tick=" + tickMs + "ms udp=" + useUdp);

                for (int i = 0; i < clientCount; i++)
                {
                    NetSquare.Client.NetSquareClient client = ConnectClient(port, useUdp, i);
                    client.WorldsManager.OnReceiveSynchFrames += delegate (uint clientID, INetSquareSynchFrame[] frames)
                    {
                        if (frames != null)
                            Interlocked.Add(ref receivedFrames, frames.Length);
                    };
                    clients.Add(client);
                }

                for (int i = 0; i < clients.Count; i++)
                {
                    NetsquareTransformFrame spawn = CreateMovementFrame(i, 0f, clients.Count);
                    if (!TryJoinWorld(clients[i], worldID, spawn))
                        throw new InvalidOperationException("client " + i + " failed to join world");
                }

                int gen0Before = GC.CollectionCount(0);
                int gen1Before = GC.CollectionCount(1);
                int gen2Before = GC.CollectionCount(2);
                long memoryBefore = GC.GetTotalMemory(false);
                TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < durationSeconds)
                {
                    float time = (float)stopwatch.Elapsed.TotalSeconds;
                    for (int i = 0; i < clients.Count; i++)
                    {
                        clients[i].WorldsManager.SendSynchFrame(CreateMovementFrame(i, time, clients.Count));
                        sentFrames++;
                    }

                    Thread.Sleep(tickMs);
                }

                stopwatch.Stop();
                TimeSpan cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;
                long memoryAfter = GC.GetTotalMemory(false);
                Thread.Sleep(500);
                double elapsedSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                Console.WriteLine("  sent frames=" + sentFrames + " received frames=" + receivedFrames);
                Console.WriteLine("  sent frames/s=" + (sentFrames / elapsedSeconds).ToString("0", CultureInfo.InvariantCulture));
                Console.WriteLine("  process cpu ms=" + (cpuAfter - cpuBefore).TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture));
                Console.WriteLine("  gc0=" + (GC.CollectionCount(0) - gen0Before) +
                    " gc1=" + (GC.CollectionCount(1) - gen1Before) +
                    " gc2=" + (GC.CollectionCount(2) - gen2Before));
                Console.WriteLine("  managed memory delta=" + (memoryAfter - memoryBefore));
                Console.WriteLine("  world clients=" + world.Clients.Count + " pending=" + (world.Spatializer != null ? world.Spatializer.CreateSnapshot().PendingFrameCount : 0));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Load scenario failed:");
                Console.WriteLine(ex);
                return 1;
            }
            finally
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    try { clients[i].Disconnect(); } catch { }
                }

                if (server != null)
                {
                    try
                    {
                        if (server.Worlds != null)
                            foreach (NetSquareWorld world in server.Worlds.Worlds.Values)
                                world.SetSpatializer(null);
                    }
                    catch { }
                    try { server.Stop(); } catch { }
                }

                if (serverThread != null && serverThread.IsAlive)
                    serverThread.Join(1000);
                NetSquare.Server.TcpListener.MaxConcurrentHandshakesPerAddress = originalPerAddressHandshakeLimit;
            }
        }
        /// <summary>
        /// Runs comparable simple and chunked spatialization scenarios at increasing client counts.
        /// </summary>
        /// <param name="args">Original command-line arguments.</param>
        /// <returns>Zero when every scenario succeeds.</returns>
        private static int RunSpatialMatrix(string[] args)
        {
            int[] clientCounts = { 100, 500, 1000 };
            for (int spatializerIndex = 0; spatializerIndex < 2; spatializerIndex++)
            {
                bool simpleSpatializer = spatializerIndex == 0;
                for (int countIndex = 0; countIndex < clientCounts.Length; countIndex++)
                {
                    List<string> scenarioArguments = CopyScenarioArguments(args);
                    scenarioArguments.Add("--clients");
                    scenarioArguments.Add(clientCounts[countIndex].ToString(CultureInfo.InvariantCulture));
                    if (simpleSpatializer)
                        scenarioArguments.Add("--simple-spatializer");

                    Console.WriteLine();
                    Console.WriteLine(
                        "Spatial matrix: " +
                        (simpleSpatializer ? "simple" : "chunked") +
                        " clients=" + clientCounts[countIndex]);
                    int exitCode = Run(scenarioArguments.ToArray());
                    if (exitCode != 0)
                        return exitCode;
                }
            }
            return 0;
        }

        /// <summary>
        /// Copies matrix arguments while removing options that must be unique per generated scenario.
        /// </summary>
        /// <param name="args">Original command-line arguments.</param>
        /// <returns>Arguments safe to reuse for one generated scenario.</returns>
        private static List<string> CopyScenarioArguments(string[] args)
        {
            List<string> copied = new List<string>();
            if (args == null)
                return copied;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.Equals(argument, "--spatial-matrix", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(argument, "--simple-spatializer", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(argument, "--clients", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(argument, "--port", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    continue;
                }
                copied.Add(argument);
            }
            return copied;
        }
        #endregion

        #region Scenario
        /// <summary>
        /// Configures the test spatializer.
        /// </summary>
        /// <param name="world">World to configure.</param>
        /// <param name="simpleSpatializer">Whether to use the simple spatializer.</param>
        private static void ConfigureSpatializer(NetSquareWorld world, bool simpleSpatializer)
        {
            if (simpleSpatializer)
                world.SetSpatializer(Spatializer.GetSimpleSpatializer(world, 10f, 20f, 30f, 2f));
            else
                world.SetSpatializer(Spatializer.GetChunkedSpatializer(world, 10f, 20f, 12f, -128f, -128f, 128f, 128f, 1f));
        }

        /// <summary>
        /// Creates a deterministic movement frame for a bot.
        /// </summary>
        /// <param name="index">Bot index.</param>
        /// <param name="time">Scenario time.</param>
        /// <param name="count">Bot count.</param>
        /// <returns>Movement frame.</returns>
        private static NetsquareTransformFrame CreateMovementFrame(int index, float time, int count)
        {
            int safeCount = Math.Max(1, count);
            int columns = (int)Math.Ceiling(Math.Sqrt(safeCount));
            int rows = (safeCount + columns - 1) / columns;
            const float spacing = 4f;
            float baseX = ((index % columns) - ((columns - 1) * 0.5f)) * spacing;
            float baseZ = ((index / columns) - ((rows - 1) * 0.5f)) * spacing;
            float angle = time * 0.8f + index * 0.35f;
            float radius = 1f + ((index % 5) * 0.25f);
            float x = baseX + ((float)Math.Cos(angle) * radius);
            float z = baseZ + ((float)Math.Sin(angle) * radius);
            return new NetsquareTransformFrame(x, 0f, z, 0f, 0f, 0f, 1f, time);
        }

        /// <summary>
        /// Tries to join a client to a world.
        /// </summary>
        /// <param name="client">Client to join.</param>
        /// <param name="worldID">World id.</param>
        /// <param name="spawn">Spawn transform.</param>
        /// <returns>True when joined.</returns>
        private static bool TryJoinWorld(NetSquare.Client.NetSquareClient client, ushort worldID, NetsquareTransformFrame spawn)
        {
            bool result = false;
            ManualResetEventSlim completed = new ManualResetEventSlim(false);
            client.WorldsManager.TryJoinWorld(worldID, spawn, delegate (bool ok)
            {
                result = ok;
                completed.Set();
            });

            return completed.Wait(5000) && result;
        }
        #endregion

        #region Client
        /// <summary>
        /// Connects one scenario client.
        /// </summary>
        /// <param name="port">Server port.</param>
        /// <param name="useUdp">Whether synchronization uses UDP.</param>
        /// <param name="index">Client index.</param>
        /// <returns>Connected client.</returns>
        private static NetSquare.Client.NetSquareClient ConnectClient(int port, bool useUdp, int index)
        {
            NetSquare.Client.NetSquareClient client = new NetSquare.Client.NetSquareClient(false);
            NetSquare.Client.ConnectionResult result = client.ConnectAsync(
                "127.0.0.1",
                port,
                useUdp ? NetSquareProtocoleType.TCP_AND_UDP : NetSquareProtocoleType.TCP,
                useUdp,
                10000).GetAwaiter().GetResult();
            if (result.IsConnected)
                return client;

            string detail = result.Status.ToString();
            if (result.RejectionInfo != null)
                detail += ": " + result.RejectionInfo.Reason;
            if (result.Exception != null)
                detail += ": " + result.Exception.Message;
            throw new InvalidOperationException("scenario client " + index + " failed to connect: " + detail);
        }
        #endregion

        #region Args
        /// <summary>
        /// Checks whether an argument exists.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <returns>True when found.</returns>
        private static bool HasArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Gets an integer argument value.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <param name="fallback">Fallback value.</param>
        /// <returns>Parsed value.</returns>
        private static int GetIntArgValue(string[] args, string name, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    continue;

                int parsed;
                if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    return Math.Max(1, parsed);
            }

            return fallback;
        }
        #endregion

        #region Wait
        /// <summary>
        /// Gets a free local TCP port.
        /// </summary>
        /// <returns>Free TCP port.</returns>
        private static int GetFreeTcpPort()
        {
            System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Gets adjacent free local ports for the scenario TCP listener and its UDP transport.
        /// </summary>
        /// <returns>Free TCP port whose following port is also available for UDP.</returns>
        private static int GetFreeTcpUdpPortPair()
        {
            for (int attempt = 0; attempt < 128; attempt++)
            {
                System.Net.Sockets.TcpListener tcpListener =
                    new System.Net.Sockets.TcpListener(IPAddress.Any, 0);
                UdpClient udpListener = null;
                try
                {
                    tcpListener.Start();
                    int tcpPort = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
                    if (tcpPort >= ushort.MaxValue)
                        continue;

                    udpListener = new UdpClient(new IPEndPoint(IPAddress.Any, tcpPort + 1));
                    return tcpPort;
                }
                catch (SocketException)
                {
                    // Retry when the adjacent UDP port is already owned by another process.
                }
                finally
                {
                    if (udpListener != null)
                        udpListener.Close();
                    tcpListener.Stop();
                }
            }

            throw new InvalidOperationException("Unable to reserve adjacent TCP and UDP ports for diagnostics.");
        }

        /// <summary>
        /// Waits until a condition becomes true.
        /// </summary>
        /// <param name="condition">Condition to poll.</param>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <param name="message">Timeout message.</param>
        private static void WaitUntil(Func<bool> condition, int timeoutMs, string message)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                    return;
                Thread.Sleep(10);
            }
            throw new TimeoutException(message);
        }
        #endregion
    }
}
#endregion
