using System;
using System.Runtime.InteropServices;

namespace NetSquare.Core
{
    [StructLayout(LayoutKind.Explicit)]
    public struct UInt24 : IEquatable<UInt24>
    {
        [FieldOffset(0)]
        public byte b0;
        [FieldOffset(1)]
        public byte b1;
        [FieldOffset(2)]
        public byte b2;
        [FieldOffset(0)]
        public uint UInt32;

        public static readonly UInt24 zero = new UInt24(0);
        public const int MaxValue = 16777215;
        public const int HalfValue = 8388607;

        public static uint GetUInt(byte[] bytes, int offset = 0)
        {
            return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16));
        }

        public UInt24(UInt32 value)
        {
            b0 = 0;
            b1 = 0;
            b2 = 0;
            UInt32 = value;
        }

        public UInt24(byte _b0, byte _b1, byte _b2)
        {
            UInt32 = 0;
            b0 = _b0;
            b1 = _b1;
            b2 = _b2;
        }

        public UInt24(byte[] bytes, int offset)
        {
            UInt32 = 0;
            b0 = bytes[offset];
            b1 = bytes[offset + 1];
            b2 = bytes[offset + 2];
        }

        public byte[] GetBytes()
        {
            return new byte[] { b0, b1, b2 };
        }

        public bool Equals(UInt24 val)
        {
            return UInt32.Equals(val.UInt32);
        }

        public override string ToString()
        {
            return UInt32.ToString();
        }

        public override bool Equals(object obj)
        {
            if (!(obj is UInt24))
                return false;

            return Equals((UInt24)obj);
        }

        public override int GetHashCode()
        {
            return (int)UInt32;
        }
    }
}