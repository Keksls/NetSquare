using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Carries the final negotiated settings before the client confirms readiness.
    /// </summary>
    public sealed class HandshakeServerAccept
    {
        public ushort SelectedWireProtocolVersion { get; private set; }
        public NetSquareProtocoleType SelectedTransport { get; private set; }
        public HandshakeCapabilities Capabilities { get; private set; }
        public byte[] SessionToken { get; private set; }
        public byte[] TranscriptHash { get; private set; }

        /// <summary>
        /// Initializes one immutable server acceptance description.
        /// </summary>
        public HandshakeServerAccept(
            ushort selectedWireProtocolVersion,
            NetSquareProtocoleType selectedTransport,
            HandshakeCapabilities capabilities,
            byte[] sessionToken,
            byte[] transcriptHash)
        {
            SelectedWireProtocolVersion = selectedWireProtocolVersion;
            SelectedTransport = selectedTransport;
            Capabilities = capabilities;
            SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
            // Preserve the accepted settings and their transcript binding together.
            TranscriptHash = transcriptHash ?? throw new ArgumentNullException(nameof(transcriptHash));
        }
    }
}
