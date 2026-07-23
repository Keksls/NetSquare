using System;
using System.Threading;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Provides lock-free leases over a single preallocated character slab.
    /// </summary>
    internal sealed class WriterMessageBufferPool
    {
        private readonly char[] characters;
        private readonly int[] nextIndices;
        private readonly int bufferSize;
        private long headState;

        /// <summary>
        /// Preallocates every message buffer and its lock-free free-list metadata.
        /// </summary>
        internal WriterMessageBufferPool(int bufferCount, int bufferSize)
        {
            // A single slab avoids thousands of long-lived array objects.
            if (bufferCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferCount));
            if (bufferSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize));

            this.bufferSize = bufferSize;
            characters = new char[checked(bufferCount * bufferSize)];
            nextIndices = new int[bufferCount];
            for (int index = 0; index < bufferCount - 1; index++)
                nextIndices[index] = index + 1;
            nextIndices[bufferCount - 1] = -1;
            headState = CreateHeadState(0, 0U);
        }

        /// <summary>
        /// Attempts to rent a buffer without locks or allocations.
        /// </summary>
        internal bool TryRent(out WriterMessageBuffer buffer)
        {
            // A Treiber free list makes concurrent producer rentals non-blocking.
            while (true)
            {
                long currentState = Volatile.Read(ref headState);
                int currentHead = GetHeadIndex(currentState);
                if (currentHead < 0)
                {
                    buffer = default(WriterMessageBuffer);
                    return false;
                }

                int nextIndex = Volatile.Read(ref nextIndices[currentHead]);
                long nextState = CreateHeadState(nextIndex, GetHeadVersion(currentState) + 1U);
                if (Interlocked.CompareExchange(ref headState, nextState, currentState) != currentState)
                    continue;

                buffer = new WriterMessageBuffer(characters, currentHead * bufferSize, bufferSize, currentHead);
                return true;
            }
        }

        /// <summary>
        /// Returns a leased buffer to the lock-free free list.
        /// </summary>
        internal void Return(in WriterMessageBuffer buffer)
        {
            // Only valid leases originating from this pool are returned.
            if (!buffer.IsValid || !ReferenceEquals(buffer.Characters, characters))
                return;

            int bufferIndex = buffer.PoolIndex;
            while (true)
            {
                long currentState = Volatile.Read(ref headState);
                int currentHead = GetHeadIndex(currentState);
                Volatile.Write(ref nextIndices[bufferIndex], currentHead);
                long nextState = CreateHeadState(bufferIndex, GetHeadVersion(currentState) + 1U);
                if (Interlocked.CompareExchange(ref headState, nextState, currentState) == currentState)
                    return;
            }
        }

        /// <summary>
        /// Packs a free-list index and an ABA-prevention version into one atomic value.
        /// </summary>
        private static long CreateHeadState(int index, uint version)
        {
            // Adding one reserves zero as the encoded empty-list sentinel.
            return ((long)version << 32) | (uint)(index + 1);
        }

        /// <summary>
        /// Extracts the free-list index from a packed head state.
        /// </summary>
        private static int GetHeadIndex(long state)
        {
            // The zero encoded value maps back to the empty-list index minus one.
            return unchecked((int)(uint)state) - 1;
        }

        /// <summary>
        /// Extracts the ABA-prevention version from a packed head state.
        /// </summary>
        private static uint GetHeadVersion(long state)
        {
            // Unsigned shifting preserves all version bits across wraparound.
            return (uint)((ulong)state >> 32);
        }
    }
}
