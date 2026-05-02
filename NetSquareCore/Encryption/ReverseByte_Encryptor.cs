
using System;

#region Source
namespace NetSquare.Core.Encryption
{
    /// <summary>
    /// Represents the reverse byte encryptor component.
    /// </summary>
    public class ReverseByte_Encryptor : Encryptor
    {
        /// <summary>
        /// Executes the decrypt operation.
        /// </summary>
        public override byte[] Decrypt(byte[] data)
        {
            Array.Reverse(data);
            return data;
        }

        /// <summary>
        /// Executes the encrypt operation.
        /// </summary>
        public override byte[] Encrypt(byte[] data)
        {
            Array.Reverse(data);
            return data;
        }
    }
}
#endregion
