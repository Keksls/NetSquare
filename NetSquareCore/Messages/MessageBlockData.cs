using Newtonsoft.Json;
using System;

namespace NetSquare.Core
{
    public class MessageBlockData
    {
        public MessageBlockType Type;
        public byte[] data;

        public void SetBytes(byte[] val)
        {
            data = val;
            Type = MessageBlockType.ByteArray;
        }

        public void SetByte(byte val)
        {
            data = new byte[1] { val };
            Type = MessageBlockType.Byte;
        }

        public byte GetByte()
        {
            return data[0];
        }

        public void SetShort(short val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Short;
        }

        public short GetShort()
        {
            return BitConverter.ToInt16(data, 0);
        }

        public void SetInt(int val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Int;
        }

        public int GetInt()
        {
            return BitConverter.ToInt32(data, 0);
        }

        public void SetLong(long val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Long;
        }

        public ushort GetUShort()
        {
            return BitConverter.ToUInt16(data, 0);
        }

        public void SetUShort(ushort val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.UShort;
        }

        public uint GetUint()
        {
            return BitConverter.ToUInt32(data, 0);
        }

        public void SetUInt(uint val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.UInt;
        }

        public ulong GetUlong()
        {
            return BitConverter.ToUInt64(data, 0);
        }

        public void SetULong(ulong val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.ULong;
        }

        public float GetFloat()
        {
            return BitConverter.ToSingle(data, 0);
        }

        public void SetFloat(float val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Float;
        }

        public bool GetBool()
        {
            return BitConverter.ToBoolean(data, 0);
        }

        public void SetBool(bool val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Bool;
        }

        public char Getchar()
        {
            return BitConverter.ToChar(data, 0);
        }

        public void SetChar(char val)
        {
            data = BitConverter.GetBytes(val);
            Type = MessageBlockType.Char;
        }

        public void SetString(string val)
        {
            data = System.Text.Encoding.Default.GetBytes(val);
            Type = MessageBlockType.String;
        }

        /// <summary>
        /// Must be called only once by message and alwayse at the end
        /// </summary>
        /// <param name="val">serializable object (will be json and byte[] by Default))</param>
        public void SetObject(object val)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                Messages.NetSquareMessageSerialization.Serializer.Serialize(val, stream);
                data = stream.ToArray();
            }
            //data = System.Text.Encoding.Default.GetBytes(JsonConvert.SerializeObject(val));
            Type = MessageBlockType.Custom;
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
        ULong = 11,
        ByteArray = 12
    }
}