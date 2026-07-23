namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Identifies a leased segment inside the preallocated Writer message slab.
    /// </summary>
    internal readonly struct WriterMessageBuffer
    {
        internal readonly char[] Characters;
        internal readonly int Offset;
        internal readonly int Capacity;
        internal readonly int PoolIndex;

        internal bool IsValid => Characters != null && PoolIndex >= 0;

        /// <summary>
        /// Initializes a leased message-buffer segment.
        /// </summary>
        internal WriterMessageBuffer(char[] characters, int offset, int capacity, int poolIndex)
        {
            // A lease only references the shared slab and never allocates its own array.
            Characters = characters;
            Offset = offset;
            Capacity = capacity;
            PoolIndex = poolIndex;
        }
    }
}
