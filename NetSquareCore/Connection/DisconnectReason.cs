namespace NetSquare.Core
{
    /// <summary>
    /// Defines why an established NetSquare connection ended.
    /// </summary>
    public enum DisconnectReason : byte
    {
        Unknown = 0,
        ClientRequest = 1,
        ServerRequest = 2,
        ServerShutdown = 3,
        Kicked = 4,
        Timeout = 5,
        ConnectionLost = 6,
        ProtocolError = 7,
        BannedTemporary = 8,
        BannedPermanent = 9
    }
}
