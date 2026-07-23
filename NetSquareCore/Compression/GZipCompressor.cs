using System.IO;
using System.IO.Compression;

#region Source
namespace NetSquare.Core.Compression
{
    /// <summary>
    /// Represents the g zip compressor component.
    /// </summary>
    public class GZipCompressor : Compressor
    {
        /// <summary>
        /// Executes the compress operation.
        /// </summary>
        public override byte[] Compress(byte[] data)
        {
            using (var compressedStream = new MemoryStream())
            using (var zipStream = new GZipStream(compressedStream, CompressionMode.Compress))
            {
                zipStream.Write(data, 0, data.Length);
                zipStream.Close();
                return compressedStream.ToArray();
            }
        }

        /// <summary>
        /// Executes the decompress operation.
        /// </summary>
        public override byte[] Decompress(byte[] data)
        {
            return Decompress(data, int.MaxValue);
        }

        /// <summary>
        /// Decompresses GZip data while enforcing an absolute output limit.
        /// </summary>
        /// <param name="data">Compressed input bytes.</param>
        /// <param name="maxOutputLength">Maximum accepted decompressed length.</param>
        /// <returns>The decompressed bytes.</returns>
        public override byte[] Decompress(byte[] data, int maxOutputLength)
        {
            ValidateDecompressionArguments(data, maxOutputLength);
            using (var compressedStream = new MemoryStream(data))
            using (var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            {
                return ReadBounded(zipStream, maxOutputLength);
            }
        }
    }
}
#endregion
