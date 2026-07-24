using NetSquare.Client;
using NetSquare.Core;
using System;
using System.Diagnostics;
using System.Threading;

#region Source
namespace Client_Test
{
    /// <summary>
    /// Represents one instrumented load-test client.
    /// </summary>
    public class ClientRoutine
    {
        #region Constants
        private const int ConnectionTimeoutMilliseconds = 10000;
        private const int JoinTimeoutMilliseconds = 10000;
        #endregion

        #region Fields
        public NetSquareClient client;
        public int LineIndex;

        private static readonly Stopwatch LocalClock = Stopwatch.StartNew();
        private static int timeSynchronizationStarted;
        private static float serverOffset;

        private readonly ManualResetEventSlim startupCompleted = new ManualResetEventSlim(false);
        private int startupState;
        private int readyToSync;
        private NetsquareTransformFrame currentPos;
        private readonly float xOffset = 100f;
        private readonly float zOffset = 100f;
        private Stopwatch clientClock;
        #endregion

        #region Properties
        public static float Time
        {
            get { return EnlapsedTime + Volatile.Read(ref serverOffset); }
        }

        public static float EnlapsedTime
        {
            get { return (float)LocalClock.Elapsed.TotalSeconds; }
        }

        public bool IsReady
        {
            get { return Volatile.Read(ref readyToSync) != 0; }
        }
        #endregion

        #region Startup
        /// <summary>
        /// Connects the client and waits a bounded amount of time for its world join.
        /// </summary>
        /// <param name="x">Initial local X position.</param>
        /// <param name="y">Initial local Y position.</param>
        /// <param name="z">Initial local Z position.</param>
        /// <returns>True when the client is connected and joined to the world.</returns>
        public bool Start(float x, float y, float z)
        {
            // Typed connection results and terminal events prevent one failed bot from blocking the whole test.
            clientClock = Stopwatch.StartNew();
            client = new NetSquareClient();
            client.OnConnected += Client_Connected;
            client.OnDisconnected += Client_Disconnected;
            client.OnException += Client_Exception;
            client.WorldsManager.OnReceiveSynchFrames += WorldsManager_OnClientMove;
            currentPos = new NetsquareTransformFrame(
                x + xOffset,
                y,
                z + zOffset,
                0f,
                0f,
                0f,
                1f,
                0f);

            ConnectionResult result;
            try
            {
                result = client.ConnectAsync(
                    "127.0.0.1",
                    5050,
                    NetSquareProtocoleType.TCP,
                    false,
                    ConnectionTimeoutMilliseconds).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Client slot " + LineIndex + " connection exception: " + ex);
                CompleteStartup(false);
                return false;
            }

            if (!result.IsConnected)
            {
                Console.WriteLine(
                    "Client slot " + LineIndex + " connection failed: " +
                    DescribeConnectionResult(result));
                CompleteStartup(false);
                return false;
            }

            if (!startupCompleted.Wait(JoinTimeoutMilliseconds))
            {
                Console.WriteLine(
                    "Client " + result.ClientID + " world join timed out after " +
                    JoinTimeoutMilliseconds + " ms.");
                CompleteStartup(false);
                client.Disconnect();
                return false;
            }

            return Volatile.Read(ref startupState) == 1 && IsReady;
        }

        /// <summary>
        /// Publishes the first terminal startup result and wakes the creator thread.
        /// </summary>
        /// <param name="success">Whether connection and world join completed.</param>
        private void CompleteStartup(bool success)
        {
            // Only the first join, failure, disconnection, or timeout decides startup.
            int completedState = success ? 1 : -1;
            if (Interlocked.CompareExchange(ref startupState, completedState, 0) == 0)
            {
                Volatile.Write(ref readyToSync, success ? 1 : 0);
                startupCompleted.Set();
            }
        }

        /// <summary>
        /// Formats a typed connection result for load-test diagnostics.
        /// </summary>
        /// <param name="result">Connection result to describe.</param>
        /// <returns>Human-readable status with rejection or exception details.</returns>
        private static string DescribeConnectionResult(ConnectionResult result)
        {
            // Preserve the protocol-level cause so transport resets and capacity rejections are distinguishable.
            if (result == null)
                return "no result";

            string details = result.Status.ToString();
            if (result.RejectionInfo != null)
            {
                details += " / " + result.RejectionInfo.Reason;
                if (!string.IsNullOrWhiteSpace(result.RejectionInfo.Message))
                    details += ": " + result.RejectionInfo.Message;
            }
            if (result.Exception != null)
                details += ": " + result.Exception.Message;
            return details;
        }
        #endregion

        #region Connection Events
        /// <summary>
        /// Starts time synchronization without blocking the connection callback, then joins the test world.
        /// </summary>
        /// <param name="clientID">Assigned client identifier.</param>
        private void Client_Connected(uint clientID)
        {
            // One shared sample is enough for all local bots and avoids serial connection stalls.
            if (Interlocked.CompareExchange(ref timeSynchronizationStarted, 1, 0) == 0)
            {
                client.SyncTime(
                    () => clientClock.ElapsedMilliseconds / 1000f,
                    5,
                    1000,
                    serverTime =>
                    {
                        Volatile.Write(ref serverOffset, serverTime - EnlapsedTime);
                        Console.WriteLine("Shared server time synchronized by client " + clientID + ".");
                    });
            }

            Console.WriteLine("Connected with ID " + clientID);
            currentPos.Set(Time);
            client.WorldsManager.TryJoinWorld(1, currentPos, success =>
            {
                // Join completion is the readiness boundary consumed by the creation loop.
                if (success)
                {
                    Console.WriteLine("Client " + clientID + " Join lobby");
                    CompleteStartup(true);
                    return;
                }

                Console.WriteLine("Client " + clientID + " Fail lobby");
                CompleteStartup(false);
                client.Disconnect();
            });
        }

        /// <summary>
        /// Reports the typed terminal reason and releases any startup waiter.
        /// </summary>
        /// <param name="info">Typed disconnection information.</param>
        private void Client_Disconnected(DisconnectInfo info)
        {
            // Detailed reasons make server timeouts, explicit kicks, and raw socket loss immediately visible.
            Volatile.Write(ref readyToSync, 0);
            string details = info != null ? info.Reason.ToString() : DisconnectReason.Unknown.ToString();
            if (info != null && !string.IsNullOrWhiteSpace(info.Message))
                details += ": " + info.Message;
            Console.WriteLine(
                "Client " + (client != null ? client.ClientID : 0) +
                " disconnected: " + details);
            CompleteStartup(false);
        }

        /// <summary>
        /// Reports transport, framing, callback, and worker exceptions with their stack traces.
        /// </summary>
        /// <param name="exception">Observed client exception.</param>
        private void Client_Exception(Exception exception)
        {
            // Keep the complete exception because the first transport failure is the useful load-test signal.
            Console.WriteLine(
                "Client " + (client != null ? client.ClientID : 0) +
                " exception: " + exception);
        }
        #endregion

        #region World Events
        /// <summary>
        /// Displays the most recent transform observed by the first client.
        /// </summary>
        /// <param name="clientID">Source client identifier.</param>
        /// <param name="frames">Received synchronization frames.</param>
        private void WorldsManager_OnClientMove(uint clientID, INetSquareSynchFrame[] frames)
        {
            // Limit verbose transform output to the first bot.
            if (client.ClientID == 1 &&
                NetSquareSynchFramesUtils.TryGetMostRecentTransformFrame(
                    frames,
                    out NetsquareTransformFrame transform))
            {
                Console.WriteLine(
                    "Client " + clientID + " move to " +
                    transform.x + ", " + transform.y + ", " + transform.z +
                    " at time " + transform.Time);
            }
        }

        /// <summary>
        /// Displays the server welcome message.
        /// </summary>
        /// <param name="message">Received welcome message.</param>
        [NetSquareAction((ushort)MessagesTypes.WelcomeMessage)]
        public static void ReceivingDebugMessageFromServer(NetworkMessage message)
        {
            // This route verifies that reflected client actions remain functional under load.
            string text = message.Serializer.GetString();
            Console.WriteLine("from Server : " + text);
        }
        #endregion

        #region Update
        /// <summary>
        /// Updates the local transform and optionally sends all stored frames.
        /// </summary>
        /// <param name="send">Whether pending frames must be transmitted now.</param>
        /// <param name="x">Current local X position.</param>
        /// <param name="y">Current local Z position.</param>
        public void Update(bool send, float x, float y)
        {
            // Disconnected and partially initialized bots never produce synchronization traffic.
            if (!IsReady)
                return;

            currentPos = new NetsquareTransformFrame(
                x + xOffset,
                1f,
                y + zOffset,
                0f,
                0f,
                0f,
                1f,
                Time);
            client.WorldsManager.StoreSynchFrame(currentPos);
            if (send)
                client.WorldsManager.SendFrames();
        }
        #endregion
    }

    internal enum MessagesTypes
    {
        WelcomeMessage = 0
    }
}
#endregion
