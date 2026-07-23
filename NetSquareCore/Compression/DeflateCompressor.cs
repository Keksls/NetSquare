using System.IO;
using System.IO.Compression;

#region Source
namespace NetSquare.Core.Compression
{
    /// <summary>
    /// Represents the deflate compressor component.
    /// </summary>
    public class DeflateCompressor : Compressor
    {
        /// <summary>
        /// Executes the compress operation.
        /// </summary>
        public override byte[] Compress(byte[] data)
        {
            MemoryStream output = new MemoryStream();
            using (DeflateStream dstream = new DeflateStream(output, CompressionLevel.Optimal))
            {
                dstream.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        /// <summary>
        /// Executes the decompress operation.
        /// </summary>
        public override byte[] Decompress(byte[] data)
        {
            return Decompress(data, int.MaxValue);
        }

        /// <summary>
        /// Decompresses Deflate data while enforcing an absolute output limit.
        /// </summary>
        /// <param name="data">Compressed input bytes.</param>
        /// <param name="maxOutputLength">Maximum accepted decompressed length.</param>
        /// <returns>The decompressed bytes.</returns>
        public override byte[] Decompress(byte[] data, int maxOutputLength)
        {
            ValidateDecompressionArguments(data, maxOutputLength);
            using (MemoryStream input = new MemoryStream(data))
            using (DeflateStream dstream = new DeflateStream(input, CompressionMode.Decompress))
            {
                return ReadBounded(dstream, maxOutputLength);
            }
        }
    }
}
#endregion
