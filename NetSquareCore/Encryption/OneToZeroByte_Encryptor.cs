using System.Collections;

#region Source
namespace NetSquare.Core.Encryption
{
    /// <summary>
    /// Represents the one to zero bit encryptor component.
    /// </summary>
    public class OneToZeroBit_Encryptor : Encryptor
    {
        /// <summary>
        /// Executes the decrypt operation.
        /// </summary>
        public override byte[] Decrypt(byte[] data)
        {
            BitArray array = new BitArray(data);
            for (int i = 0; i < array.Length; i++)
                array.Set(i, !array.Get(i));
            array.CopyTo(data, 0);
            return data;
        }

        /// <summary>
        /// Executes the encrypt operation.
        /// </summary>
        public override byte[] Encrypt(byte[] data)
        {
            BitArray array = new BitArray(data);
            for (int i = 0; i < array.Length; i++)
                array.Set(i, !array.Get(i));
            array.CopyTo(data, 0);
            return data;
        }
    }
}
#endregion
