using K4os.Compression.LZ4;

namespace NetSquare.Core.Compression
{
    public class LZ4Compressor : Compressor
    {
        public override byte[] Compress(byte[] data)
        {
            return LZ4Pickler.Pickle(data, LZ4Level.L00_FAST);
            //byte[] target = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
            //var encodedLength = LZ4Codec.Encode(data, 0, data.Length, target, 0, target.Length);
            //return target;
        }

        public override byte[] Decompress(byte[] data)
        {
            //byte[] target = new byte[data.Length * 255]; // or source.Length * 255 to be safe
            //var decoded = LZ4Codec.Decode(
            //    data, 0, data.Length,
            //    target, 0, target.Length);
            return LZ4Pickler.Unpickle(data);
        }
    }
}