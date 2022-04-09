
using System;

namespace NetSquare.Core.Encryption
{
    public class ReverseByte_Encryptor : Encryptor
    {
        public override byte[] Decrypt(byte[] data)
        {
            Array.Reverse(data);
            return data;
        }

        public override byte[] Encrypt(byte[] data)
        {
            Array.Reverse(data);
            return data;
        }
    }
}