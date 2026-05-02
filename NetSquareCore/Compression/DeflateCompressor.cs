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
            MemoryStream input = new MemoryStream(data);
            MemoryStream output = new MemoryStream();
            using (DeflateStream dstream = new DeflateStream(input, CompressionMode.Decompress))
            {
                dstream.CopyTo(output);
            }
            return output.ToArray();
        }
    }
}
#endregion
