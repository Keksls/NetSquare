using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

#region Source
namespace NetSquare.Core.Encryption
{
    /// <summary>
    /// Represents the rijndael encryptor component.
    /// </summary>
    public class Rijndael_Encryptor : Encryptor
    {
        /// <summary>
        /// Stores the encryptor value.
        /// </summary>
        private ICryptoTransform encryptor;
        /// <summary>
        /// Stores the decryptor value.
        /// </summary>
        private ICryptoTransform decryptor;

        /// <summary>
        /// Executes the decrypt operation.
        /// </summary>
        public override byte[] Decrypt(byte[] data)
        {
            byte[] decrypted = null;
            using (MemoryStream ms = new MemoryStream())
            {
                using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
                {
                    cs.Write(data, 0, data.Length);
                    cs.Close();

                }
                decrypted = ms.ToArray();
            }
            return decrypted;
        }

        /// <summary>
        /// Executes the encrypt operation.
        /// </summary>
        public override byte[] Encrypt(byte[] data)
        {
            byte[] encrypted = null;
            using (MemoryStream ms = new MemoryStream())
            {
                using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    cs.Write(data, 0, data.Length);
                    cs.Close();

                }
                encrypted = ms.ToArray();
            }
            return encrypted;
        }

        /// <summary>
        /// Executes the generate random salt operation.
        /// </summary>
        private static byte[] GenerateRandomSalt()
        {
            byte[] data = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                for (int i = 0; i < 10; i++)
                    rng.GetBytes(data);
            }
            return data;
        }

        /// <summary>
        /// Executes the post set key operation.
        /// </summary>
        internal override void PostSetKey()
        {
            if (string.IsNullOrEmpty(password))
                throw new Exception("No key given. Please enter a key for Rijndael Cipher.");
            else
            {
                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = 256;
#if NET8_0_OR_GREATER
                    byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), GenerateRandomSalt(), 52768, HashAlgorithmName.SHA256, (aes.KeySize / 8) + (aes.BlockSize / 8));
                    aes.Key = new byte[aes.KeySize / 8];
                    aes.IV = new byte[aes.BlockSize / 8];
                    System.Buffer.BlockCopy(derivedKey, 0, aes.Key, 0, aes.Key.Length);
                    System.Buffer.BlockCopy(derivedKey, aes.Key.Length, aes.IV, 0, aes.IV.Length);
#else
                    var key = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), GenerateRandomSalt(), 52768);
                    aes.Key = key.GetBytes(aes.KeySize / 8);
                    aes.IV = key.GetBytes(aes.BlockSize / 8);
#endif
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Mode = CipherMode.CBC;
                    KeyIV.Key = aes.Key;
                    KeyIV.IV = aes.IV;
                    encryptor = aes.CreateEncryptor(KeyIV.Key, KeyIV.IV);
                    decryptor = aes.CreateDecryptor(KeyIV.Key, KeyIV.IV);
                }
            }
        }
    }
}
#endregion
