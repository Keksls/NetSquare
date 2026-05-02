using NetSquare.Core;
using NetSquare.Server.Utils;
using NetSquare.Server.Worlds;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
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
            int clientCount = GetIntArgValue(args, "--clients", 16);
            int durationSeconds = GetIntArgValue(args, "--duration-seconds", 10);
            int tickMs = GetIntArgValue(args, "--tick-ms", 50);
            int port = GetIntArgValue(args, "--port", GetFreeTcpPort());
            ushort worldID = (ushort)Math.Min(ushort.MaxValue, GetIntArgValue(args, "--world-id", 1));
            bool useUdp = HasArg(args, "--udp");
            bool simpleSpatializer = HasArg(args, "--simple-spatializer");

            NetSquare.Server.NetSquareServer server = null;
            Thread serverThread = null;
            List<NetSquare.Client.NetSquareClient> clients = new List<NetSquare.Client.NetSquareClient>();
            long sentFrames = 0;
            long receivedFrames = 0;

            try
            {
                Writer.SetOutputAsNull();
                Writer.StartDisplayLog();
                Writer.StopDisplayTitle();

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

                Thread.Sleep(500);
                Console.WriteLine("  sent frames=" + sentFrames + " received frames=" + receivedFrames);
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
            }
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
            float lane = (index % Math.Max(1, count / 4 + 1)) * 2.5f;
            float angle = time * 0.8f + index * 0.35f;
            float radius = 8f + (index % 5);
            float x = (float)Math.Cos(angle) * radius + lane;
            float z = (float)Math.Sin(angle) * radius - lane;
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
            ManualResetEventSlim connected = new ManualResetEventSlim(false);
            ManualResetEventSlim failed = new ManualResetEventSlim(false);
            client.OnConnected += delegate { connected.Set(); };
            client.OnConnectionFail += delegate { failed.Set(); };
            client.Connect("127.0.0.1", port, useUdp ? NetSquareProtocoleType.TCP_AND_UDP : NetSquareProtocoleType.TCP, useUdp);

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < 10000)
            {
                if (connected.IsSet)
                    return client;
                if (failed.IsSet)
                    throw new InvalidOperationException("scenario client " + index + " failed to connect");

                Thread.Sleep(5);
            }

            throw new TimeoutException("scenario client " + index + " did not connect");
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
