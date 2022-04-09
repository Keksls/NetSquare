using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NetSquare.Core.Encryption
{
    public class AES_Encryptor : Encryptor
    {
        public ICryptoTransform encryptor { get; private set; }
        public ICryptoTransform decryptor { get; private set; }

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

        private static byte[] GenerateRandomSalt()
        {
            byte[] data = new byte[32];
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
            for (int i = 0; i < 10; i++)
                rng.GetBytes(data);
            return data;
        }

        internal override void PostSetKey()
        {
            var key = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), GenerateRandomSalt(), 52768);
            AesManaged aes = new AesManaged();
            aes.KeySize = 256;
            aes.Key = key.GetBytes(aes.KeySize / 8);
            aes.IV = key.GetBytes(aes.BlockSize / 8);
            aes.Padding = PaddingMode.PKCS7;
            aes.Mode = CipherMode.CBC;
            KeyIV.Key = aes.Key;
            KeyIV.IV = aes.IV;
            encryptor = aes.CreateEncryptor(KeyIV.Key, KeyIV.IV);
            decryptor = aes.CreateDecryptor(KeyIV.Key, KeyIV.IV);
        }
    }
}