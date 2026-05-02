using System;
using System.Security.Cryptography;
using System.Text;

#region Source
namespace NetSquare.Core.Encryption
{
    /// <summary>
    /// Represents the caesar chipher encryptor component.
    /// </summary>
    public class CaesarChipher_Encryptor : Encryptor
    {
        /// <summary>
        /// Executes the decrypt operation.
        /// </summary>
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

        /// <summary>
        /// Executes the encrypt operation.
        /// </summary>
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

        /// <summary>
        /// Executes the post set key operation.
        /// </summary>
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
#endregion
