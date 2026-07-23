namespace NetSquare.Client
{
    /// <summary>
    /// Defines the final state of a client connection attempt.
    /// </summary>
    public enum ConnectionResultStatus
    {
        Connected = 0,
        Rejected = 1,
        TransportError = 2,
        TimedOut = 3,
        Cancelled = 4,
        AlreadyConnected = 5,
        ConnectionInProgress = 6
    }
}
