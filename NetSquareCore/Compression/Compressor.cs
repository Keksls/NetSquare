using System;
using System.IO;

#region Source
namespace NetSquare.Core.Compression
{
    /// <summary>
    /// Represents the compressor component.
    /// </summary>
    public abstract class Compressor
    {
        /// <summary>
        /// Executes the compress operation.
        /// </summary>
        public abstract byte[] Compress(byte[] buffer);
        /// <summary>
        /// Executes the decompress operation.
        /// </summary>
        public abstract byte[] Decompress(byte[] buffer);

        /// <summary>
        /// Decompresses a buffer while enforcing an absolute output limit.
        /// </summary>
        /// <param name="buffer">Compressed input bytes.</param>
        /// <param name="maxOutputLength">Maximum accepted decompressed length.</param>
        /// <returns>The decompressed bytes.</returns>
        public virtual byte[] Decompress(byte[] buffer, int maxOutputLength)
        {
            ValidateDecompressionArguments(buffer, maxOutputLength);
            byte[] result = Decompress(buffer);
            if (result == null || result.Length > maxOutputLength)
                throw new InvalidDataException("The decompressed payload exceeds the configured limit.");
            return result;
        }

        /// <summary>
        /// Copies a decompression stream into a bounded output buffer.
        /// </summary>
        /// <param name="source">Decompression stream.</param>
        /// <param name="maxOutputLength">Maximum accepted decompressed length.</param>
        /// <returns>The bounded decompressed bytes.</returns>
        protected static byte[] ReadBounded(Stream source, int maxOutputLength)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (maxOutputLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutputLength));

            byte[] copyBuffer = NetSquareBufferPool.Rent(Math.Min(81920, maxOutputLength));
            try
            {
                using (MemoryStream output = new MemoryStream())
                {
                    int totalLength = 0;
                    while (true)
                    {
                        int remaining = maxOutputLength - totalLength;
                        int readLength = remaining == 0
                            ? 1
                            : Math.Min(copyBuffer.Length, remaining);
                        int read = source.Read(
                            copyBuffer,
                            0,
                            readLength);
                        if (read <= 0)
                            break;
                        if (read > maxOutputLength - totalLength)
                            throw new InvalidDataException("The decompressed payload exceeds the configured limit.");

                        output.Write(copyBuffer, 0, read);
                        totalLength += read;
                    }
                    return output.ToArray();
                }
            }
            finally
            {
                NetSquareBufferPool.Return(copyBuffer);
            }
        }

        /// <summary>
        /// Validates bounded decompression arguments.
        /// </summary>
        /// <param name="buffer">Compressed input bytes.</param>
        /// <param name="maxOutputLength">Maximum accepted decompressed length.</param>
        protected static void ValidateDecompressionArguments(byte[] buffer, int maxOutputLength)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (maxOutputLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutputLength));
        }
    }
}
#endregion
