using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Describes the protocol selected by the server and its proof-of-work challenge.
    /// </summary>
    public sealed class HandshakeServerChallenge
    {
        public ushort SelectedWireProtocolVersion { get; private set; }
        public NetSquareProtocoleType SelectedTransport { get; private set; }
        public HandshakeCapabilities Capabilities { get; private set; }
        public byte ProofOfWorkDifficulty { get; private set; }
        public byte[] ServerNonce { get; private set; }
        public byte[] ClientHelloHash { get; private set; }

        /// <summary>
        /// Initializes one immutable server challenge description.
        /// </summary>
        public HandshakeServerChallenge(
            ushort selectedWireProtocolVersion,
            NetSquareProtocoleType selectedTransport,
            HandshakeCapabilities capabilities,
            byte proofOfWorkDifficulty,
            byte[] serverNonce,
            byte[] clientHelloHash)
        {
            SelectedWireProtocolVersion = selectedWireProtocolVersion;
            SelectedTransport = selectedTransport;
            Capabilities = capabilities;
            ProofOfWorkDifficulty = proofOfWorkDifficulty;
            // Preserve the negotiated challenge as a single immutable frame description.
            ServerNonce = serverNonce ?? throw new ArgumentNullException(nameof(serverNonce));
            ClientHelloHash = clientHelloHash ?? throw new ArgumentNullException(nameof(clientHelloHash));
        }
    }
}
