using System.Collections.Concurrent;
using System;
#if !NETFRAMEWORK
using System.Buffers;
#endif

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Represents the pooled byte buffer component.
    /// </summary>
    internal sealed class PooledByteBuffer : IDisposable
    {
        /// <summary>
        /// Gets or sets the buffer value.
        /// </summary>
        public byte[] Buffer { get; private set; }
        /// <summary>
        /// Gets or sets the length value.
        /// </summary>
        public int Length { get; private set; }
        /// <summary>
        /// Stores the pooled value.
        /// </summary>
        private readonly bool pooled;

        /// <summary>
        /// Executes the pooled byte buffer operation.
        /// </summary>
        public PooledByteBuffer(byte[] buffer, int length, bool pooled)
        {
            Buffer = buffer;
            Length = length;
            this.pooled = pooled;
        }

        /// <summary>
        /// Executes the wrap operation.
        /// </summary>
        public static PooledByteBuffer Wrap(byte[] buffer)
        {
            return new PooledByteBuffer(buffer, buffer != null ? buffer.Length : 0, false);
        }

        /// <summary>
        /// Executes the rent operation.
        /// </summary>
        public static PooledByteBuffer Rent(int length)
        {
            return new PooledByteBuffer(NetSquareBufferPool.Rent(length), length, true);
        }

        /// <summary>
        /// Executes the dispose operation.
        /// </summary>
        public void Dispose()
        {
            if (pooled && Buffer != null)
                NetSquareBufferPool.Return(Buffer);
            Buffer = null;
            Length = 0;
        }
    }

    /// <summary>
    /// Represents the net square buffer pool component.
    /// </summary>
    internal static class NetSquareBufferPool
    {
        /// <summary>
        /// Defines the max pooled buffer size constant.
        /// </summary>
        private const int MaxPooledBufferSize = 1024 * 1024;
        /// <summary>
        /// Stores the buckets value.
        /// </summary>
        private static readonly ConcurrentDictionary<int, ConcurrentBag<byte[]>> Buckets = new ConcurrentDictionary<int, ConcurrentBag<byte[]>>();

        /// <summary>
        /// Executes the rent operation.
        /// </summary>
        public static byte[] Rent(int minimumLength)
        {
#if !NETFRAMEWORK
            return ArrayPool<byte>.Shared.Rent(minimumLength);
#else
            int size = GetBucketSize(minimumLength);
            if (size > MaxPooledBufferSize)
                return new byte[minimumLength];

            ConcurrentBag<byte[]> bucket = Buckets.GetOrAdd(size, _ => new ConcurrentBag<byte[]>());
            byte[] buffer;
            if (bucket.TryTake(out buffer))
                return buffer;

            return new byte[size];
#endif
        }

        /// <summary>
        /// Executes the return operation.
        /// </summary>
        public static void Return(byte[] buffer)
        {
            if (buffer == null || buffer.Length > MaxPooledBufferSize)
                return;

#if !NETFRAMEWORK
            ArrayPool<byte>.Shared.Return(buffer);
#else
            int size = GetBucketSize(buffer.Length);
            if (size != buffer.Length)
                return;

            Buckets.GetOrAdd(size, _ => new ConcurrentBag<byte[]>()).Add(buffer);
#endif
        }

        /// <summary>
        /// Executes the get bucket size operation.
        /// </summary>
        private static int GetBucketSize(int minimumLength)
        {
            if (minimumLength <= 0)
                return 0;

            int size = 256;
            while (size < minimumLength && size < MaxPooledBufferSize)
                size <<= 1;
            return size < minimumLength ? minimumLength : size;
        }
    }
}
#endregion
