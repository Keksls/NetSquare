namespace NetSquare.Core
{
    /// <summary>
    /// Defines why a server refused a connection before assigning a client ID.
    /// </summary>
    public enum ConnectionRejectionReason : byte
    {
        Unknown = 0,
        RejectedByServer = 1,
        ServerFull = 2,
        InvalidHandshake = 3,
        HandshakeTimeout = 4,
        ProtocolMismatch = 5,
        ServerError = 6,
        BannedTemporary = 7,
        BannedPermanent = 8
    }
}
