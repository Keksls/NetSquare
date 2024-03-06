namespace NetSquare.Core.Compression
{
    public class NoCompression : Compressor
    {
        public override byte[] Compress(byte[] buffer)
        {
            return buffer;
        }

        public override byte[] Decompress(byte[] buffer)
        {
            return buffer;
        }
    }
}