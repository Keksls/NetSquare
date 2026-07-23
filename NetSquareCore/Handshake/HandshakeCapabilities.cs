using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Defines optional NetSquare features negotiated during the handshake.
    /// </summary>
    [Flags]
    public enum HandshakeCapabilities : uint
    {
        None = 0,
        Heartbeat = 1 << 0,
        HighPrecisionTimeSynchronization = 1 << 1,
        AuthenticatedUdpDatagrams = 1 << 2
    }
}
