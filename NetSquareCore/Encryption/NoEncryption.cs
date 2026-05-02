#region Source
namespace NetSquare.Core.Encryption
{
    /// <summary>
    /// Represents the no encryption component.
    /// </summary>
    public class NoEncryption : Encryptor
    {
        /// <summary>
        /// Executes the decrypt operation.
        /// </summary>
        public override byte[] Decrypt(byte[] data)
        {
            return data;
        }

        /// <summary>
        /// Executes the encrypt operation.
        /// </summary>
        public override byte[] Encrypt(byte[] data)
        {
            return data;
        }
    }
}
#endregion
