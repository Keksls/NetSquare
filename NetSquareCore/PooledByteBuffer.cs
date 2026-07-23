using System;
using System.Collections.Concurrent;
using System.Threading;

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Owns one send buffer and recycles both the byte array and its lightweight wrapper.
    /// </summary>
    internal sealed class PooledByteBuffer : IDisposable
    {
        #region Fields
        private const int MaximumCachedWrappers = 4096;
        private static readonly ConcurrentBag<PooledByteBuffer> Wrappers = new ConcurrentBag<PooledByteBuffer>();
        private static int cachedWrapperCount;
        private bool pooled;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the owned buffer.
        /// </summary>
        public byte[] Buffer { get; private set; }

        /// <summary>
        /// Gets the logical byte count stored in the buffer.
        /// </summary>
        public int Length { get; private set; }
        #endregion

        #region Factory Methods
        /// <summary>
        /// Wraps an externally owned exact-length array.
        /// </summary>
        /// <param name="buffer">Externally owned array.</param>
        /// <returns>Reusable wrapper that will not return the array to the pool.</returns>
        public static PooledByteBuffer Wrap(byte[] buffer)
        {
            return Create(buffer, buffer != null ? buffer.Length : 0, false);
        }

        /// <summary>
        /// Rents a pooled array and a reusable wrapper for one logical message length.
        /// </summary>
        /// <param name="length">Required logical message length.</param>
        /// <returns>Owned pooled buffer.</returns>
        public static PooledByteBuffer Rent(int length)
        {
            return Create(NetSquareBufferPool.Rent(length), length, true);
        }

        /// <summary>
        /// Gets a recycled wrapper or creates one when the cache is empty.
        /// </summary>
        /// <param name="buffer">Array owned by the wrapper.</param>
        /// <param name="length">Logical data length.</param>
        /// <param name="pooled">Whether the array must return to the byte pool.</param>
        /// <returns>Initialized wrapper.</returns>
        private static PooledByteBuffer Create(byte[] buffer, int length, bool pooled)
        {
            PooledByteBuffer wrapper;
            if (Wrappers.TryTake(out wrapper))
                Interlocked.Decrement(ref cachedWrapperCount);
            else
                wrapper = new PooledByteBuffer();

            wrapper.Buffer = buffer;
            wrapper.Length = length;
            wrapper.pooled = pooled;
            return wrapper;
        }
        #endregion

        #region IDisposable
        /// <summary>
        /// Returns owned resources to their bounded caches exactly once.
        /// </summary>
        public void Dispose()
        {
            byte[] buffer = Buffer;
            if (buffer == null)
                return;

            bool returnBuffer = pooled;
            Buffer = null;
            Length = 0;
            pooled = false;
            if (returnBuffer)
                NetSquareBufferPool.Return(buffer);

            int cachedCount = Interlocked.Increment(ref cachedWrapperCount);
            if (cachedCount <= MaximumCachedWrappers)
                Wrappers.Add(this);
            else
                Interlocked.Decrement(ref cachedWrapperCount);
        }
        #endregion
    }
}
#endregion