using NetSquare.Client;
using NetSquare.Core;
using System;
using System.Diagnostics;

#region Source
namespace Client_Test
{
    /// <summary>
    /// Represents the client routine component.
    /// </summary>
    public class ClientRoutine
    {
        /// <summary>
        /// Stores the client value.
        /// </summary>
        public NetSquareClient client;
        bool readyToSync = false;
        /// <summary>
        /// Stores the server offset value.
        /// </summary>
        private static float serverOffset = 0;
        /// <summary>
        /// Gets or sets the time value.
        /// </summary>
        public static float Time { get { return EnlapsedTime + serverOffset; } }
        /// <summary>
        /// Gets or sets the enlapsed time value.
        /// </summary>
        public static float EnlapsedTime { get { return (float)(DateTime.Now - startTime).TotalSeconds; } }
        /// <summary>
        /// Stores the start time value.
        /// </summary>
        private static DateTime startTime;
        /// <summary>
        /// Stores the line index value.
        /// </summary>
        public int LineIndex = 0;
        NetsquareTransformFrame currentPos;
        /// <summary>
        /// Stores the x offset value.
        /// </summary>
        private float xOffset = 100;
        /// <summary>
        /// Stores the z offset value.
        /// </summary>
        private float zOffset = 100;
        /// <summary>
        /// Gets or sets the time synced value.
        /// </summary>
        private static bool timeSynced => nbTimeSynced > 1;
        /// <summary>
        /// Stores the nb time synced value.
        /// </summary>
        private static int nbTimeSynced = 0;
        /// <summary>
        /// Stores the sw value.
        /// </summary>
        private Stopwatch sw;

        /// <summary>
        /// Executes the start operation.
        /// </summary>
        public void Start(float x, float y, float z)
        {
            sw = Stopwatch.StartNew();
            client = new NetSquareClient();
            client.OnConnected += Client_Connected;
            client.OnConnectionFail += Client_ConnectionFail;
            client.OnDisconected += Client_Disconected;
            client.WorldsManager.OnReceiveSynchFrames += WorldsManager_OnClientMove;
            currentPos = new NetsquareTransformFrame(x + xOffset, y, z + zOffset, 0, 0, 0, 1f, 0);
            client.Connect("127.0.0.1", 5050, NetSquareProtocoleType.TCP, false);
        }

        /// <summary>
        /// Executes the worlds manager on client move operation.
        /// </summary>
        private void WorldsManager_OnClientMove(uint clientID, INetSquareSynchFrame[] frames)
        {
            if (client.ClientID == 1)
            {
                if (NetSquareSynchFramesUtils.TryGetMostRecentTransformFrame(frames, out NetsquareTransformFrame transform))
                {
                    Console.WriteLine("Client " + clientID + " move to " + transform.x + ", " + transform.y + ", " + transform.z + " at time " + transform.Time);
                }
            }
        }

        /// <summary>
        /// Executes the client disconected operation.
        /// </summary>
        private void Client_Disconected()
        {
            Console.WriteLine("Client Disconnected");
        }

        /// <summary>
        /// Executes the client connection fail operation.
        /// </summary>
        private void Client_ConnectionFail()
        {
            Console.WriteLine("Client Connection fail");
        }

        /// <summary>
        /// Executes the client connected operation.
        /// </summary>
        private void Client_Connected(uint ID)
        {
            if (!timeSynced)
            {
                startTime = DateTime.Now;
                client.SyncTime(() => { return sw.ElapsedMilliseconds / 1000f; }, 5, 1000, (serverTime) =>
                {
                    nbTimeSynced++;
                    serverOffset = serverTime - EnlapsedTime;
                });

                while (!timeSynced)
                {
                    System.Threading.Thread.Sleep(10);
                }
            }

            Console.WriteLine("Connected with ID " + ID);
            currentPos.Set(Time);
            client.WorldsManager.TryJoinWorld(1, currentPos, (success) =>
            {
                if (success)
                {
                    Console.WriteLine("Client " + ID + " Join lobby");
                    readyToSync = true;
                }
                else
                {
                    Console.WriteLine("Client " + ID + " Fail lobby");
                    client.Disconnect();
                }
            });
        }

        [NetSquareAction((ushort)MessagesTypes.WelcomeMessage)]
        /// <summary>
        /// Executes the receiving debug message from server operation.
        /// </summary>
        public static void ReceivingDebugMessageFromServer(NetworkMessage message)
        {
            string text = message.Serializer.GetString();
            Console.WriteLine("from Server : " + text);
        }

        /// <summary>
        /// Executes the update operation.
        /// </summary>
        public void Update(bool send, float x, float y)
        {
            if (!readyToSync)
                return;
            currentPos = new NetsquareTransformFrame(x + xOffset, 1f, y + zOffset, 0, 0, 0, 1, Time);
            client.WorldsManager.StoreSynchFrame(currentPos);
            if (send)
            {
                client.WorldsManager.SendFrames();
            }
        }
    }

    enum MessagesTypes
    {
        WelcomeMessage = 0
    }
}
#endregion
