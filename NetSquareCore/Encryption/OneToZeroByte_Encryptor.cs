using System.Collections;

namespace NetSquare.Core.Encryption
{
    public class OneToZeroBit_Encryptor : Encryptor
    {
        public override byte[] Decrypt(byte[] data)
        {
            BitArray array = new BitArray(data);
            for (int i = 0; i < array.Length; i++)
                array.Set(i, !array.Get(i));
            array.CopyTo(data, 0);
            return data;
        }

        public override byte[] Encrypt(byte[] data)
        {
            BitArray array = new BitArray(data);
            for (int i = 0; i < array.Length; i++)
                array.Set(i, !array.Get(i));
            array.CopyTo(data, 0);
            return data;
        }
    }
}