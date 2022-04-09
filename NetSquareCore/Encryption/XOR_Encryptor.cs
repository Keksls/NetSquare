using System;
using System.Text;

namespace NetSquare.Core.Encryption
{
    public class XOR_Encryptor : Encryptor
    {
        public override byte[] Decrypt(byte[] data)
        {
            return Encrypt(data);
        }

        public override byte[] Encrypt(byte[] data)
        {
            byte[] output = data;
            int keyIndex = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (keyIndex == KeyIV.Key.Length)
                    keyIndex = 0;
                data[i] = (byte)(data[i] ^ KeyIV.Key[keyIndex]);
                keyIndex++;
            }
            return data;
        }

        internal override void PostSetKey()
        {
            if (string.IsNullOrEmpty(password))
                throw new Exception("No key given. Please enter a key for XOR Cipher.");
            else
                KeyIV.Key = Encoding.UTF8.GetBytes(password);
        }
    }
}