using System;
using System.Security.Cryptography;
using System.Text;

namespace NetSquare.Core.Encryption
{
    public class CaesarChipher_Encryptor : Encryptor
    {
        public override byte[] Decrypt(byte[] data)
        {
            int passwordShiftIndex = 0;
            bool shiftFlag = false;
            for (int i = 0; i < data.Length; i++)
            {
                int shift = KeyIV.Key[passwordShiftIndex % 31];
                data[i] = shift <= 128
                    ? (byte)(data[i] + (shiftFlag
                        ? (byte)(((shift << 2)) % 255)
                        : (byte)(((shift << 4)) % 255)))
                    : (byte)(data[i] - (shiftFlag
                        ? (byte)(((shift << 4)) % 255)
                        : (byte)(((shift << 2)) % 255)));
                passwordShiftIndex = (passwordShiftIndex + 1) % 64;
                shiftFlag = !shiftFlag;
            }
            return data;
        }

        public override byte[] Encrypt(byte[] data)
        {
            int passwordShiftIndex = 0;
            bool shiftFlag = false;
            for (int i = 0; i < data.Length; i++)
            {
                int shift = KeyIV.Key[passwordShiftIndex % 31];
                data[i] = shift <= 128
                    ? (byte)(data[i] - (shiftFlag
                        ? (byte)(((shift << 2)) % 255)
                        : (byte)(((shift << 4)) % 255)))
                    : (byte)(data[i] + (shiftFlag
                        ? (byte)(((shift << 4)) % 255)
                        : (byte)(((shift << 2)) % 255)));
                passwordShiftIndex = (passwordShiftIndex + 1) % 64;
                shiftFlag = !shiftFlag;
            }
            return data;
        }

        internal override void PostSetKey()
        {
            if (string.IsNullOrEmpty(password))
                throw new Exception("No key given. Please enter a key for Caesar Cipher.");
            else
            {
                using (SHA256 sha256Hash = SHA256.Create())
                    KeyIV.Key = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(password));
            }
        }
    }
}