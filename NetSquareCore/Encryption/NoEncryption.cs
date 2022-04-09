namespace NetSquare.Core.Encryption
{
    public class NoEncryption : Encryptor
    {
        public override byte[] Decrypt(byte[] data)
        {
            return data;
        }

        public override byte[] Encrypt(byte[] data)
        {
            return data;
        }
    }
}