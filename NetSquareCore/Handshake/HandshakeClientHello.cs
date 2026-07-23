using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Describes the protocol range and features requested by a NetSquare client.
    /// </summary>
    public sealed class HandshakeClientHello
    {
        public ushort MinimumWireProtocolVersion { get; private set; }
        public ushort MaximumWireProtocolVersion { get; private set; }
        public NetSquareProtocoleType RequestedTransport { get; private set; }
        public HandshakeCapabilities Capabilities { get; private set; }
        public Version LibraryVersion { get; private set; }
        public byte[] ClientNonce { get; private set; }

        /// <summary>
        /// Initializes one immutable client hello description.
        /// </summary>
        public HandshakeClientHello(
            ushort minimumWireProtocolVersion,
            ushort maximumWireProtocolVersion,
            NetSquareProtocoleType requestedTransport,
            HandshakeCapabilities capabilities,
            Version libraryVersion,
            byte[] clientNonce)
        {
            MinimumWireProtocolVersion = minimumWireProtocolVersion;
            MaximumWireProtocolVersion = maximumWireProtocolVersion;
            RequestedTransport = requestedTransport;
            Capabilities = capabilities;
            LibraryVersion = libraryVersion ?? throw new ArgumentNullException(nameof(libraryVersion));
            // Preserve the compatibility proposal as a single immutable frame description.
            ClientNonce = clientNonce ?? throw new ArgumentNullException(nameof(clientNonce));
        }
    }
}
