using Newtonsoft.Json;
using System;

namespace NetSquare.Core
{
    public class MessageBlockData
    {
        public MessageBlockType Type;
        public byte[] data;
        public int Size;

        public void SetByte(byte val)
        {
            data = new byte[1] { val };
            Type = MessageBlockType.Byte;
            Size = 1;
        }

        public byte GetByte()
        {
            return data[0];
        }

        public void SetShort(short val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Short;
            Size = 2;
        }

        public short GetShort()
        {
            return BitConverter.ToInt16(data, 0);
        }

        public void SetInt(int val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Int;
            Size = 4;
        }

        public int GetInt()
        {
            return BitConverter.ToInt32(data, 0);
        }

        public void SetLong(long val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Long;
            Size = 8;
        }

        public ushort GetUShort()
        {
            return BitConverter.ToUInt16(data, 0);
        }

        public void SetUShort(ushort val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.UShort;
            Size = 2;
        }

        public uint GetUint()
        {
            return BitConverter.ToUInt32(data, 0);
        }

        public void SetUInt(uint val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.UInt;
            Size = 4;
        }

        public ulong GetUlong()
        {
            return BitConverter.ToUInt64(data, 0);
        }

        public void SetULong(ulong val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.ULong;
            Size = 8;
        }

        public float GetFloat()
        {
            return BitConverter.ToSingle(data, 0);
        }

        public void SetFloat(float val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Float;
            Size = 4;
        }

        public bool GetBool()
        {
            return BitConverter.ToBoolean(data, 0);
        }

        public void SetBool(bool val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Bool;
            Size = 1;
        }

        public char Getchar()
        {
            return BitConverter.ToChar(data, 0);
        }

        public void SetChar(char val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Char;
            Size = 2;
        }

        public void SetString(string val)
        {
            data = System.Text.Encoding.Default.GetBytes(val);
            Type = MessageBlockType.String;
            Size = data.Length + 4;
        }

        /// <summary>
        /// Must be called only once by message and alwayse at the end
        /// </summary>
        /// <param name="val">serializable object (will be json and byte[] by Default))</param>
        public void SetObject(object val)
        {
            data = System.Text.Encoding.Default.GetBytes(JsonConvert.SerializeObject(val));
            Type = MessageBlockType.Custom;
            Size = data.Length + 4;
        }
    }

    public enum MessageBlockType
    {
        Byte = 0,
        Short = 1,
        Int = 2,
        Long = 3,
        Float = 4,
        Bool = 5,
        Char = 6,
        String = 7,
        Custom = 8,
        UShort = 9,
        UInt = 10,
        ULong = 11
    }
}