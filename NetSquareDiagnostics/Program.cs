using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquare.Server.Utils;
using NetSquare.Server.Worlds;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

#region Source
namespace NetSquareDiagnostics
{
    /// <summary>
    /// Represents the program component.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Defines the echo head id constant.
        /// </summary>
        private const ushort EchoHeadId = 65000;
        /// <summary>
        /// Defines the tcp smoke head id constant.
        /// </summary>
        private const ushort TcpSmokeHeadId = 64001;
        /// <summary>
        /// Defines the udp smoke head id constant.
        /// </summary>
        private const ushort UdpSmokeHeadId = 64002;
        /// <summary>
        /// Defines the packing smoke head id constant.
        /// </summary>
        private const ushort PackingSmokeHeadId = 64003;
        /// <summary>
        /// Defines the world broadcast head id constant.
        /// </summary>
        private const ushort WorldBroadcastHeadId = 64004;
        /// <summary>
        /// Defines the world sync head id constant.
        /// </summary>
        private const ushort WorldSyncHeadId = 64005;
        /// <summary>
        /// Stores the benchmark results value.
        /// </summary>
        private static readonly List<BenchmarkResult> BenchmarkResults = new List<BenchmarkResult>();
        /// <summary>
        /// Stores the passed tests value.
        /// </summary>
        private static int passedTests;
        /// <summary>
        /// Stores the failed tests value.
        /// </summary>
        private static int failedTests;

        [STAThread]
        /// <summary>
        /// Executes the main operation.
        /// </summary>
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
                return RunDiagnostics(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DiagnosticsWindow());
            return 0;
        }

        /// <summary>
        /// Executes the run diagnostics operation.
        /// </summary>
        internal static int RunDiagnostics(string[] args)
        {
            passedTests = 0;
            failedTests = 0;
            BenchmarkResults.Clear();

            bool testsOnly = HasArg(args, "--tests-only");
            bool benchOnly = HasArg(args, "--bench-only");
            bool fullLoad = HasArg(args, "--full-load");
            int benchmarkRuns = GetIntArgValue(args, "--runs", GetIntArgValue(args, "--benchmark-runs", 1));
            string resultsDir = GetArgValue(args, "--results-dir") ?? GetDefaultResultsDir();

            try
            {
                if (HasArg(args, "--generate-server-config"))
                {
                    string configPath = GetArgValue(args, "--generate-server-config") ?? Path.Combine(Environment.CurrentDirectory, "netsquare.generated.config.json");
                    NetSquare.Server.NetSquareServerConfigGenerator.WriteDefault(configPath);
                    Console.WriteLine("Generated server config JSON: " + configPath);
                    return 0;
                }

                if (HasArg(args, "--load-scenario"))
                    return LoadScenarioRunner.Run(args);

                if (!benchOnly)
                    RunReliabilityTests();

                if (!testsOnly)
                {
                    RunBenchmarks(fullLoad, benchmarkRuns);
                    WriteBenchmarkResults(resultsDir);
                }

                Console.WriteLine();
                Console.WriteLine("Diagnostics completed.");
                return failedTests == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Fatal diagnostics error:");
                Console.WriteLine(ex);
                return 1;
            }
        }

        /// <summary>
        /// Executes the has arg operation.
        /// </summary>
        private static bool HasArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Executes the get arg value operation.
        /// </summary>
        private static string GetArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        /// <summary>
        /// Executes the get int arg value operation.
        /// </summary>
        private static int GetIntArgValue(string[] args, string name, int fallback)
        {
            string value = GetArgValue(args, name);
            int parsed;
            if (value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return Math.Max(1, parsed);
            return fallback;
        }

        /// <summary>
        /// Executes the get default results dir operation.
        /// </summary>
        private static string GetDefaultResultsDir()
        {
            string workspaceDiagnostics = Path.Combine(Environment.CurrentDirectory, "NetSquareDiagnostics");
            if (Directory.Exists(workspaceDiagnostics))
                return Path.Combine(workspaceDiagnostics, "results");
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results");
        }

        /// <summary>
        /// Executes the run reliability tests operation.
        /// </summary>
        private static void RunReliabilityTests()
        {
            Console.WriteLine("Reliability tests");
            RunTest("serializer roundtrip", TestSerializerRoundtrip);
            RunTest("synch frame sequence roundtrip", TestSynchFrameSequenceRoundtrip);
            RunTest("invalid frame rejected", TestInvalidFrameRejected);
            RunTest("datagram length mismatch rejected", TestDatagramMismatchRejected);
            RunTest("SetType uses argument", TestSetType);
            RunTest("TCP fragmented receive", TestTcpFragmentedReceive);
            RunTest("TCP oversized frame disconnects", TestTcpOversizedFrameDisconnects);
            RunTest("TCP reset probe disconnects cleanly", TestTcpResetProbeDisconnectsCleanly);
            RunTest("manual client disconnect notice", TestManualClientDisconnectSendsNotice);
            RunTest("server stop notifies clients", TestServerStopNotifiesClients);
            RunTest("TCP concurrent sends stay framed", TestTcpConcurrentSends);
            RunTest("NetworkMessage packing", TestPackingRoundtrip);
            RunTest("server/client settings", TestServerClientSettings);
            RunTest("TCP client/server message", TestTcpClientServerMessage);
            RunTest("client/server time synchronization", TestClientServerTimeSynchronization);
            RunTest("automatic client/server time synchronization", TestAutomaticClientServerTimeSynchronization);
            RunTest("UDP client/server message", TestUdpClientServerMessage);
            RunTest("UDP client local port conflict", TestUdpClientLocalPortConflict);
            RunTest("UDP replace client ID routing", TestUdpReplaceClientIdRouting);
            RunTest("world join broadcast sync leave", TestWorldJoinBroadcastSyncLeave);
            RunTest("world transform cache updates from frames", TestWorldTransformCacheUpdatesFromFrames);
            RunTest("simple spatializer visibility", TestSimpleSpatializerVisibility);
            RunTest("chunked spatializer visibility", TestChunkedSpatializerVisibility);
            Console.WriteLine("Tests: " + passedTests + " passed, " + failedTests + " failed");
        }

        /// <summary>
        /// Executes the run test operation.
        /// </summary>
        private static void RunTest(string name, Action test)
        {
            try
            {
                test();
                passedTests++;
                Console.WriteLine("  PASS " + name);
            }
            catch (Exception ex)
            {
                failedTests++;
                Console.WriteLine("  FAIL " + name + " - " + ex.Message);
            }
        }

        /// <summary>
        /// Executes the test serializer roundtrip operation.
        /// </summary>
        private static void TestSerializerRoundtrip()
        {
            NetworkMessage message = new NetworkMessage(42, 7)
                .Set(123456)
                .Set(12.5f)
                .Set(42.25d)
                .Set("hello")
                .Set(new byte[] { 1, 2, 3, 4 })
                .Set(new int[] { 10, 20, 30 })
                .Set(new float[] { 1.5f, 2.5f });

            byte[] data = message.Serialize();
            NetworkMessage copy = new NetworkMessage(data);

            Assert(copy.HeadID == 42, "head id mismatch");
            Assert(copy.ClientID == 7, "client id mismatch");
            Assert(copy.Serializer.GetInt() == 123456, "int mismatch");
            Assert(Math.Abs(copy.Serializer.GetFloat() - 12.5f) < 0.0001f, "float mismatch");
            Assert(Math.Abs(copy.Serializer.GetDouble() - 42.25d) < 0.0001d, "double mismatch");
            Assert(copy.Serializer.GetString() == "hello", "string mismatch");
            byte[] bytes = copy.Serializer.GetByteArray();
            Assert(bytes.Length == 4 && bytes[0] == 1 && bytes[3] == 4, "byte array mismatch");
            int[] ints = copy.Serializer.GetIntArray();
            Assert(ints.Length == 3 && ints[0] == 10 && ints[2] == 30, "int array mismatch");
            float[] floats = copy.Serializer.GetFloatArray();
            Assert(floats.Length == 2 && Math.Abs(floats[1] - 2.5f) < 0.0001f, "float array mismatch");
        }

        /// <summary>
        /// Executes the test synch frame sequence roundtrip operation.
        /// </summary>
        private static void TestSynchFrameSequenceRoundtrip()
        {
            NetsquareTransformFrame transformFrame = new NetsquareTransformFrame(1, 2, 3, 0, 0, 0, 1, 12.5f);
            transformFrame.SequenceID = 42;
            NetSquareStateFrame stateFrame = new NetSquareStateFrame(12.5f, 7, 43);

            NetworkMessage message = new NetworkMessage(NetSquareMessageID.SetSynchFrames);
            NetSquareSynchFramesUtils.SerializeFrames(message, new List<INetSquareSynchFrame> { transformFrame, stateFrame });
            NetworkMessage copy = new NetworkMessage(message.Serialize());
            INetSquareSynchFrame[] frames = NetSquareSynchFramesUtils.GetFrames(copy);

            Assert(frames.Length == 2, "frame count mismatch");
            Assert(frames[0].SequenceID == 42, "transform sequence mismatch");
            Assert(frames[1].SequenceID == 43, "state sequence mismatch");
            Assert(Math.Abs(((NetsquareTransformFrame)frames[0]).x - 1f) < 0.0001f, "transform payload mismatch");
            Assert(((NetSquareStateFrame)frames[1]).States == 7, "state payload mismatch");
        }

        /// <summary>
        /// Executes the test invalid frame rejected operation.
        /// </summary>
        private static void TestInvalidFrameRejected()
        {
            byte[] data = new byte[ConnectedClient.MinTcpMessageSize];
            WriteInt(data, 0, ConnectedClient.MinTcpMessageSize - 1);
            ExpectThrows(delegate { new NetworkMessage(data); }, "invalid length was accepted");
        }

        /// <summary>
        /// Executes the test datagram mismatch rejected operation.
        /// </summary>
        private static void TestDatagramMismatchRejected()
        {
            byte[] data = new NetworkMessage(3, 9).Set(1).Serialize();
            WriteInt(data, 0, data.Length + 1);
            NetworkMessage message = new NetworkMessage();
            Assert(!message.SafeSetDatagram(data), "datagram mismatch was accepted");
        }

        /// <summary>
        /// Executes the test set type operation.
        /// </summary>
        private static void TestSetType()
        {
            NetworkMessage message = new NetworkMessage(1);
            message.SetType(NetSquareMessageType.BroadcastCurrentWorld);
            Assert(message.MsgType == (byte)NetSquareMessageType.BroadcastCurrentWorld, "SetType ignored the provided type");
        }

        /// <summary>
        /// Executes the test tcp fragmented receive operation.
        /// </summary>
        private static void TestTcpFragmentedReceive()
        {
            using (SocketPair pair = SocketPair.Create())
            {
                ManualResetEventSlim received = new ManualResetEventSlim(false);
                NetworkMessage receivedMessage = null;
                Exception receiveException = null;

                pair.Connected.OnException += delegate (Exception ex) { receiveException = ex; };
                pair.Connected.OnMessageReceived += delegate (NetworkMessage message)
                {
                    receivedMessage = message;
                    received.Set();
                };

                byte[] data = new NetworkMessage(77, 11).Set("fragmented").Serialize();
                NetworkStream stream = pair.Client.GetStream();
                stream.Write(data, 0, 2);
                Thread.Sleep(15);
                stream.Write(data, 2, 3);
                Thread.Sleep(15);
                stream.Write(data, 5, data.Length - 5);

                Assert(received.Wait(3000), "message was not received");
                Assert(receiveException == null, "receive exception: " + receiveException);
                Assert(receivedMessage.HeadID == 77, "head id mismatch");
                Assert(receivedMessage.Serializer.GetString() == "fragmented", "payload mismatch");
            }
        }

        /// <summary>
        /// Executes the test tcp oversized frame disconnects operation.
        /// </summary>
        private static void TestTcpOversizedFrameDisconnects()
        {
            using (SocketPair pair = SocketPair.Create())
            {
                ManualResetEventSlim disconnected = new ManualResetEventSlim(false);
                pair.Connected.OnDisconected += delegate (uint id) { disconnected.Set(); };

                byte[] length = BitConverter.GetBytes(ConnectedClient.MaxTcpMessageSize + 1);
                pair.Client.GetStream().Write(length, 0, length.Length);

                Assert(disconnected.Wait(3000), "oversized frame did not disconnect");
            }
        }

        /// <summary>
        /// Executes the test tcp reset probe disconnects cleanly operation.
        /// </summary>
        private static void TestTcpResetProbeDisconnectsCleanly()
        {
            using (SocketPair pair = SocketPair.Create())
            {
                pair.Client.Client.LingerState = new LingerOption(true, 0);
                pair.Client.Close();

                WaitUntil(delegate { return !pair.Connected.IsConnected(); }, 3000, "reset socket was not reported disconnected");
            }
        }

        /// <summary>
        /// Executes the test manual client disconnect sends notice operation.
        /// </summary>
        private static void TestManualClientDisconnectSendsNotice()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, false))
            {
                ManualResetEventSlim noticeReceived = new ManualResetEventSlim(false);
                ManualResetEventSlim disconnected = new ManualResetEventSlim(false);
                server.Server.OnMessageReceived += delegate (NetworkMessage message)
                {
                    if (message.HeadID == (ushort)NetSquareMessageID.Disconnecting)
                        noticeReceived.Set();
                };
                server.Server.OnClientDisconnected += delegate (uint id) { disconnected.Set(); };

                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                client.Disconnect();

                Assert(noticeReceived.Wait(1000), "server did not receive disconnect notice");
                Assert(disconnected.Wait(3000), "server did not disconnect client");
            }
        }

        /// <summary>
        /// Executes the test server stop notifies clients operation.
        /// </summary>
        private static void TestServerStopNotifiesClients()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, false))
            {
                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                ManualResetEventSlim disconnected = new ManualResetEventSlim(false);
                client.OnDisconected += delegate () { disconnected.Set(); };

                server.Server.Stop();

                Assert(disconnected.Wait(3000), "client did not observe server disconnection");
                WaitUntil(delegate { return server.Server.Clients.Count == 0; }, 3000, "server still tracks clients after stop");
            }
        }

        /// <summary>
        /// Executes the test tcp concurrent sends operation.
        /// </summary>
        private static void TestTcpConcurrentSends()
        {
            const int threads = 8;
            const int perThread = 80;
            const int total = threads * perThread;

            using (SocketPair pair = SocketPair.Create())
            {
                pair.Client.ReceiveTimeout = 5000;
                ManualResetEventSlim start = new ManualResetEventSlim(false);
                Thread[] workers = new Thread[threads];

                for (int t = 0; t < threads; t++)
                {
                    int threadIndex = t;
                    workers[t] = new Thread(delegate ()
                    {
                        start.Wait();
                        int offset = threadIndex * perThread;
                        for (int i = 0; i < perThread; i++)
                        {
                            int id = offset + i;
                            pair.Connected.AddTCPMessage(new NetworkMessage((ushort)(1000 + id), 1).Set(id));
                        }
                    });
                    workers[t].IsBackground = true;
                    workers[t].Start();
                }

                start.Set();
                for (int i = 0; i < workers.Length; i++)
                    workers[i].Join();

                HashSet<int> received = new HashSet<int>();
                NetworkStream stream = pair.Client.GetStream();
                for (int i = 0; i < total; i++)
                {
                    NetworkMessage message = new NetworkMessage(ReadFrame(stream));
                    int id = message.Serializer.GetInt();
                    received.Add(id);
                }

                Assert(received.Count == total, "received " + received.Count + " unique messages instead of " + total);
            }
        }

        /// <summary>
        /// Executes the test packing roundtrip operation.
        /// </summary>
        private static void TestPackingRoundtrip()
        {
            List<NetworkMessage> messages = new List<NetworkMessage>
            {
                new NetworkMessage(PackingSmokeHeadId, 101).Set(10).Set("alpha"),
                new NetworkMessage(PackingSmokeHeadId, 202).Set(20).Set("beta")
            };

            NetworkMessage packed = new NetworkMessage(PackingSmokeHeadId, 9).Set(true);
            packed.Pack(messages);

            Assert(packed.Serializer.GetBool(), "packed prefix payload was lost");
            List<NetworkMessage> unpacked = packed.Unpack();
            Assert(unpacked.Count == 2, "unpacked count mismatch");
            Assert(unpacked[0].ClientID == 101 && unpacked[0].Serializer.GetInt() == 10 && unpacked[0].Serializer.GetString() == "alpha", "first packed message mismatch");
            Assert(unpacked[1].ClientID == 202 && unpacked[1].Serializer.GetInt() == 20 && unpacked[1].Serializer.GetString() == "beta", "second packed message mismatch");

            NetworkMessage received = new NetworkMessage(new NetworkMessage(PackingSmokeHeadId, 303).Set(30).Serialize());
            NetworkMessage packedSerialized = new NetworkMessage(PackingSmokeHeadId);
            packedSerialized.Pack(new[] { received }, true);
            List<NetworkMessage> unpackedSerialized = packedSerialized.Unpack();
            Assert(unpackedSerialized.Count == 1, "serialized packed count mismatch");
            Assert(unpackedSerialized[0].ClientID == 303 && unpackedSerialized[0].Serializer.GetInt() == 30, "serialized packed message mismatch");
        }

        /// <summary>
        /// Executes the test server client settings operation.
        /// </summary>
        private static void TestServerClientSettings()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, false))
            {
                Assert(server.Server.ProtocoleType == NetSquareProtocoleType.TCP, "server protocol mismatch");
                Assert(server.Server.Worlds == null, "world manager should be disabled");

                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                Assert(client.ProtocoleType == NetSquareProtocoleType.TCP, "client protocol mismatch");
                Assert(!client.WorldsManager.SynchronizeUsingUDP, "client should not synchronize using UDP");
                Assert(client.Client != null && !client.Client.UDPEnabled, "TCP-only client created UDP connection");
            }

            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP_AND_UDP, true))
            {
                Assert(server.Server.Worlds != null, "world manager should be enabled");

                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP, true);
                Assert(client.ProtocoleType == NetSquareProtocoleType.TCP_AND_UDP, "synchronizeUsingUDP should force TCP_AND_UDP");
                Assert(client.WorldsManager.SynchronizeUsingUDP, "client synchronizeUsingUDP flag mismatch");
                Assert(client.Client != null && client.Client.UDPEnabled, "UDP client did not create UDP connection");
            }
        }

        /// <summary>
        /// Executes the test tcp client server message operation.
        /// </summary>
        private static void TestTcpClientServerMessage()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, false))
            {
                server.Server.Dispatcher.AddHeadAction(TcpSmokeHeadId, "TcpSmoke", delegate (NetworkMessage message)
                {
                    int value = message.Serializer.GetInt();
                    string text = message.Serializer.GetString();
                    server.Server.Reply(message, new NetworkMessage().Set(value + 1).Set(text + "-pong"));
                });

                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                ManualResetEventSlim replyReceived = new ManualResetEventSlim(false);
                int replyValue = 0;
                string replyText = null;

                client.SendMessage(new NetworkMessage(TcpSmokeHeadId).Set(41).Set("ping"), delegate (NetworkMessage reply)
                {
                    replyValue = reply.Serializer.GetInt();
                    replyText = reply.Serializer.GetString();
                    replyReceived.Set();
                });

                Assert(replyReceived.Wait(5000), "TCP reply was not received");
                Assert(replyValue == 42 && replyText == "ping-pong", "TCP reply payload mismatch");
            }
        }

        /// <summary>
        /// Executes the test client server time synchronization operation.
        /// </summary>
        private static void TestClientServerTimeSynchronization()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, false))
            {
                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                Stopwatch stopwatch = Stopwatch.StartNew();
                ManualResetEventSlim synchronized = new ManualResetEventSlim(false);
                int callbackCount = 0;

                client.TimeSynchronizationRequestTimeoutMs = 1000;
                client.TimeSynchronizationMaxAttempts = 6;
                client.SyncTime(
                    delegate { return (float)stopwatch.Elapsed.TotalSeconds; },
                    3,
                    20,
                    delegate
                    {
                        if (Interlocked.Increment(ref callbackCount) >= 2)
                            synchronized.Set();
                    });

                Assert(synchronized.Wait(5000), "time synchronization did not complete");
                Assert(client.IsTimeSynchronized, "client did not mark time synchronized");
                Assert(client.IsServerTimeSynchronizationFresh(1000), "time synchronization was not fresh");

                float estimatedServerTime = client.GetServerTime((float)stopwatch.Elapsed.TotalSeconds);
                float actualServerTime = server.Server.Time;
                Assert(Math.Abs(estimatedServerTime - actualServerTime) < 0.25f, "server time estimate drifted too far");
            }
        }

        /// <summary>
        /// Executes the test automatic client server time synchronization operation.
        /// </summary>
        private static void TestAutomaticClientServerTimeSynchronization()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, false))
            {
                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                Stopwatch stopwatch = Stopwatch.StartNew();
                ManualResetEventSlim secondRefresh = new ManualResetEventSlim(false);
                int updateCount = 0;

                client.TimeSynchronizationRequestTimeoutMs = 1000;
                client.TimeSynchronizationMaxAttempts = 4;
                client.StartAutoSyncTime(
                    delegate { return (float)stopwatch.Elapsed.TotalSeconds; },
                    2,
                    10,
                    1000,
                    delegate
                    {
                        if (Interlocked.Increment(ref updateCount) >= 3)
                            secondRefresh.Set();
                    });

                Assert(client.IsAutoTimeSynchronizationEnabled, "auto time synchronization was not enabled");
                Assert(secondRefresh.Wait(5000), "auto time synchronization did not refresh");
                Assert(client.IsServerTimeSynchronizationFresh(1500), "auto time synchronization was not fresh");

                client.StopAutoSyncTime();
                Assert(!client.IsAutoTimeSynchronizationEnabled, "auto time synchronization did not stop");
            }
        }

        /// <summary>
        /// Executes the test udp client server message operation.
        /// </summary>
        private static void TestUdpClientServerMessage()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP_AND_UDP, false))
            {
                ManualResetEventSlim serverReceived = new ManualResetEventSlim(false);
                server.Server.Dispatcher.AddHeadAction(UdpSmokeHeadId, "UdpSmoke", delegate (NetworkMessage message)
                {
                    if (message.Serializer.GetInt() == 1234)
                        serverReceived.Set();

                    NetworkMessage ack = new NetworkMessage(UdpSmokeHeadId, message.ClientID).Set(5678);
                    server.Server.SendToClientUnreliable(UdpSmokeHeadId, ack.Serialize(), message.Client);
                });

                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP_AND_UDP, true);
                ManualResetEventSlim clientReceived = new ManualResetEventSlim(false);
                int ackValue = 0;
                client.Dispatcher.AddHeadAction(UdpSmokeHeadId, "UdpSmokeAck", delegate (NetworkMessage message)
                {
                    ackValue = message.Serializer.GetInt();
                    clientReceived.Set();
                });

                client.SendMessageUDP(new NetworkMessage(UdpSmokeHeadId).Set(1234));

                Assert(serverReceived.Wait(5000), "server did not receive UDP message");
                Assert(clientReceived.Wait(5000), "client did not receive UDP ack");
                Assert(ackValue == 5678, "UDP ack payload mismatch");
            }
        }

        /// <summary>
        /// Executes the test udp client local port conflict operation.
        /// </summary>
        private static void TestUdpClientLocalPortConflict()
        {
            System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            TcpClient tcpClient = null;
            Socket serverSocket = null;
            ConnectedClient connected = null;
            UdpClient blockedUdp = null;

            try
            {
                listener.Start();
                int serverPort = ((IPEndPoint)listener.LocalEndpoint).Port;

                int clientLocalPort = 0;
                for (int i = 0; i < 20 && blockedUdp == null; i++)
                {
                    int candidate = GetFreeTcpPort();
                    if (candidate >= ushort.MaxValue)
                        continue;

                    try
                    {
                        blockedUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, candidate + 1));
                        clientLocalPort = candidate;
                    }
                    catch (SocketException) { }
                }

                Assert(blockedUdp != null, "could not reserve UDP conflict port");

                Task<Socket> acceptTask = Task.Factory.StartNew(delegate { return listener.AcceptSocket(); });
                tcpClient = new TcpClient(new IPEndPoint(IPAddress.Loopback, clientLocalPort));
                tcpClient.NoDelay = true;
                tcpClient.Connect(IPAddress.Loopback, serverPort);
                serverSocket = acceptTask.Result;

                connected = new ConnectedClient();
                connected.ID = 123;
                connected.SetClient(tcpClient.Client, true, true);

                IPEndPoint udpLocalEndPoint = (IPEndPoint)connected.UDP.connection.Client.LocalEndPoint;
                Assert(connected.UDPEnabled, "UDP was not enabled");
                Assert(udpLocalEndPoint.Port != clientLocalPort + 1, "client UDP still used TCP local port + 1");
            }
            finally
            {
                try { connected?.UDP?.connection?.Close(); } catch { }
                try { tcpClient?.Close(); } catch { }
                try { serverSocket?.Close(); } catch { }
                try { blockedUdp?.Close(); } catch { }
                try { listener.Stop(); } catch { }
            }
        }

        /// <summary>
        /// Executes the test udp replace client id routing operation.
        /// </summary>
        private static void TestUdpReplaceClientIdRouting()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP_AND_UDP, false))
            {
                NetSquare.Client.NetSquareClient client = server.ConnectClient(NetSquareProtocoleType.TCP_AND_UDP, true);
                uint oldClientID = client.ClientID;
                uint newClientID = oldClientID + 1000;
                if (newClientID > UInt24.MaxValue)
                    newClientID = oldClientID - 1;

                Assert(server.Server.ReplaceClientID(oldClientID, newClientID), "server failed to replace client ID");
                client.ReplaceClientID(newClientID);

                ManualResetEventSlim serverReceived = new ManualResetEventSlim(false);
                ManualResetEventSlim clientReceived = new ManualResetEventSlim(false);
                int clientValue = 0;
                server.Server.Dispatcher.AddHeadAction(UdpSmokeHeadId, "UdpReplaceIdSmoke", delegate (NetworkMessage message)
                {
                    if (message.ClientID == newClientID && message.Serializer.GetInt() == 2468)
                        serverReceived.Set();
                });
                client.Dispatcher.AddHeadAction(UdpSmokeHeadId, "UdpReplaceIdAck", delegate (NetworkMessage message)
                {
                    clientValue = message.Serializer.GetInt();
                    clientReceived.Set();
                });

                client.SendMessageUDP(new NetworkMessage(UdpSmokeHeadId).Set(2468));
                Assert(serverReceived.Wait(5000), "server did not receive UDP after client ID replacement");

                server.Server.SendToClientUnreliable(new NetworkMessage(UdpSmokeHeadId, newClientID).Set(1357), newClientID);
                Assert(clientReceived.Wait(5000), "client did not receive UDP after client ID replacement");
                Assert(clientValue == 1357, "client UDP after replace payload mismatch");
            }
        }

        /// <summary>
        /// Executes the test world join broadcast sync leave operation.
        /// </summary>
        private static void TestWorldJoinBroadcastSyncLeave()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, true))
            {
                const ushort worldId = 42;
                NetSquareWorld world = server.Server.Worlds.AddWorld(worldId, "diagnostics", 8);
                world.StartSynchronizer(30, false);

                NetSquare.Client.NetSquareClient client1 = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                NetSquare.Client.NetSquareClient client2 = server.ConnectClient(NetSquareProtocoleType.TCP, false);

                ManualResetEventSlim client1SawClient2Join = new ManualResetEventSlim(false);
                ManualResetEventSlim client1SawClient2Leave = new ManualResetEventSlim(false);
                client1.WorldsManager.OnClientJoinWorld += delegate (uint id, NetsquareTransformFrame transform, NetworkMessage message)
                {
                    if (id == client2.ClientID)
                        client1SawClient2Join.Set();
                };
                client1.WorldsManager.OnClientLeaveWorld += delegate (uint id)
                {
                    if (id == client2.ClientID)
                        client1SawClient2Leave.Set();
                };

                ManualResetEventSlim broadcastReceived = new ManualResetEventSlim(false);
                client2.Dispatcher.AddHeadAction(WorldBroadcastHeadId, "WorldBroadcast", delegate (NetworkMessage message)
                {
                    if (message.Serializer.GetString() == "hello-world")
                        broadcastReceived.Set();
                });

                ManualResetEventSlim syncReceived = new ManualResetEventSlim(false);
                client2.Dispatcher.AddHeadAction(WorldSyncHeadId, "WorldSync", delegate (NetworkMessage message)
                {
                    foreach (NetworkMessage unpacked in message.Unpack())
                    {
                        if (unpacked.ClientID == client1.ClientID && unpacked.Serializer.GetInt() == 777)
                            syncReceived.Set();
                    }
                });

                Assert(TryJoinWorld(client1, worldId, new NetsquareTransformFrame(1, 2, 3)), "client1 failed to join world");
                Assert(client1.WorldsManager.IsInWorld && server.Server.Worlds.IsInWorld(client1.ClientID), "client1 world state mismatch");
                Assert(TryJoinWorld(client2, worldId, new NetsquareTransformFrame(4, 5, 6)), "client2 failed to join world");
                Assert(client2.WorldsManager.IsInWorld && server.Server.Worlds.IsInWorld(client2.ClientID), "client2 world state mismatch");
                Assert(client1SawClient2Join.Wait(5000), "world join notification was not broadcast");

                client1.WorldsManager.Broadcast(new NetworkMessage(WorldBroadcastHeadId).Set("hello-world"));
                Assert(broadcastReceived.Wait(5000), "world broadcast was not received");

                client1.WorldsManager.Synchronize(new NetworkMessage(WorldSyncHeadId).Set(777));
                Assert(syncReceived.Wait(5000), "world synchronizer message was not received");

                bool leaveResult = false;
                ManualResetEventSlim leaveCompleted = new ManualResetEventSlim(false);
                client2.WorldsManager.TryleaveWorld(delegate (bool ok)
                {
                    leaveResult = ok;
                    leaveCompleted.Set();
                });

                Assert(leaveCompleted.Wait(5000), "world leave reply was not received");
                Assert(leaveResult, "client2 failed to leave world");
                Assert(!client2.WorldsManager.IsInWorld && !server.Server.Worlds.IsInWorld(client2.ClientID), "client2 world leave state mismatch");
                Assert(client1SawClient2Leave.Wait(5000), "world leave notification was not broadcast");

                world.StopUsingSynchronizer();
            }
        }

        /// <summary>
        /// Executes the test world transform cache updates from frames operation.
        /// </summary>
        private static void TestWorldTransformCacheUpdatesFromFrames()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, true))
            {
                const ushort worldId = 45;
                NetSquareWorld world = server.Server.Worlds.AddWorld(worldId, "world-transform-cache", 8);

                NetSquare.Client.NetSquareClient client1 = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                NetSquare.Client.NetSquareClient client2 = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                ManualResetEventSlim frameReceived = new ManualResetEventSlim(false);
                uint receivedSequence = 0;
                client2.WorldsManager.OnReceiveSynchFrames += delegate (uint clientID, INetSquareSynchFrame[] frames)
                {
                    if (clientID != client1.ClientID || frames.Length == 0)
                        return;

                    receivedSequence = frames[0].SequenceID;
                    frameReceived.Set();
                };

                Assert(TryJoinWorld(client1, worldId, new NetsquareTransformFrame(0, 0, 0)), "client1 failed to join world");
                Assert(TryJoinWorld(client2, worldId, new NetsquareTransformFrame(3, 0, 0)), "client2 failed to join world");

                client1.WorldsManager.SendSynchFrame(new NetsquareTransformFrame(11, 0, 0));
                WaitUntil(delegate
                {
                    NetsquareTransformFrame transform;
                    return world.Clients.TryGetValue(client1.ClientID, out transform) && Math.Abs(transform.x - 11f) < 0.0001f;
                }, 5000, "world transform cache was not updated");

                Assert(frameReceived.Wait(5000), "synch frame was not broadcast");
                Assert(receivedSequence != 0, "synch frame sequence was not assigned");
            }
        }

        /// <summary>
        /// Executes the test simple spatializer visibility operation.
        /// </summary>
        private static void TestSimpleSpatializerVisibility()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, true))
            {
                const ushort worldId = 43;
                NetSquareWorld world = server.Server.Worlds.AddWorld(worldId, "simple-spatializer", 8);
                world.SetSpatializer(Spatializer.GetSimpleSpatializer(world, 60, 60, 5f));

                NetSquare.Client.NetSquareClient client1 = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                NetSquare.Client.NetSquareClient client2 = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                NetSquare.Client.NetSquareClient client3 = server.ConnectClient(NetSquareProtocoleType.TCP, false);

                ManualResetEventSlim client1SawClient2Join = new ManualResetEventSlim(false);
                ManualResetEventSlim client1SawClient3Join = new ManualResetEventSlim(false);
                ManualResetEventSlim client1SawClient2Leave = new ManualResetEventSlim(false);
                client1.WorldsManager.OnClientJoinWorld += delegate (uint id, NetsquareTransformFrame transform, NetworkMessage message)
                {
                    if (id == client2.ClientID)
                        client1SawClient2Join.Set();
                    if (id == client3.ClientID)
                        client1SawClient3Join.Set();
                };
                client1.WorldsManager.OnClientLeaveWorld += delegate (uint id)
                {
                    if (id == client2.ClientID)
                        client1SawClient2Leave.Set();
                };

                ManualResetEventSlim nearBroadcastReceived = new ManualResetEventSlim(false);
                ManualResetEventSlim farBroadcastReceived = new ManualResetEventSlim(false);
                client2.Dispatcher.AddHeadAction(WorldBroadcastHeadId, "SimpleSpatializerNearBroadcast", delegate (NetworkMessage message)
                {
                    if (message.Serializer.GetString() == "simple-near")
                        nearBroadcastReceived.Set();
                });
                client3.Dispatcher.AddHeadAction(WorldBroadcastHeadId, "SimpleSpatializerFarBroadcast", delegate (NetworkMessage message)
                {
                    if (message.Serializer.GetString() == "simple-near")
                        farBroadcastReceived.Set();
                });

                Assert(TryJoinWorld(client1, worldId, new NetsquareTransformFrame(0, 0, 0)), "client1 failed to join simple spatialized world");
                Assert(TryJoinWorld(client2, worldId, new NetsquareTransformFrame(2, 0, 0)), "client2 failed to join simple spatialized world");
                Assert(TryJoinWorld(client3, worldId, new NetsquareTransformFrame(50, 0, 0)), "client3 failed to join simple spatialized world");
                Assert(client1SawClient2Join.Wait(5000), "simple spatializer did not show nearby client");
                Assert(!client1SawClient3Join.Wait(500), "simple spatializer showed far client");

                client1.WorldsManager.Broadcast(new NetworkMessage(WorldBroadcastHeadId).Set("simple-near"));
                Assert(nearBroadcastReceived.Wait(5000), "simple spatializer did not deliver nearby broadcast");
                Assert(!farBroadcastReceived.Wait(500), "simple spatializer delivered broadcast to far client");

                client2.WorldsManager.SendSynchFrame(new NetsquareTransformFrame(30, 0, 0));
                Assert(client1SawClient2Leave.Wait(5000), "simple spatializer did not hide client after moving out of view");

                world.SetSpatializer(null);
            }
        }

        /// <summary>
        /// Executes the test chunked spatializer visibility operation.
        /// </summary>
        private static void TestChunkedSpatializerVisibility()
        {
            using (RunningServer server = RunningServer.Start(NetSquareProtocoleType.TCP, true))
            {
                const ushort worldId = 44;
                NetSquareWorld world = server.Server.Worlds.AddWorld(worldId, "chunked-spatializer", 8);
                world.SetSpatializer(Spatializer.GetChunkedSpatializer(world, 60, 60, 10f, -10f, -10f, 30f, 30f));

                NetSquare.Client.NetSquareClient client1 = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                NetSquare.Client.NetSquareClient client2 = server.ConnectClient(NetSquareProtocoleType.TCP, false);
                NetSquare.Client.NetSquareClient client3 = server.ConnectClient(NetSquareProtocoleType.TCP, false);

                ManualResetEventSlim client1SawClient2Join = new ManualResetEventSlim(false);
                ManualResetEventSlim client1SawClient3Join = new ManualResetEventSlim(false);
                ManualResetEventSlim client1SawClient2Leave = new ManualResetEventSlim(false);
                client1.WorldsManager.OnClientJoinWorld += delegate (uint id, NetsquareTransformFrame transform, NetworkMessage message)
                {
                    if (id == client2.ClientID)
                        client1SawClient2Join.Set();
                    if (id == client3.ClientID)
                        client1SawClient3Join.Set();
                };
                client1.WorldsManager.OnClientLeaveWorld += delegate (uint id)
                {
                    if (id == client2.ClientID)
                        client1SawClient2Leave.Set();
                };

                Assert(TryJoinWorld(client1, worldId, new NetsquareTransformFrame(0, 0, 0)), "client1 failed to join chunked spatialized world");
                Assert(TryJoinWorld(client2, worldId, new NetsquareTransformFrame(9, 0, 0)), "client2 failed to join chunked spatialized world");
                Assert(TryJoinWorld(client3, worldId, new NetsquareTransformFrame(25, 0, 25)), "client3 failed to join chunked spatialized world");
                Assert(client1SawClient2Join.Wait(5000), "chunked spatializer did not show nearby client");
                Assert(!client1SawClient3Join.Wait(500), "chunked spatializer showed non-neighbour client");

                world.AddStaticEntity(1, 1, new NetsquareTransformFrame(0, 0, 0));
                world.AddStaticEntity(1, 2, new NetsquareTransformFrame(100, 0, 100));

                client2.WorldsManager.SendSynchFrame(new NetsquareTransformFrame(100, 0, 100));
                Assert(client1SawClient2Leave.Wait(5000), "chunked spatializer did not hide out-of-bounds client");

                world.SetSpatializer(null);
            }
        }

        /// <summary>
        /// Executes the try join world operation.
        /// </summary>
        private static bool TryJoinWorld(NetSquare.Client.NetSquareClient client, ushort worldId, NetsquareTransformFrame transform)
        {
            bool result = false;
            ManualResetEventSlim completed = new ManualResetEventSlim(false);
            client.WorldsManager.TryJoinWorld(worldId, transform, delegate (bool ok)
            {
                result = ok;
                completed.Set();
            });
            Assert(completed.Wait(5000), "world join reply was not received");
            return result;
        }

        /// <summary>
        /// Executes the run benchmarks operation.
        /// </summary>
        private static void RunBenchmarks(bool fullLoad, int runs)
        {
            runs = Math.Max(1, runs);
            Console.WriteLine();
            Console.WriteLine(runs == 1 ? "Benchmarks" : "Benchmarks (" + runs + " runs)");

            for (int run = 1; run <= runs; run++)
            {
                if (runs > 1)
                {
                    Console.WriteLine();
                    Console.WriteLine("Run " + run + "/" + runs);
                }

                BenchmarkSerialization(50000, run);
                BenchmarkArraySerialization(20000, run);
                BenchmarkTcpSendPump(2000, run);
                BenchmarkRealServerLoad(fullLoad, run);
            }

            if (runs > 1)
                PrintBenchmarkSummary(runs);
        }

        /// <summary>
        /// Executes the benchmark serialization operation.
        /// </summary>
        private static void BenchmarkSerialization(int count, int run)
        {
            PrepareForBenchmark();
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            long memoryBefore = GC.GetTotalMemory(false);

            Stopwatch stopwatch = Stopwatch.StartNew();
            long bytes = 0;
            for (int i = 0; i < count; i++)
            {
                NetworkMessage message = new NetworkMessage((ushort)(i % 1024), (uint)(i % 1000))
                    .Set(i)
                    .Set((float)i * 0.25f)
                    .Set("payload");
                byte[] data = message.Serialize();
                bytes += data.Length;
                NetworkMessage copy = new NetworkMessage(data);
                copy.Serializer.GetInt();
                copy.Serializer.GetFloat();
                copy.Serializer.GetString();
            }
            stopwatch.Stop();

            BenchmarkResult result = CreateBenchmarkResult("serialize+deserialize", count, bytes, stopwatch.Elapsed, gc0, gc1, gc2, memoryBefore);
            result.Run = run;
            PrintBenchmark(result);
            BenchmarkResults.Add(result);
        }

        /// <summary>
        /// Executes the benchmark array serialization operation.
        /// </summary>
        private static void BenchmarkArraySerialization(int count, int run)
        {
            int[] ints = new int[256];
            float[] floats = new float[128];
            for (int i = 0; i < ints.Length; i++)
                ints[i] = i * 17;
            for (int i = 0; i < floats.Length; i++)
                floats[i] = i * 0.5f;

            PrepareForBenchmark();
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            long memoryBefore = GC.GetTotalMemory(false);

            Stopwatch stopwatch = Stopwatch.StartNew();
            long bytes = 0;
            for (int i = 0; i < count; i++)
            {
                NetworkMessage message = new NetworkMessage((ushort)(i % 1024), (uint)(i % 1000))
                    .Set(ints)
                    .Set(floats);
                byte[] data = message.Serialize();
                bytes += data.Length;
                NetworkMessage copy = new NetworkMessage(data);
                int[] copiedInts = copy.Serializer.GetIntArray();
                float[] copiedFloats = copy.Serializer.GetFloatArray();
                if (copiedInts.Length != ints.Length || copiedFloats.Length != floats.Length)
                    throw new InvalidOperationException("array benchmark roundtrip mismatch");
            }
            stopwatch.Stop();

            BenchmarkResult result = CreateBenchmarkResult("primitive array serialize+deserialize", count, bytes, stopwatch.Elapsed, gc0, gc1, gc2, memoryBefore);
            result.Run = run;
            PrintBenchmark(result);
            BenchmarkResults.Add(result);
        }

        /// <summary>
        /// Executes the benchmark tcp send pump operation.
        /// </summary>
        private static void BenchmarkTcpSendPump(int count, int run)
        {
            PrepareForBenchmark();
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            long memoryBefore = GC.GetTotalMemory(false);

            using (SocketPair pair = SocketPair.Create())
            {
                pair.Client.ReceiveTimeout = 10000;
                Stopwatch stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                    pair.Connected.AddTCPMessage(new NetworkMessage((ushort)(i % 1024), 1).Set(i));

                NetworkStream stream = pair.Client.GetStream();
                long bytes = 0;
                for (int i = 0; i < count; i++)
                    bytes += ReadFrame(stream).Length;

                stopwatch.Stop();
                BenchmarkResult result = CreateBenchmarkResult("tcp send pump", count, bytes, stopwatch.Elapsed, gc0, gc1, gc2, memoryBefore);
                result.Run = run;
                PrintBenchmark(result);
                BenchmarkResults.Add(result);
            }
        }

        /// <summary>
        /// Executes the benchmark real server load operation.
        /// </summary>
        private static void BenchmarkRealServerLoad(bool fullLoad, int run)
        {
            int clientCount = fullLoad ? 64 : 16;
            int messagesPerClient = fullLoad ? 64 : 20;
            int payloadSize = fullLoad ? 192 : 96;
            int total = clientCount * messagesPerClient;
            int originalWorkerThreads;
            int originalCompletionPortThreads;
            ThreadPool.GetMinThreads(out originalWorkerThreads, out originalCompletionPortThreads);
            ThreadPool.SetMinThreads(Math.Max(originalWorkerThreads, clientCount * 4), Math.Max(originalCompletionPortThreads, clientCount * 2));

            NetSquare.Server.NetSquareServer server = null;
            Thread serverThread = null;
            List<NetSquare.Client.NetSquareClient> clients = new List<NetSquare.Client.NetSquareClient>();
            INetSquareWriterOutput previousWriterOutput = Writer.GetOutput();
            bool previousDisplayLog = Writer.DisplayLog;
            bool previousDisplayTitle = Writer.DisplayTitle;

            try
            {
                Writer.SetOutputAsNull();
                Writer.StartDisplayLog();
                Writer.StopDisplayTitle();

                int port = GetFreeTcpPort();
                server = new NetSquare.Server.NetSquareServer(NetSquareProtocoleType.TCP, false);
                server.DrawHeaderOverrideCallback = delegate { };
                server.Dispatcher.AddHeadAction(EchoHeadId, "DiagnosticsEcho", delegate (NetworkMessage message)
                {
                    int sequence = message.Serializer.GetInt();
                    ArraySegment<byte> payload = message.Serializer.GetByteArraySegment();
                    server.Reply(message, new NetworkMessage().Set(sequence).Set(payload.Array, payload.Offset, payload.Count));
                });

                serverThread = new Thread(delegate () { server.Start(port, true, false, false); });
                serverThread.IsBackground = true;
                serverThread.Start();
                WaitUntil(delegate { return server.IsStarted; }, 5000, "real server did not start");

                for (int i = 0; i < clientCount; i++)
                    clients.Add(ConnectBenchmarkClient(port, i, fullLoad ? 15000 : 8000));

                byte[] payloadTemplate = new byte[payloadSize];
                for (int i = 0; i < payloadTemplate.Length; i++)
                    payloadTemplate[i] = (byte)(i & 0xFF);

                WarmupRealServerClients(clients, payloadTemplate, Math.Min(2, messagesPerClient));
                PrepareForBenchmark();

                int gc0 = GC.CollectionCount(0);
                int gc1 = GC.CollectionCount(1);
                int gc2 = GC.CollectionCount(2);
                long memoryBefore = GC.GetTotalMemory(false);

                long[] latencyTicks = new long[total];
                int latencyCount = 0;
                int remaining = total;
                long failures = 0;
                long bytes = 0;
                ManualResetEventSlim completed = new ManualResetEventSlim(false);
                Stopwatch stopwatch = Stopwatch.StartNew();

                for (int c = 0; c < clients.Count; c++)
                {
                    NetSquare.Client.NetSquareClient client = clients[c];
                    for (int m = 0; m < messagesPerClient; m++)
                    {
                        int sequence = c * messagesPerClient + m;
                        long sentTick = Stopwatch.GetTimestamp();

                        client.SendMessage(new NetworkMessage(EchoHeadId).Set(sequence).Set(payloadTemplate), delegate (NetworkMessage reply)
                        {
                            try
                            {
                                int echoedSequence = reply.Serializer.GetInt();
                                ArraySegment<byte> echoedPayload = reply.Serializer.GetByteArraySegment();
                                if (echoedSequence != sequence || echoedPayload.Count != payloadSize)
                                    Interlocked.Increment(ref failures);

                                int index = Interlocked.Increment(ref latencyCount) - 1;
                                latencyTicks[index] = Stopwatch.GetTimestamp() - sentTick;
                                Interlocked.Add(ref bytes, reply.MessageLength + payloadSize + 18);
                            }
                            catch
                            {
                                Interlocked.Increment(ref failures);
                            }
                            finally
                            {
                                if (Interlocked.Decrement(ref remaining) == 0)
                                    completed.Set();
                            }
                        });
                    }
                }

                bool allCompleted = completed.Wait(fullLoad ? 45000 : 20000);
                stopwatch.Stop();

                int missing = Math.Max(0, remaining);
                if (!allCompleted)
                    failures += missing;

                int completedOperations = total - missing;
                BenchmarkResult result = CreateBenchmarkResult("real server TCP roundtrip", completedOperations, bytes, stopwatch.Elapsed, gc0, gc1, gc2, memoryBefore);
                result.Run = run;
                result.Clients = clientCount;
                result.PayloadBytes = payloadSize;
                result.Failures = failures;
                result.P50Ms = PercentileMs(latencyTicks, latencyCount, 50);
                result.P95Ms = PercentileMs(latencyTicks, latencyCount, 95);
                result.P99Ms = PercentileMs(latencyTicks, latencyCount, 99);

                PrintBenchmark(result);
                BenchmarkResults.Add(result);
            }
            finally
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    try { clients[i].Disconnect(); } catch { }
                }

                if (server != null)
                {
                    try { WaitUntil(delegate { return server.Clients.Count == 0; }, 1000, "clients did not disconnect"); } catch { }
                }

                if (server != null)
                {
                    try { server.Statistics.Stop(); } catch { }
                    try { server.Stop(); } catch { }
                }
                Thread.Sleep(25);
                ThreadPool.SetMinThreads(originalWorkerThreads, originalCompletionPortThreads);

                Writer.SetOutput(previousWriterOutput);
                if (previousDisplayLog)
                    Writer.StartDisplayLog();
                else
                    Writer.StopDisplayLog();
                if (previousDisplayTitle)
                    Writer.StartDisplayTitle();
                else
                    Writer.StopDisplayTitle();
            }
        }

        /// <summary>
        /// Executes the connect benchmark client operation.
        /// </summary>
        private static NetSquare.Client.NetSquareClient ConnectBenchmarkClient(int port, int index, int timeoutMs)
        {
            NetSquare.Client.NetSquareClient client = new NetSquare.Client.NetSquareClient(false);
            ManualResetEventSlim connected = new ManualResetEventSlim(false);
            ManualResetEventSlim failed = new ManualResetEventSlim(false);
            Exception clientException = null;

            client.OnConnected += delegate (uint id) { connected.Set(); };
            client.OnConnectionFail += delegate { failed.Set(); };
            client.OnException += delegate (Exception ex) { clientException = ex; };
            client.Connect("127.0.0.1", port, NetSquareProtocoleType.TCP, false);

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (connected.IsSet)
                    return client;

                if (failed.IsSet)
                    throw new InvalidOperationException("benchmark client " + index + " failed to connect" + (clientException != null ? ": " + clientException.Message : string.Empty));

                Thread.Sleep(5);
            }

            try { client.Disconnect(); } catch { }
            throw new TimeoutException("benchmark client " + index + " did not connect within " + timeoutMs + " ms");
        }

        /// <summary>
        /// Executes the warmup real server clients operation.
        /// </summary>
        private static void WarmupRealServerClients(List<NetSquare.Client.NetSquareClient> clients, byte[] payloadTemplate, int messagesPerClient)
        {
            int total = clients.Count * messagesPerClient;
            if (total <= 0)
                return;

            int remaining = total;
            long failures = 0;
            ManualResetEventSlim completed = new ManualResetEventSlim(false);

            for (int c = 0; c < clients.Count; c++)
            {
                NetSquare.Client.NetSquareClient client = clients[c];
                for (int m = 0; m < messagesPerClient; m++)
                {
                    int sequence = -((c * messagesPerClient) + m + 1);
                    client.SendMessage(new NetworkMessage(EchoHeadId).Set(sequence).Set(payloadTemplate), delegate (NetworkMessage reply)
                    {
                        try
                        {
                            int echoedSequence = reply.Serializer.GetInt();
                            ArraySegment<byte> echoedPayload = reply.Serializer.GetByteArraySegment();
                            if (echoedSequence != sequence || echoedPayload.Count != payloadTemplate.Length)
                                Interlocked.Increment(ref failures);
                        }
                        catch
                        {
                            Interlocked.Increment(ref failures);
                        }
                        finally
                        {
                            if (Interlocked.Decrement(ref remaining) == 0)
                                completed.Set();
                        }
                    });
                }
            }

            Assert(completed.Wait(10000), "real server warmup timed out");
            Assert(failures == 0, "real server warmup failed");
            Thread.Sleep(10);
        }

        /// <summary>
        /// Executes the prepare for benchmark operation.
        /// </summary>
        private static void PrepareForBenchmark()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Executes the create benchmark result operation.
        /// </summary>
        private static BenchmarkResult CreateBenchmarkResult(string name, int count, long bytes, TimeSpan elapsed, int gc0, int gc1, int gc2, long memoryBefore)
        {
            double seconds = Math.Max(elapsed.TotalSeconds, 0.000001);
            return new BenchmarkResult
            {
                Name = name,
                Operations = count,
                Bytes = bytes,
                ElapsedMs = elapsed.TotalMilliseconds,
                OperationsPerSecond = count / seconds,
                KibPerSecond = bytes / 1024.0 / seconds,
                Gen0Collections = GC.CollectionCount(0) - gc0,
                Gen1Collections = GC.CollectionCount(1) - gc1,
                Gen2Collections = GC.CollectionCount(2) - gc2,
                MemoryBefore = memoryBefore,
                MemoryAfter = GC.GetTotalMemory(false)
            };
        }

        /// <summary>
        /// Executes the print benchmark operation.
        /// </summary>
        private static void PrintBenchmark(BenchmarkResult result)
        {
            Console.WriteLine(
                "  " + result.Name + ": " +
                result.Operations + " ops in " + result.ElapsedMs.ToString("0.00", CultureInfo.InvariantCulture) + " ms | " +
                result.OperationsPerSecond.ToString("0", CultureInfo.InvariantCulture) + " ops/s | " +
                result.KibPerSecond.ToString("0.00", CultureInfo.InvariantCulture) + " KiB/s");

            if (result.Clients > 0)
            {
                Console.WriteLine(
                    "    clients=" + result.Clients +
                    " payload=" + result.PayloadBytes +
                    "B failures=" + result.Failures +
                    " p50=" + result.P50Ms.ToString("0.00", CultureInfo.InvariantCulture) +
                    "ms p95=" + result.P95Ms.ToString("0.00", CultureInfo.InvariantCulture) +
                    "ms p99=" + result.P99Ms.ToString("0.00", CultureInfo.InvariantCulture) + "ms");
            }
        }

        /// <summary>
        /// Executes the print benchmark summary operation.
        /// </summary>
        private static void PrintBenchmarkSummary(int runs)
        {
            Console.WriteLine();
            Console.WriteLine("Benchmark summary (" + runs + " runs)");

            foreach (string name in BenchmarkResults.Select(r => r.Name).Distinct())
            {
                List<BenchmarkResult> results = BenchmarkResults.Where(r => r.Name == name).ToList();
                double[] ops = results.Select(r => r.OperationsPerSecond).OrderBy(v => v).ToArray();
                double medianOps = MedianSorted(ops);
                double averageOps = ops.Average();
                double minOps = ops.First();
                double maxOps = ops.Last();

                Console.WriteLine(
                    "  " + name +
                    ": median " + medianOps.ToString("0", CultureInfo.InvariantCulture) +
                    " ops/s | avg " + averageOps.ToString("0", CultureInfo.InvariantCulture) +
                    " | min " + minOps.ToString("0", CultureInfo.InvariantCulture) +
                    " | max " + maxOps.ToString("0", CultureInfo.InvariantCulture));

                if (results[0].Clients > 0)
                {
                    double medianP50 = MedianSorted(results.Select(r => r.P50Ms).OrderBy(v => v).ToArray());
                    double medianP95 = MedianSorted(results.Select(r => r.P95Ms).OrderBy(v => v).ToArray());
                    double medianP99 = MedianSorted(results.Select(r => r.P99Ms).OrderBy(v => v).ToArray());
                    long failures = results.Sum(r => r.Failures);
                    Console.WriteLine(
                        "    latency median p50=" + medianP50.ToString("0.00", CultureInfo.InvariantCulture) +
                        "ms p95=" + medianP95.ToString("0.00", CultureInfo.InvariantCulture) +
                        "ms p99=" + medianP99.ToString("0.00", CultureInfo.InvariantCulture) +
                        "ms failures=" + failures);
                }
            }
        }

        /// <summary>
        /// Executes the median sorted operation.
        /// </summary>
        private static double MedianSorted(double[] sortedValues)
        {
            if (sortedValues.Length == 0)
                return 0;

            int middle = sortedValues.Length / 2;
            if ((sortedValues.Length & 1) == 1)
                return sortedValues[middle];

            return (sortedValues[middle - 1] + sortedValues[middle]) / 2.0;
        }

        /// <summary>
        /// Executes the write benchmark results operation.
        /// </summary>
        private static void WriteBenchmarkResults(string resultsDir)
        {
            if (BenchmarkResults.Count == 0)
                return;

            Directory.CreateDirectory(resultsDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string jsonPath = Path.Combine(resultsDir, "diagnostics-" + stamp + ".json");
            string csvPath = Path.Combine(resultsDir, "diagnostics-" + stamp + ".csv");

            File.WriteAllText(jsonPath, BuildJson(BenchmarkResults), Encoding.UTF8);
            File.WriteAllText(csvPath, BuildCsv(BenchmarkResults), Encoding.UTF8);

            Console.WriteLine();
            Console.WriteLine("Baseline results:");
            Console.WriteLine("  JSON " + jsonPath);
            Console.WriteLine("  CSV  " + csvPath);
        }

        /// <summary>
        /// Executes the build csv operation.
        /// </summary>
        private static string BuildCsv(List<BenchmarkResult> results)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("timestamp,machine,git,run,name,operations,clients,payload_bytes,elapsed_ms,ops_per_second,kib_per_second,p50_ms,p95_ms,p99_ms,gc0,gc1,gc2,memory_before,memory_after,failures");
            string timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            string machine = Environment.MachineName;
            string git = GetGitCommit();
            for (int i = 0; i < results.Count; i++)
            {
                BenchmarkResult result = results[i];
                AppendCsv(sb, timestamp);
                AppendCsv(sb, machine);
                AppendCsv(sb, git);
                sb.Append(result.Run).Append(',');
                AppendCsv(sb, result.Name);
                sb.Append(result.Operations).Append(',');
                sb.Append(result.Clients).Append(',');
                sb.Append(result.PayloadBytes).Append(',');
                sb.Append(result.ElapsedMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(result.OperationsPerSecond.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(result.KibPerSecond.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(result.P50Ms.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(result.P95Ms.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(result.P99Ms.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(result.Gen0Collections).Append(',');
                sb.Append(result.Gen1Collections).Append(',');
                sb.Append(result.Gen2Collections).Append(',');
                sb.Append(result.MemoryBefore).Append(',');
                sb.Append(result.MemoryAfter).Append(',');
                sb.Append(result.Failures);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Executes the append csv operation.
        /// </summary>
        private static void AppendCsv(StringBuilder sb, string value)
        {
            sb.Append('"');
            if (value != null)
                sb.Append(value.Replace("\"", "\"\""));
            sb.Append('"').Append(',');
        }

        /// <summary>
        /// Executes the build json operation.
        /// </summary>
        private static string BuildJson(List<BenchmarkResult> results)
        {
            string timestamp = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            string machine = Environment.MachineName;
            string git = GetGitCommit();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.Append("  \"timestamp\": \"").Append(JsonEscape(timestamp)).AppendLine("\",");
            sb.Append("  \"machine\": \"").Append(JsonEscape(machine)).AppendLine("\",");
            sb.Append("  \"git\": \"").Append(JsonEscape(git)).AppendLine("\",");
            sb.AppendLine("  \"benchmarks\": [");
            for (int i = 0; i < results.Count; i++)
            {
                BenchmarkResult result = results[i];
                sb.AppendLine("    {");
                sb.Append("      \"run\": ").Append(result.Run).AppendLine(",");
                sb.Append("      \"name\": \"").Append(JsonEscape(result.Name)).AppendLine("\",");
                sb.Append("      \"operations\": ").Append(result.Operations).AppendLine(",");
                sb.Append("      \"clients\": ").Append(result.Clients).AppendLine(",");
                sb.Append("      \"payload_bytes\": ").Append(result.PayloadBytes).AppendLine(",");
                sb.Append("      \"elapsed_ms\": ").Append(result.ElapsedMs.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.Append("      \"ops_per_second\": ").Append(result.OperationsPerSecond.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.Append("      \"kib_per_second\": ").Append(result.KibPerSecond.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.Append("      \"p50_ms\": ").Append(result.P50Ms.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.Append("      \"p95_ms\": ").Append(result.P95Ms.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.Append("      \"p99_ms\": ").Append(result.P99Ms.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.Append("      \"gc0\": ").Append(result.Gen0Collections).AppendLine(",");
                sb.Append("      \"gc1\": ").Append(result.Gen1Collections).AppendLine(",");
                sb.Append("      \"gc2\": ").Append(result.Gen2Collections).AppendLine(",");
                sb.Append("      \"memory_before\": ").Append(result.MemoryBefore).AppendLine(",");
                sb.Append("      \"memory_after\": ").Append(result.MemoryAfter).AppendLine(",");
                sb.Append("      \"failures\": ").Append(result.Failures).AppendLine();
                sb.Append("    }");
                if (i < results.Count - 1)
                    sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// Executes the json escape operation.
        /// </summary>
        private static string JsonEscape(string value)
        {
            if (value == null)
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        /// <summary>
        /// Executes the get git commit operation.
        /// </summary>
        private static string GetGitCommit()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo("git", "rev-parse --short HEAD");
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.CreateNoWindow = true;
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                        return string.Empty;
                    if (!process.WaitForExit(1000))
                        return string.Empty;
                    return process.StandardOutput.ReadToEnd().Trim();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Executes the percentile ms operation.
        /// </summary>
        private static double PercentileMs(long[] ticks, int count, double percentile)
        {
            if (count <= 0)
                return 0;

            long[] copy = new long[count];
            Array.Copy(ticks, copy, count);
            Array.Sort(copy);
            int index = (int)Math.Ceiling((percentile / 100.0) * count) - 1;
            if (index < 0)
                index = 0;
            if (index >= count)
                index = count - 1;
            return copy[index] * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>
        /// Executes the get free tcp port operation.
        /// </summary>
        private static int GetFreeTcpPort()
        {
            System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Executes the wait until operation.
        /// </summary>
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

        /// <summary>
        /// Executes the read frame operation.
        /// </summary>
        private static byte[] ReadFrame(NetworkStream stream)
        {
            byte[] header = ReadExact(stream, 4);
            int length = BitConverter.ToInt32(header, 0);
            Assert(length >= ConnectedClient.MinTcpMessageSize, "invalid frame length " + length);
            Assert(length <= ConnectedClient.MaxTcpMessageSize, "oversized frame length " + length);

            byte[] data = new byte[length];
            Buffer.BlockCopy(header, 0, data, 0, header.Length);
            byte[] body = ReadExact(stream, length - header.Length);
            Buffer.BlockCopy(body, 0, data, header.Length, body.Length);
            return data;
        }

        /// <summary>
        /// Executes the read exact operation.
        /// </summary>
        private static byte[] ReadExact(NetworkStream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                    throw new EndOfStreamException("socket closed while reading");
                offset += read;
            }
            return buffer;
        }

        /// <summary>
        /// Executes the expect throws operation.
        /// </summary>
        private static void ExpectThrows(Action action, string message)
        {
            try
            {
                action();
            }
            catch
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Executes the assert operation.
        /// </summary>
        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Executes the write int operation.
        /// </summary>
        private static void WriteInt(byte[] data, int offset, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
        }

        /// <summary>
        /// Represents the running server component.
        /// </summary>
        private sealed class RunningServer : IDisposable
        {
            /// <summary>
            /// Gets or sets the server value.
            /// </summary>
            public NetSquare.Server.NetSquareServer Server { get; private set; }
            /// <summary>
            /// Gets or sets the port value.
            /// </summary>
            public int Port { get; private set; }
            /// <summary>
            /// Stores the server thread value.
            /// </summary>
            private readonly Thread serverThread;
            /// <summary>
            /// Stores the clients value.
            /// </summary>
            private readonly List<NetSquare.Client.NetSquareClient> clients = new List<NetSquare.Client.NetSquareClient>();

            /// <summary>
            /// Executes the running server operation.
            /// </summary>
            private RunningServer(NetSquare.Server.NetSquareServer server, int port, Thread serverThread)
            {
                Server = server;
                Port = port;
                this.serverThread = serverThread;
            }

            /// <summary>
            /// Executes the start operation.
            /// </summary>
            public static RunningServer Start(NetSquareProtocoleType protocol, bool useWorldManager)
            {
                int port = GetFreeTcpPort();
                NetSquare.Server.NetSquareServer server = new NetSquare.Server.NetSquareServer(protocol, useWorldManager);
                Thread thread = new Thread(delegate () { server.Start(port, true, false, false); });
                thread.IsBackground = true;
                thread.Start();
                WaitUntil(delegate { return server.IsStarted; }, 5000, "test server did not start");
                return new RunningServer(server, port, thread);
            }

            /// <summary>
            /// Executes the connect client operation.
            /// </summary>
            public NetSquare.Client.NetSquareClient ConnectClient(NetSquareProtocoleType protocol, bool synchronizeUsingUdp)
            {
                NetSquare.Client.NetSquareClient client = new NetSquare.Client.NetSquareClient(false);
                ManualResetEventSlim connected = new ManualResetEventSlim(false);
                ManualResetEventSlim failed = new ManualResetEventSlim(false);
                client.OnConnected += delegate (uint id) { connected.Set(); };
                client.OnConnectionFail += delegate () { failed.Set(); };
                client.Connect("127.0.0.1", Port, protocol, synchronizeUsingUdp);

                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 10000)
                {
                    if (connected.IsSet)
                    {
                        clients.Add(client);
                        return client;
                    }
                    if (failed.IsSet)
                        throw new InvalidOperationException("client failed to connect");
                    Thread.Sleep(10);
                }

                throw new TimeoutException("client did not connect");
            }

            /// <summary>
            /// Executes the dispose operation.
            /// </summary>
            public void Dispose()
            {
                for (int i = 0; i < clients.Count; i++)
                {
                    try { clients[i].Disconnect(); } catch { }
                }

                try
                {
                    if (Server.Worlds != null)
                    {
                        foreach (NetSquareWorld world in Server.Worlds.Worlds.Values)
                            if (world.UseSynchronizer)
                                world.StopUsingSynchronizer();
                    }
                }
                catch { }

                try { Server.Statistics.Stop(); } catch { }
                try { Server.Stop(); } catch { }
                try
                {
                    if (serverThread != null && serverThread.IsAlive)
                        serverThread.Join(1000);
                }
                catch { }
            }
        }

        /// <summary>
        /// Represents the benchmark result component.
        /// </summary>
        private sealed class BenchmarkResult
        {
            /// <summary>
            /// Stores the run value.
            /// </summary>
            public int Run;
            /// <summary>
            /// Stores the name value.
            /// </summary>
            public string Name;
            /// <summary>
            /// Stores the operations value.
            /// </summary>
            public int Operations;
            /// <summary>
            /// Stores the bytes value.
            /// </summary>
            public long Bytes;
            /// <summary>
            /// Stores the elapsed ms value.
            /// </summary>
            public double ElapsedMs;
            /// <summary>
            /// Stores the operations per second value.
            /// </summary>
            public double OperationsPerSecond;
            /// <summary>
            /// Stores the kib per second value.
            /// </summary>
            public double KibPerSecond;
            /// <summary>
            /// Stores the clients value.
            /// </summary>
            public int Clients;
            /// <summary>
            /// Stores the payload bytes value.
            /// </summary>
            public int PayloadBytes;
            /// <summary>
            /// Stores the p50 ms value.
            /// </summary>
            public double P50Ms;
            /// <summary>
            /// Stores the p95 ms value.
            /// </summary>
            public double P95Ms;
            /// <summary>
            /// Stores the p99 ms value.
            /// </summary>
            public double P99Ms;
            /// <summary>
            /// Stores the gen0 collections value.
            /// </summary>
            public int Gen0Collections;
            /// <summary>
            /// Stores the gen1 collections value.
            /// </summary>
            public int Gen1Collections;
            /// <summary>
            /// Stores the gen2 collections value.
            /// </summary>
            public int Gen2Collections;
            /// <summary>
            /// Stores the memory before value.
            /// </summary>
            public long MemoryBefore;
            /// <summary>
            /// Stores the memory after value.
            /// </summary>
            public long MemoryAfter;
            /// <summary>
            /// Stores the failures value.
            /// </summary>
            public long Failures;
        }

        /// <summary>
        /// Represents the socket pair component.
        /// </summary>
        private sealed class SocketPair : IDisposable
        {
            /// <summary>
            /// Gets or sets the client value.
            /// </summary>
            public TcpClient Client { get; private set; }
            /// <summary>
            /// Gets or sets the connected value.
            /// </summary>
            public ConnectedClient Connected { get; private set; }
            /// <summary>
            /// Stores the server socket value.
            /// </summary>
            private readonly Socket serverSocket;
            /// <summary>
            /// Stores the listener value.
            /// </summary>
            private readonly System.Net.Sockets.TcpListener listener;

            /// <summary>
            /// Executes the socket pair operation.
            /// </summary>
            private SocketPair(System.Net.Sockets.TcpListener listener, TcpClient client, Socket serverSocket, ConnectedClient connected)
            {
                this.listener = listener;
                Client = client;
                this.serverSocket = serverSocket;
                Connected = connected;
            }

            /// <summary>
            /// Executes the create operation.
            /// </summary>
            public static SocketPair Create()
            {
                System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                Task<Socket> acceptTask = Task.Factory.StartNew(delegate { return listener.AcceptSocket(); });
                TcpClient client = new TcpClient();
                client.NoDelay = true;
                client.Connect(IPAddress.Loopback, port);

                Socket serverSocket = acceptTask.Result;
                serverSocket.NoDelay = true;

                ConnectedClient connected = new ConnectedClient();
                connected.ID = 1;
                connected.SetClient(serverSocket, false, false);
                listener.Stop();

                return new SocketPair(listener, client, serverSocket, connected);
            }

            /// <summary>
            /// Executes the dispose operation.
            /// </summary>
            public void Dispose()
            {
                try { Client.Close(); } catch { }
                try { serverSocket.Close(); } catch { }
                try { listener.Stop(); } catch { }
            }
        }
    }
}
#endregion
