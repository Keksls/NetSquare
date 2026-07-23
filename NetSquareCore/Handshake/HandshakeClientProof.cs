using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Carries the client proof nonce and the hash binding it to the handshake transcript.
    /// </summary>
    public sealed class HandshakeClientProof
    {
        public ulong ProofNonce { get; private set; }
        public byte[] ProofHash { get; private set; }

        /// <summary>
        /// Initializes one immutable client proof.
        /// </summary>
        public HandshakeClientProof(ulong proofNonce, byte[] proofHash)
        {
            ProofNonce = proofNonce;
            // Preserve the proof nonce and its transcript-bound hash together.
            ProofHash = proofHash ?? throw new ArgumentNullException(nameof(proofHash));
        }
    }
}
