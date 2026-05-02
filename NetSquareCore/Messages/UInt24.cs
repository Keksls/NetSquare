using System;
using System.Runtime.InteropServices;

#region Source
namespace NetSquare.Core
{
    [StructLayout(LayoutKind.Explicit)]
    /// <summary>
    /// Represents the u int24 value.
    /// </summary>
    public struct UInt24 : IEquatable<UInt24>
    {
        [FieldOffset(0)]
        /// <summary>
        /// Stores the b0 value.
        /// </summary>
        public byte b0;
        [FieldOffset(1)]
        /// <summary>
        /// Stores the b1 value.
        /// </summary>
        public byte b1;
        [FieldOffset(2)]
        /// <summary>
        /// Stores the b2 value.
        /// </summary>
        public byte b2;
        [FieldOffset(0)]
        /// <summary>
        /// Stores the u int32 value.
        /// </summary>
        public uint UInt32;

        /// <summary>
        /// Stores the zero value.
        /// </summary>
        public static readonly UInt24 zero = new UInt24(0);
        /// <summary>
        /// Defines the max value constant.
        /// </summary>
        public const int MaxValue = 16777215;
        /// <summary>
        /// Defines the half value constant.
        /// </summary>
        public const int HalfValue = 8388607;

        /// <summary>
        /// Executes the get u int operation.
        /// </summary>
        public static uint GetUInt(byte[] bytes, int offset = 0)
        {
            return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16));
        }

        /// <summary>
        /// Initializes a new instance of the u int24 class.
        /// </summary>
        public UInt24(UInt32 value)
        {
            b0 = 0;
            b1 = 0;
            b2 = 0;
            UInt32 = value;
        }

        /// <summary>
        /// Initializes a new instance of the u int24 class.
        /// </summary>
        public UInt24(byte _b0, byte _b1, byte _b2)
        {
            UInt32 = 0;
            b0 = _b0;
            b1 = _b1;
            b2 = _b2;
        }

        /// <summary>
        /// Initializes a new instance of the u int24 class.
        /// </summary>
        public UInt24(byte[] bytes, int offset)
        {
            UInt32 = 0;
            b0 = bytes[offset];
            b1 = bytes[offset + 1];
            b2 = bytes[offset + 2];
        }

        /// <summary>
        /// Executes the get bytes operation.
        /// </summary>
        public byte[] GetBytes()
        {
            return new byte[] { b0, b1, b2 };
        }

        /// <summary>
        /// Executes the equals operation.
        /// </summary>
        public bool Equals(UInt24 val)
        {
            return UInt32.Equals(val.UInt32);
        }

        /// <summary>
        /// Executes the to string operation.
        /// </summary>
        public override string ToString()
        {
            return UInt32.ToString();
        }

        /// <summary>
        /// Executes the equals operation.
        /// </summary>
        public override bool Equals(object obj)
        {
            if (!(obj is UInt24))
                return false;

            return Equals((UInt24)obj);
        }

        /// <summary>
        /// Executes the get hash code operation.
        /// </summary>
        public override int GetHashCode()
        {
            return (int)UInt32;
        }
    }
}
#endregion
