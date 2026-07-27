using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Carries the definitive client ID and server-owned heartbeat policy.
    /// </summary>
    public sealed class HandshakeServerConnected
    {
        public uint ClientID { get; private set; }
        public bool HeartbeatEnabled { get; private set; }
        public int HeartbeatIntervalMilliseconds { get; private set; }
        public int HeartbeatTimeoutMilliseconds { get; private set; }
        public byte[] ReadyHash { get; private set; }

        /// <summary>
        /// Initializes one immutable connected frame description.
        /// </summary>
        /// <param name="clientID">Client ID allocated by the server.</param>
        /// <param name="heartbeatEnabled">Whether the client must start its heartbeat loop.</param>
        /// <param name="heartbeatIntervalMilliseconds">Delay between heartbeat requests.</param>
        /// <param name="heartbeatTimeoutMilliseconds">Maximum heartbeat reply wait.</param>
        /// <param name="readyHash">Hash binding this confirmation to the client ready frame.</param>
        public HandshakeServerConnected(
            uint clientID,
            bool heartbeatEnabled,
            int heartbeatIntervalMilliseconds,
            int heartbeatTimeoutMilliseconds,
            byte[] readyHash)
        {
            // Keep the server confirmation immutable after decoding.
            ClientID = clientID;
            HeartbeatEnabled = heartbeatEnabled;
            HeartbeatIntervalMilliseconds = heartbeatIntervalMilliseconds;
            HeartbeatTimeoutMilliseconds = heartbeatTimeoutMilliseconds;
            ReadyHash = readyHash ?? throw new ArgumentNullException(nameof(readyHash));
        }
    }
}
