namespace NetSquare.Core.Compression
{
    public abstract class Compressor
    {
        public abstract byte[] Compress(byte[] buffer);
        public abstract byte[] Decompress(byte[] buffer);
    }
}