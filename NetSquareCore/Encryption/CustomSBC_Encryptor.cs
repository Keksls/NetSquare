using System;
using System.Text;

namespace NetSquare.Core.Encryption
{
    public class CustomSBC_Encryptor : Encryptor
    {
        public override byte[] Decrypt(byte[] data)
        {
            int passwordShiftIndex = 0;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(data[i] - KeyIV.Key[passwordShiftIndex]);
                passwordShiftIndex = (passwordShiftIndex + 1) % KeyIV.Key.Length;
            }
            return data;
        }

        public override byte[] Encrypt(byte[] data)
        {
            int passwordShiftIndex = 0;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(data[i] + KeyIV.Key[passwordShiftIndex]);
                passwordShiftIndex = (passwordShiftIndex + 1) % KeyIV.Key.Length;
            }
            return data;
        }

        internal override void PostSetKey()
        {
            if (string.IsNullOrEmpty(password))
                throw new Exception("No key given. Please enter a key for SBC Cipher.");
            else
                KeyIV.Key = Encoding.UTF8.GetBytes(password);
        }
    }
}