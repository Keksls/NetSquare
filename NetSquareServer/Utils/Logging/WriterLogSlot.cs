namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Stores one preallocated ring-buffer sequence and log entry.
    /// </summary>
    internal struct WriterLogSlot
    {
        internal long Sequence;
        internal NetSquareLogEntry Entry;
    }
}
