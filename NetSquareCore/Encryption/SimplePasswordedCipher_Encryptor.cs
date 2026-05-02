using System;
using System.Text;

#region Source
namespace NetSquare.Core.Encryption
{
    /// <summary>
    /// Represents the simple passworded cipher encryptor component.
    /// </summary>
    public class SimplePasswordedCipher_Encryptor : Encryptor
    {
        /// <summary>
        /// Executes the decrypt operation.
        /// </summary>
        public override byte[] Decrypt(byte[] data)
        {
            int keyIndex = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (keyIndex == KeyIV.Key.Length)
                    keyIndex = 0;
                data[i] = (byte)(data[i] - KeyIV.Key[keyIndex]);
                keyIndex++;
            }
            return data;
        }

        /// <summary>
        /// Executes the encrypt operation.
        /// </summary>
        public override byte[] Encrypt(byte[] data)
        {
            int keyIndex = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (keyIndex == KeyIV.Key.Length)
                    keyIndex = 0;
                data[i] = (byte)(data[i] + KeyIV.Key[keyIndex]);
                keyIndex++;
            }
            return data;
        }

        /// <summary>
        /// Executes the post set key operation.
        /// </summary>
        internal override void PostSetKey()
        {
            if (string.IsNullOrEmpty(password))
                throw new Exception("No key given. Please enter a key for SBC Cipher.");
            else
                KeyIV.Key = Encoding.UTF8.GetBytes(password);
        }
    }
}
#endregion
