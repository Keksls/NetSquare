using NetSquare.Core;
using System;

namespace NetSquare.Client
{
    /// <summary>
    /// Carries typed server rejection feedback through the asynchronous handshake reader.
    /// </summary>
    internal sealed class HandshakeRejectedException : Exception
    {
        public ConnectionRejectionInfo RejectionInfo { get; private set; }

        /// <summary>
        /// Initializes an exception for one server-side handshake rejection.
        /// </summary>
        /// <param name="rejectionInfo">Typed rejection received from the server.</param>
        public HandshakeRejectedException(ConnectionRejectionInfo rejectionInfo)
            : base(rejectionInfo?.Message ?? "The NetSquare server rejected the connection.")
        {
            // Preserve the typed reason without converting it into a generic transport failure.
            RejectionInfo = rejectionInfo ?? throw new ArgumentNullException(nameof(rejectionInfo));
        }
    }
}
