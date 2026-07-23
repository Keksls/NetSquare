using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Carries the definitive client ID after the server receives the ready acknowledgement.
    /// </summary>
    public sealed class HandshakeServerConnected
    {
        public uint ClientID { get; private set; }
        public byte[] ReadyHash { get; private set; }

        /// <summary>
        /// Initializes one immutable connected frame description.
        /// </summary>
        public HandshakeServerConnected(uint clientID, byte[] readyHash)
        {
            // Keep the server confirmation immutable after decoding.
            ClientID = clientID;
            ReadyHash = readyHash ?? throw new ArgumentNullException(nameof(readyHash));
        }
    }
}
