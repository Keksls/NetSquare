using NetSquare.Core.Compression;
using NetSquare.Core.Encryption;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace NetSquare.Core
{
    public static class ProtocoleManager
    {
        private static eCompression compressorType;
        private static Compressor compressor;
        private static eEncryption encryptorType;
        private static Encryptor encryptor;

        static ProtocoleManager()
        {
            SetEncryptor(eEncryption.NoEncryption);
            SetCompressor(eCompression.NoCompression);
        }

        #region Encryptor Initalisation
        /// <summary>
        /// Set Encryptor for next operations
        /// </summary>
        /// <param name="_encryptorType">The encryptor you want to use</param>
        /// <param name="key">The plainText key you want to use for the selected encryptor</param>
        public static void SetEncryptor(eEncryption _encryptorType, string key)
        {
            SetEncryptor(_encryptorType);
            if (encryptor != null)
                encryptor.SetKey(key);
        }

        /// <summary>
        /// Set Encryptor for next operations
        /// </summary>
        /// <param name="_encryptorType">The encryptor you want to use</param>
        /// <param name="key">The byte[] Key</param>
        /// <param name="IV">The byte[] IV</param>
        public static void SetEncryptor(eEncryption _encryptorType, byte[] key, byte[] IV)
        {
            SetEncryptor(_encryptorType);
            if (encryptor != null)
                encryptor.SetKey(key, IV);
        }

        /// <summary>
        /// Set Encryptor for next operations
        /// </summary>
        /// <param name="_encryptorType">The encryptor you want to use</param>
        /// <param name="keyIV">The KeyIV Instance you want to use (Key + IV)</param>
        public static void SetEncryptor(eEncryption _encryptorType, KeyIV keyIV)
        {
            SetEncryptor(_encryptorType);
            if (encryptor != null)
                encryptor.SetKey(keyIV);
        }

        /// <summary>
        /// Set Encryptor for next operations
        /// </summary>
        /// <param name="_encryptorType">The encryptor you want to use</param>
        public static void SetEncryptor(eEncryption _encryptorType)
        {
            encryptorType = _encryptorType;
            switch (encryptorType)
            {
                case eEncryption.ReverseByte:
                    encryptor = new ReverseByte_Encryptor();
                    break;
                case eEncryption.OneToZeroBit:
                    encryptor = new OneToZeroBit_Encryptor();
                    break;
                case eEncryption.AES:
                    encryptor = new AES_Encryptor();
                    break;
                case eEncryption.CaesarChipher:
                    encryptor = new CaesarChipher_Encryptor();
                    break;
                case eEncryption.Rijndael:
                    encryptor = new Rijndael_Encryptor();
                    break;
                case eEncryption.SimplePasswordedCipher:
                    encryptor = new SimplePasswordedCipher_Encryptor();
                    break;
                case eEncryption.CustomSBC:
                    encryptor = new CustomSBC_Encryptor();
                    break;
                case eEncryption.XOR:
                    encryptor = new XOR_Encryptor();
                    break;
                default:
                    encryptor = new NoEncryption();
                    break;
            }
        }
        #endregion

        #region Encryption
        /// <summary>
        /// Encrypt a byte[] with the selected Encryptor
        /// </summary>
        /// <param name="data">the byte[] you want to Encrypt</param>
        /// <returns>Encrypted byte[]</returns>
        public static byte[] Encrypt(byte[] data)
        {
            return encryptor.Encrypt(data);
        }

        /// <summary>
        /// Decrypt a byte[] with the selected Encryptor
        /// </summary>
        /// <param name="data">the byte[] you want to Decrypt</param>
        /// <returns>Decrypted byte[]</returns>
        public static byte[] Decrypt(byte[] data)
        {
            return encryptor.Decrypt(data);
        }
        #endregion

        #region Key and IV
        /// <summary>
        /// Save a binary file that contain the KeyIV data currently used by the selected Encryptor
        /// </summary>
        /// <param name="path">the path the file you want to save</param>
        public static void SaveCurrentKeyIV(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
            using (MemoryStream stream = new MemoryStream())
            {
                var binaryFormatter = new BinaryFormatter();
                binaryFormatter.Serialize(stream, encryptor.KeyIV);
                File.WriteAllBytes(path, new OneToZeroBit_Encryptor().Encrypt(stream.ToArray()));
                stream.Close();
            }
        }

        /// <summary>
        /// Get a saved KeyIV instance from a binaryFile (must be created with SaveCurrentKeyIV)
        /// </summary>
        /// <param name="path">the path of the binary file</param>
        /// <returns>The KeyIV INstance from the file</returns>
        public static KeyIV GetKeyFromFile(string path)
        {
            using (MemoryStream stream = new MemoryStream(new OneToZeroBit_Encryptor().Decrypt(File.ReadAllBytes(path))))
                return (KeyIV)new BinaryFormatter().Deserialize(stream);
        }

        /// <summary>
        /// Set KeyIV Instance from a binaryFile (must be created with SaveCurrentKeyIV) to the current Encryptor
        /// </summary>
        /// <param name="path">the path of the binary file</param>
        public static void SetEncryptorKeyFromFile(string path)
        {
            encryptor.SetKey(GetKeyFromFile(path));
        }
        #endregion

        #region Compression
        public static void SetCompressor(eCompression _compressorType)
        {
            compressorType = _compressorType;
            switch (compressorType)
            {
                default:
                case eCompression.NoCompression:
                    compressor = new NoCompression();
                    break;
                case eCompression.GZipCompression:
                    compressor = new GZipCompressor();
                    break;
                case eCompression.DeflateCompression:
                    compressor = new DeflateCompressor();
                    break;
                case eCompression.LZ4Compression:
                    compressor = new LZ4Compressor();
                    break;
            }
        }

        public static byte[] Compress(byte[] buffer)
        {
            return compressor.Compress(buffer);
        }

        public static byte[] Decompress(byte[] buffer)
        {
            return compressor.Decompress(buffer);
        }
        #endregion
    }
}