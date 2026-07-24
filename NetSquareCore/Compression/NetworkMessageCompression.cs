using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace NetSquare.Core.Compression
{
    /// <summary>
    /// Configures and executes optional per-message Deflate compression.
    /// </summary>
    public static class NetworkMessageCompression
    {
        #region Constants
        private const int DefaultMinimumBodyLength = 256;
        private const int DefaultMinimumSavings = 16;
        #endregion

        #region Fields
        private static int enabled;
        private static int minimumBodyLength = DefaultMinimumBodyLength;
        private static int minimumSavings = DefaultMinimumSavings;
        #endregion

        #region Properties
        public static bool Enabled
        {
            get { return Volatile.Read(ref enabled) != 0; }
            set { Volatile.Write(ref enabled, value ? 1 : 0); }
        }

        public static int MinimumBodyLength
        {
            get { return Volatile.Read(ref minimumBodyLength); }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                Volatile.Write(ref minimumBodyLength, value);
            }
        }

        public static int MinimumSavings
        {
            get { return Volatile.Read(ref minimumSavings); }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                Volatile.Write(ref minimumSavings, value);
            }
        }
        #endregion

        #region Compression
        /// <summary>
        /// Determines whether a message body is large enough to justify a compression attempt.
        /// </summary>
        /// <param name="bodyLength">Uncompressed message body length.</param>
        /// <returns>True when compression is enabled and the body reaches the configured threshold.</returns>
        internal static bool ShouldAttempt(int bodyLength)
        {
            return Enabled && bodyLength >= MinimumBodyLength;
        }

        /// <summary>
        /// Compresses one message body when the encoded result saves enough bytes.
        /// </summary>
        /// <param name="source">Buffer containing the uncompressed body.</param>
        /// <param name="offset">Body offset inside the source buffer.</param>
        /// <param name="count">Uncompressed body length.</param>
        /// <param name="metadataLength">Bytes required by the compressed message envelope.</param>
        /// <param name="compressed">Compressed bytes when the operation is beneficial.</param>
        /// <returns>True when the compressed representation should be used.</returns>
        internal static bool TryCompress(
            byte[] source,
            int offset,
            int count,
            int metadataLength,
            out byte[] compressed)
        {
            compressed = null;
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (offset < 0 || count < 0 || offset > source.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (metadataLength < 0)
                throw new ArgumentOutOfRangeException(nameof(metadataLength));
            if (!ShouldAttempt(count))
                return false;

            // Fastest favors realtime latency; the result is retained only when it reduces the wire size.
            using (MemoryStream output = new MemoryStream())
            {
                using (DeflateStream compressor = new DeflateStream(
                    output,
                    CompressionLevel.Fastest,
                    true))
                {
                    compressor.Write(source, offset, count);
                }

                int requiredSavings = MinimumSavings;
                if (output.Length + metadataLength + requiredSavings > count)
                    return false;

                compressed = output.ToArray();
                return true;
            }
        }

        /// <summary>
        /// Decompresses one Deflate body into an exact bounded output buffer.
        /// </summary>
        /// <param name="source">Buffer containing compressed bytes.</param>
        /// <param name="offset">Compressed body offset.</param>
        /// <param name="count">Compressed body length.</param>
        /// <param name="expectedLength">Exact accepted decompressed length.</param>
        /// <returns>The decompressed message body.</returns>
        internal static byte[] DecompressExact(
            byte[] source,
            int offset,
            int count,
            int expectedLength)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (offset < 0 || count < 0 || offset > source.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (expectedLength < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedLength));

            byte[] output = new byte[expectedLength];
            using (MemoryStream input = new MemoryStream(source, offset, count, false, true))
            using (DeflateStream decompressor = new DeflateStream(input, CompressionMode.Decompress))
            {
                int written = 0;
                while (written < output.Length)
                {
                    int read = decompressor.Read(output, written, output.Length - written);
                    if (read <= 0)
                        throw new InvalidDataException("The compressed message ended before its declared size.");
                    written += read;
                }

                // One additional decoded byte proves that the declared output size was forged.
                if (decompressor.ReadByte() != -1)
                    throw new InvalidDataException("The compressed message exceeds its declared size.");
            }
            return output;
        }
        #endregion
    }
}
