namespace NetSquare.Client
{
    /// <summary>
    /// Defines how world synchronization frames are transported.
    /// </summary>
    public enum NetSquareSyncTransport
    {
        /// <summary>
        /// Sends synchronization frames through TCP for reliable, ordered delivery.
        /// </summary>
        ReliableTcp = 0,

        /// <summary>
        /// Sends synchronization frames through UDP for lower latency without delivery guarantees.
        /// </summary>
        UnreliableUdp = 1
    }
}
