using System;
using System.Runtime.InteropServices;

namespace NetSquare.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public struct UInt24 : IEquatable<UInt24>
    {
        public byte b0 { get; internal set; }
        public byte b1 { get; internal set; }
        public byte b2 { get; internal set; }
        public uint UInt32 { get; private set; }

        public static readonly UInt24 zero = new UInt24(0);

        public static uint GetUInt(byte[] bytes, int offset = 0)
        {
            return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16));
        }

        public UInt24(UInt32 value)
        {
            b0 = (byte)((value) & 0xFF);
            b1 = (byte)((value >> 8) & 0xFF);
            b2 = (byte)((value >> 16) & 0xFF);
            UInt32 = (uint)(b0 | (b1 << 8) | (b2 << 16));
        }

        public UInt24(byte[] bytes, int offset)
        {
            b0 = bytes[offset];
            b1 = bytes[offset + 1];
            b2 = bytes[offset + 2];
            UInt32 = (uint)(b0 | (b1 << 8) | (b2 << 16));
        }

        public byte[] GetBytes()
        {
            return new byte[] { b0, b1, b2 };
        }

        public bool Equals(UInt24 val)
        {
            return b0 == val.b0 && b1 == val.b1 && b2 == val.b2;
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