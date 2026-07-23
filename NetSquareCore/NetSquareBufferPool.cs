using System.Collections.Concurrent;
using System;

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Represents the pooled byte buffer component.
    /// </summary>
    internal static class NetSquareBufferPool
    {
        /// <summary>
        /// Defines the max pooled buffer size constant.
        /// </summary>
        private const int MinimumBucketSize = 256;
        private const int MaximumBucketSize = 1024 * 1024;
        private const int BucketCount = 13;
        /// <summary>
        /// Stores fixed power-of-two buckets without dictionary lookups on the send hot path.
        /// </summary>
        private static readonly ConcurrentBag<byte[]>[] Buckets = CreateBuckets();

        /// <summary>
        /// Executes the rent operation.
        /// </summary>
        public static byte[] Rent(int minimumLength)
        {
            if (minimumLength < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumLength));
            if (minimumLength == 0)
                return Array.Empty<byte>();

            int bucketIndex = GetBucketIndex(minimumLength);
            if (bucketIndex < 0)
                return new byte[minimumLength];

            byte[] buffer;
            if (Buckets[bucketIndex].TryTake(out buffer))
                return buffer;
            return new byte[MinimumBucketSize << bucketIndex];
        }

        /// <summary>
        /// Executes the return operation.
        /// </summary>
        public static void Return(byte[] buffer)
        {
            if (buffer == null || buffer.Length < MinimumBucketSize || buffer.Length > MaximumBucketSize)
                return;

            int bucketIndex = GetBucketIndex(buffer.Length);
            if (bucketIndex < 0 || (MinimumBucketSize << bucketIndex) != buffer.Length)
                return;
            Buckets[bucketIndex].Add(buffer);
        }

        /// <summary>
        /// Creates every fixed power-of-two buffer bucket once during type initialization.
        /// </summary>
        /// <returns>Initialized buffer buckets.</returns>
        private static ConcurrentBag<byte[]>[] CreateBuckets()
        {
            ConcurrentBag<byte[]>[] buckets = new ConcurrentBag<byte[]>[BucketCount];
            for (int index = 0; index < buckets.Length; index++)
                buckets[index] = new ConcurrentBag<byte[]>();
            return buckets;
        }

        /// <summary>
        /// Resolves the smallest power-of-two bucket that can hold a requested length.
        /// </summary>
        /// <param name="minimumLength">Requested buffer length.</param>
        /// <returns>Bucket index, or -1 when the request exceeds the pooled range.</returns>
        private static int GetBucketIndex(int minimumLength)
        {
            if (minimumLength <= 0 || minimumLength > MaximumBucketSize)
                return -1;

            int bucketSize = MinimumBucketSize;
            int bucketIndex = 0;
            while (bucketSize < minimumLength)
            {
                bucketSize <<= 1;
                bucketIndex++;
            }
            return bucketIndex;
        }
    }
}
#endregion
