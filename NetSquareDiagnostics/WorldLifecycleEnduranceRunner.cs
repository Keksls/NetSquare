using NetSquare.Core;
using NetSquare.Server.Utils;
using NetSquare.Server.Worlds;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;

namespace NetSquareDiagnostics
{
    /// <summary>
    /// Repeatedly creates and expires worlds while tracking sessions and retained memory.
    /// </summary>
    internal static class WorldLifecycleEnduranceRunner
    {
        /// <summary>
        /// Runs the world lifecycle endurance scenario.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static int Run(string[] args)
        {
            int cycleCount = GetIntArgValue(args, "--cycles", 300);
            int warmupCycleCount = GetIntArgValue(args, "--warmup-cycles", 25);
            int clientCount = GetIntArgValue(args, "--clients", 4);
            int sampleEvery = GetIntArgValue(args, "--sample-every", 25);
            long maximumManagedGrowthBytes = GetLongArgValue(
                args,
                "--max-managed-growth-bytes",
                8L * 1024L * 1024L);
            int port = GetIntArgValue(args, "--port", GetFreeTcpPort());
            string resultsDirectory = GetArgValue(args, "--results-dir") ??
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results");

            NetSquare.Server.NetSquareServer server = null;
            Thread serverThread = null;
            List<NetSquare.Client.NetSquareClient> clients =
                new List<NetSquare.Client.NetSquareClient>();
            List<WeakReference> expiredWorlds = new List<WeakReference>();
            List<WorldLifecycleEnduranceSample> samples =
                new List<WorldLifecycleEnduranceSample>();
            int originalHandshakeLimit =
                NetSquare.Server.TcpListener.MaxConcurrentHandshakesPerAddress;

            try
            {
                Writer.SetOutputAsNull();
                Writer.StartDisplayLog();
                Writer.StopDisplayTitle();
                NetSquare.Server.TcpListener.MaxConcurrentHandshakesPerAddress =
                    Math.Max(originalHandshakeLimit, clientCount);

                server = new NetSquare.Server.NetSquareServer(NetSquareProtocoleType.TCP, true);
                server.DrawHeaderOverrideCallback = delegate { };
                serverThread = new Thread(
                    new ThreadStart(delegate { server.Start(port, true, false, false); }));
                serverThread.IsBackground = true;
                serverThread.Start();
                WaitUntil(delegate { return server.IsStarted; }, 5000, "endurance server did not start");

                for (int index = 0; index < clientCount; index++)
                    clients.Add(ConnectClient(port, index));
                WaitUntil(
                    delegate { return server.Clients.Count == clientCount; },
                    5000,
                    "network sessions did not stabilize");

                Console.WriteLine("World lifecycle endurance");
                Console.WriteLine(
                    "  cycles=" + cycleCount +
                    " warmup_cycles=" + warmupCycleCount +
                    " clients=" + clientCount +
                    " sample_every=" + sampleEvery);

                for (int cycle = 1; cycle <= warmupCycleCount; cycle++)
                    RunExpirationCycle(server, clients, expiredWorlds);

                CollectRetainedMemory();
                WorldLifecycleEnduranceSample baseline =
                    CaptureSample(server, 0);
                baseline.RetainedWorldCount = CountAlive(expiredWorlds);
                samples.Add(baseline);
                WriteSample("baseline", baseline);

                for (int cycle = 1; cycle <= cycleCount; cycle++)
                {
                    RunExpirationCycle(server, clients, expiredWorlds);
                    if (cycle % sampleEvery != 0 && cycle != cycleCount)
                        continue;

                    CollectRetainedMemory();
                    WorldLifecycleEnduranceSample sample =
                        CaptureSample(server, cycle);
                    sample.RetainedWorldCount = CountAlive(expiredWorlds);
                    samples.Add(sample);
                    WriteSample("expired", sample);
                }

                CollectRetainedMemory();
                int retainedWorldCount = CountAlive(expiredWorlds);
                WorldLifecycleEnduranceSample finalSample = samples[samples.Count - 1];
                finalSample.RetainedWorldCount = retainedWorldCount;
                long managedGrowth =
                    finalSample.ManagedMemoryBytes - baseline.ManagedMemoryBytes;
                double managedBytesPerCycle = cycleCount > 0
                    ? managedGrowth / (double)cycleCount
                    : 0d;
                string resultPath = WriteResults(resultsDirectory, samples);

                Console.WriteLine(
                    "  retained_worlds=" + retainedWorldCount +
                    " managed_growth_bytes=" + managedGrowth +
                    " managed_bytes_per_cycle=" +
                    managedBytesPerCycle.ToString("0.00", CultureInfo.InvariantCulture));
                Console.WriteLine("  results=" + resultPath);

                EnsureStableCounts(server, clientCount);
                if (retainedWorldCount > 1)
                    throw new InvalidOperationException(
                        retainedWorldCount + " expired worlds remain strongly reachable.");
                if (managedGrowth > maximumManagedGrowthBytes)
                    throw new InvalidOperationException(
                        "Managed retained memory grew by " + managedGrowth +
                        " bytes, above the configured " + maximumManagedGrowthBytes +
                        " byte limit.");

                Console.WriteLine("  PASS lifecycle counts and retained memory are bounded");
                return 0;
            }
            catch (Exception exception)
            {
                Console.WriteLine("World lifecycle endurance failed:");
                Console.WriteLine(exception);
                return 1;
            }
            finally
            {
                for (int index = 0; index < clients.Count; index++)
                {
                    try { clients[index].Disconnect(); }
                    catch { }
                }

                if (server != null)
                {
                    try { server.Stop(); }
                    catch { }
                }

                if (serverThread != null && serverThread.IsAlive)
                    serverThread.Join(1000);
                NetSquare.Server.TcpListener.MaxConcurrentHandshakesPerAddress =
                    originalHandshakeLimit;
            }
        }

        /// <summary>
        /// Creates a fully active world, joins every session, then expires it.
        /// </summary>
        /// <param name="server">Scenario server.</param>
        /// <param name="clients">Persistent client sessions.</param>
        /// <param name="expiredWorlds">Weak references used to detect retained worlds.</param>
        private static void RunExpirationCycle(
            NetSquare.Server.NetSquareServer server,
            List<NetSquare.Client.NetSquareClient> clients,
            List<WeakReference> expiredWorlds)
        {
            const ushort worldID = 1;
            NetSquareWorld world = server.Worlds.AddWorld(
                worldID,
                "lifecycle-endurance",
                (ushort)Math.Max(8, clients.Count + 2));
            world.StartSynchronizer(30);
            world.SetSpatializer(
                Spatializer.GetSimpleSpatializer(world, 20f, 30f, 100f));

            for (int index = 0; index < clients.Count; index++)
            {
                if (!TryJoinWorld(
                    clients[index],
                    worldID,
                    new NetsquareTransformFrame(index, 0f, 0f)))
                {
                    throw new InvalidOperationException(
                        "client " + index + " failed to join the endurance world.");
                }
                clients[index].WorldsManager.SendSynchFrame(
                    new NetsquareTransformFrame(index + 1f, 0f, 0f));
            }

            if (server.Worlds.SessionCount != clients.Count)
                throw new InvalidOperationException("world membership count did not reach the session count.");
            if (!server.Worlds.RemoveWorld(worldID))
                throw new InvalidOperationException("RemoveWorld returned false for an active world.");

            expiredWorlds.Add(new WeakReference(world));
            world = null;
            WaitUntil(
                delegate { return AreClientsOutsideWorld(clients); },
                5000,
                "clients did not observe world expiration");
            EnsureStableCounts(server, clients.Count);
        }

        /// <summary>
        /// Tries to join one connected client to a world.
        /// </summary>
        /// <param name="client">Client session.</param>
        /// <param name="worldID">Target world identifier.</param>
        /// <param name="spawn">Initial transform.</param>
        /// <returns>True when the server accepted the membership.</returns>
        private static bool TryJoinWorld(
            NetSquare.Client.NetSquareClient client,
            ushort worldID,
            NetsquareTransformFrame spawn)
        {
            bool result = false;
            using (ManualResetEventSlim completed = new ManualResetEventSlim(false))
            {
                client.WorldsManager.TryJoinWorld(worldID, spawn, delegate (bool joined)
                {
                    result = joined;
                    completed.Set();
                });
                return completed.Wait(5000) && result;
            }
        }

        /// <summary>
        /// Checks whether every client has cleared its local world membership.
        /// </summary>
        /// <param name="clients">Client sessions to inspect.</param>
        /// <returns>True when every client is outside a world.</returns>
        private static bool AreClientsOutsideWorld(
            List<NetSquare.Client.NetSquareClient> clients)
        {
            for (int index = 0; index < clients.Count; index++)
            {
                if (clients[index].WorldsManager.IsInWorld)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Validates stable post-expiration world, network session, and membership counts.
        /// </summary>
        /// <param name="server">Scenario server.</param>
        /// <param name="expectedSessionCount">Expected persistent network sessions.</param>
        private static void EnsureStableCounts(
            NetSquare.Server.NetSquareServer server,
            int expectedSessionCount)
        {
            if (server.Worlds.WorldCount != 0)
                throw new InvalidOperationException("world_count did not return to zero.");
            if (server.Worlds.SessionCount != 0)
                throw new InvalidOperationException("world membership count did not return to zero.");
            if (server.Clients.Count != expectedSessionCount)
                throw new InvalidOperationException(
                    "session_count changed during world expiration.");
        }

        /// <summary>
        /// Connects one persistent TCP client.
        /// </summary>
        /// <param name="port">Server TCP port.</param>
        /// <param name="index">Client index used in error messages.</param>
        /// <returns>Connected client.</returns>
        private static NetSquare.Client.NetSquareClient ConnectClient(int port, int index)
        {
            NetSquare.Client.NetSquareClient client =
                new NetSquare.Client.NetSquareClient(false);
            NetSquare.Client.ConnectionResult result = client.ConnectAsync(
                "127.0.0.1",
                port,
                NetSquareProtocoleType.TCP,
                false,
                10000).GetAwaiter().GetResult();
            if (result.IsConnected)
                return client;

            throw new InvalidOperationException(
                "endurance client " + index + " failed to connect: " +
                result.Status);
        }

        /// <summary>
        /// Forces full collections so samples represent retained managed memory.
        /// </summary>
        private static void CollectRetainedMemory()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Captures world, session, membership, and process memory counters.
        /// </summary>
        /// <param name="server">Scenario server.</param>
        /// <param name="cycle">Completed measured cycle count.</param>
        /// <returns>Captured sample.</returns>
        private static WorldLifecycleEnduranceSample CaptureSample(
            NetSquare.Server.NetSquareServer server,
            int cycle)
        {
            using (Process process = Process.GetCurrentProcess())
            {
                process.Refresh();
                return new WorldLifecycleEnduranceSample
                {
                    Cycle = cycle,
                    WorldCount = server.Worlds.WorldCount,
                    SessionCount = server.Clients.Count,
                    MembershipCount = server.Worlds.SessionCount,
                    ManagedMemoryBytes = GC.GetTotalMemory(false),
                    PrivateMemoryBytes = process.PrivateMemorySize64,
                    WorkingSetBytes = process.WorkingSet64
                };
            }
        }

        /// <summary>
        /// Writes one compact measurement line to the console.
        /// </summary>
        /// <param name="phase">Measurement phase.</param>
        /// <param name="sample">Captured counters.</param>
        private static void WriteSample(
            string phase,
            WorldLifecycleEnduranceSample sample)
        {
            Console.WriteLine(
                "  phase=" + phase +
                " cycle=" + sample.Cycle +
                " world_count=" + sample.WorldCount +
                " session_count=" + sample.SessionCount +
                " membership_count=" + sample.MembershipCount +
                " retained_worlds=" + sample.RetainedWorldCount +
                " managed_bytes=" + sample.ManagedMemoryBytes +
                " private_bytes=" + sample.PrivateMemoryBytes +
                " working_set_bytes=" + sample.WorkingSetBytes);
        }

        /// <summary>
        /// Writes all samples to a timestamped CSV file.
        /// </summary>
        /// <param name="resultsDirectory">Destination directory.</param>
        /// <param name="samples">Samples to persist.</param>
        /// <returns>Absolute result file path.</returns>
        private static string WriteResults(
            string resultsDirectory,
            List<WorldLifecycleEnduranceSample> samples)
        {
            Directory.CreateDirectory(resultsDirectory);
            string resultPath = Path.GetFullPath(Path.Combine(
                resultsDirectory,
                "world-lifecycle-endurance-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                ".csv"));
            List<string> lines = new List<string>(samples.Count + 1)
            {
                "cycle,world_count,session_count,membership_count,retained_worlds,managed_bytes,private_bytes,working_set_bytes"
            };

            for (int index = 0; index < samples.Count; index++)
            {
                WorldLifecycleEnduranceSample sample = samples[index];
                lines.Add(
                    sample.Cycle + "," +
                    sample.WorldCount + "," +
                    sample.SessionCount + "," +
                    sample.MembershipCount + "," +
                    sample.RetainedWorldCount + "," +
                    sample.ManagedMemoryBytes + "," +
                    sample.PrivateMemoryBytes + "," +
                    sample.WorkingSetBytes);
            }

            File.WriteAllLines(resultPath, lines);
            return resultPath;
        }

        /// <summary>
        /// Counts live expired worlds and removes collected weak references from the harness.
        /// </summary>
        /// <param name="worldReferences">Weak world references.</param>
        /// <returns>Number of still-alive worlds.</returns>
        private static int CountAlive(List<WeakReference> worldReferences)
        {
            int aliveCount = 0;
            for (int index = worldReferences.Count - 1; index >= 0; index--)
            {
                if (worldReferences[index].IsAlive)
                {
                    aliveCount++;
                    continue;
                }

                // Keep the endurance harness itself at constant retained memory.
                worldReferences.RemoveAt(index);
            }
            return aliveCount;
        }

        /// <summary>
        /// Gets one command-line argument value.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <returns>Value or null.</returns>
        private static string GetArgValue(string[] args, string name)
        {
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                    return args[index + 1];
            }
            return null;
        }

        /// <summary>
        /// Gets a positive integer command-line argument.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <param name="fallback">Fallback value.</param>
        /// <returns>Parsed positive value.</returns>
        private static int GetIntArgValue(string[] args, string name, int fallback)
        {
            int parsed;
            string value = GetArgValue(args, name);
            return value != null &&
                int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? Math.Max(1, parsed)
                : fallback;
        }

        /// <summary>
        /// Gets a positive long command-line argument.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <param name="fallback">Fallback value.</param>
        /// <returns>Parsed positive value.</returns>
        private static long GetLongArgValue(string[] args, string name, long fallback)
        {
            long parsed;
            string value = GetArgValue(args, name);
            return value != null &&
                long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? Math.Max(1L, parsed)
                : fallback;
        }

        /// <summary>
        /// Gets a currently unused local TCP port.
        /// </summary>
        /// <returns>Available port.</returns>
        private static int GetFreeTcpPort()
        {
            System.Net.Sockets.TcpListener listener =
                new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Waits until a condition succeeds or the timeout expires.
        /// </summary>
        /// <param name="condition">Condition to poll.</param>
        /// <param name="timeoutMilliseconds">Maximum wait.</param>
        /// <param name="message">Timeout error.</param>
        private static void WaitUntil(
            Func<bool> condition,
            int timeoutMilliseconds,
            string message)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (condition())
                    return;
                Thread.Sleep(5);
            }
            throw new TimeoutException(message);
        }
    }
}
