#region Source
namespace NetSquare.Core.Compression
{
    /// <summary>
    /// Represents the no compression component.
    /// </summary>
    public class NoCompression : Compressor
    {
        /// <summary>
        /// Executes the compress operation.
        /// </summary>
        public override byte[] Compress(byte[] buffer)
        {
            return buffer;
        }

        /// <summary>
        /// Executes the decompress operation.
        /// </summary>
        public override byte[] Decompress(byte[] buffer)
        {
            return buffer;
        }

        /// <summary>
        /// Returns uncompressed data after enforcing the output limit.
        /// </summary>
        /// <param name="buffer">Input bytes.</param>
        /// <param name="maxOutputLength">Maximum accepted length.</param>
        /// <returns>The original input buffer.</returns>
        public override byte[] Decompress(byte[] buffer, int maxOutputLength)
        {
            ValidateDecompressionArguments(buffer, maxOutputLength);
            if (buffer.Length > maxOutputLength)
                throw new System.IO.InvalidDataException("The payload exceeds the configured limit.");
            return buffer;
        }
    }
}
#endregion
