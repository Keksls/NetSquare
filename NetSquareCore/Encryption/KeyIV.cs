using System;
using System.IO;

#region Source
namespace NetSquare.Core.Encryption
{
    [Serializable]
    /// <summary>
    /// Represents the key iv component.
    /// </summary>
    public class KeyIV
    {
        /// <summary>
        /// Defines the binary serialization magic.
        /// </summary>
        private const int BinaryMagic = 0x494B534E;
        /// <summary>
        /// Defines the binary serialization version.
        /// </summary>
        private const int BinaryVersion = 1;

        /// <summary>
        /// Gets or sets the key value.
        /// </summary>
        public byte[] Key { get; set; }
        /// <summary>
        /// Gets or sets the iv value.
        /// </summary>
        public byte[] IV { get; set; }

        /// <summary>
        /// Serializes this key and IV into a compact deterministic binary representation.
        /// </summary>
        public byte[] ToBinary()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(BinaryMagic);
                writer.Write(BinaryVersion);
                WriteByteArray(writer, Key);
                WriteByteArray(writer, IV);
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Deserializes a key and IV from the compact deterministic binary representation.
        /// </summary>
        public static KeyIV FromBinary(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            using (MemoryStream stream = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int magic = reader.ReadInt32();
                int version = reader.ReadInt32();
                if (magic != BinaryMagic || version != BinaryVersion)
                    throw new InvalidDataException("Invalid NetSquare KeyIV file.");

                return new KeyIV
                {
                    Key = ReadByteArray(reader),
                    IV = ReadByteArray(reader)
                };
            }
        }

        /// <summary>
        /// Writes a nullable byte array.
        /// </summary>
        private static void WriteByteArray(BinaryWriter writer, byte[] value)
        {
            if (value == null)
            {
                writer.Write(-1);
                return;
            }

            writer.Write(value.Length);
            writer.Write(value);
        }

        /// <summary>
        /// Reads a nullable byte array.
        /// </summary>
        private static byte[] ReadByteArray(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0)
                return null;

            return reader.ReadBytes(length);
        }
    }
}
#endregion
