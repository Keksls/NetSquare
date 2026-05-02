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
    }
}
#endregion
