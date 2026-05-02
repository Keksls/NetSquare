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
    }
}
#endregion
