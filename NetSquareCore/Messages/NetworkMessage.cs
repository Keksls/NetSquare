using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace NetSquare.Core
{
    public class NetworkMessage
    {
        public uint ClientID { get; set; }
        public int TypeID { get; set; }
        public ushort HeadID { get; set; }
        public List<MessageBlockData> Blocks { get; set; }
        public ConnectedClient Client { get; set; }
        public byte[] Header { get; private set; }
        public byte[] Data { get; private set; }
        public ushort Length { get; set; }
        private int currentReadingIndex = 0;

        #region Type and Reply
        /// <summary>
        /// this id will be keeped and pass throw new message by the server. Used for reply callback
        /// </summary>
        /// <param name="id"></param>
        public void ReplyTo(int id)
        {
            SetType(id + 10);
        }

        /// <summary>
        /// Set the type of this message. 
        /// 0 => simple message send to server
        /// 1 => message that will be broadcasted to every other clients on my lobby
        /// 2 => message that will be packed and syncronized on every clients on my lobby
        /// 10 or + => message send to server, client wait for response. Response ID will be that ID
        /// </summary>
        /// <param name="typeID"></param>
        public void SetType(int typeID)
        {
            TypeID = typeID;
        }

        /// <summary>
        /// Set the type of this message. 
        /// 0 => simple message send to server
        /// 1 => message that will be broadcasted to avery other clients on my lobby
        /// 
        /// 10 or + => message send to server, client wait for response. Response ID will be that ID
        /// </summary>
        /// <param name="typeID"></param>
        public void SetType(MessageType type)
        {
            TypeID = (int)type;
        }
        #endregion

        #region Constructors
        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        /// <param name="clientID">ID of the client that send message</param>
        public NetworkMessage(ushort headID, uint clientID)
        {
            HeadID = headID;
            ClientID = clientID;
            Length = 0;
            TypeID = 0;
            Blocks = new List<MessageBlockData>();
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        /// <param name="clientID">ID of the client that send message</param>
        public NetworkMessage(Enum headID, uint clientID)
        {
            HeadID = Convert.ToUInt16(headID);
            ClientID = clientID;
            Length = 0;
            TypeID = 0;
            Blocks = new List<MessageBlockData>();
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        public NetworkMessage()
        {
            HeadID = 0;
            ClientID = 0;
            Length = 0;
            TypeID = 0;
            Blocks = new List<MessageBlockData>();
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        public NetworkMessage(ushort headID)
        {
            HeadID = headID;
            ClientID = 0;
            Length = 0;
            TypeID = 0;
            Blocks = new List<MessageBlockData>();
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        public NetworkMessage(Enum headEnum)
        {
            HeadID = Convert.ToUInt16(headEnum);
            ClientID = 0;
            Length = 0;
            TypeID = 0;
            Blocks = new List<MessageBlockData>();
        }
        #endregion

        #region Head and Data
        public void SetHead(byte[] header)
        {
            Header = header;
            ReadHead();
        }

        public void SetData(byte[] data)
        {
            data = ProtocoleManager.Decompress(data);
            data = ProtocoleManager.Decrypt(data);
            Data = data;
            RestartRead();
        }

        internal void ReadHead()
        {
            Length = BitConverter.ToUInt16(Header, 0);
            ClientID = BitConverter.ToUInt32(Header, 2);
            HeadID = BitConverter.ToUInt16(Header, 6);
            TypeID = BitConverter.ToInt32(Header, 8);
        }

        /// <summary>
        /// Get message header with following : size, clientID, HeadID, TypeID
        /// </summary>
        /// <returns></returns>
        internal byte[] GetHead()
        {
            Header = new byte[12];
            // write message Size
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)(Data.Length + 12)), 0, Header, 0, 2);
            // write Client ID
            Buffer.BlockCopy(BitConverter.GetBytes(ClientID), 0, Header, 2, 4);
            // write Head Action
            Buffer.BlockCopy(BitConverter.GetBytes(HeadID), 0, Header, 6, 2);
            // write Client ID
            Buffer.BlockCopy(BitConverter.GetBytes(TypeID), 0, Header, 8, 4);
            return Header;
        }
        #endregion

        #region Reading
        /// <summary>
        /// reset the reading index. use it if you already have read this message and you want to read it again.
        /// </summary>
        public void RestartRead()
        {
            currentReadingIndex = 0;
        }

        /// <summary>
        /// Set the reading index. use it if you want to go a specific index of the data array.
        /// </summary>
        public void SetReadingIndex(int readIndex)
        {
            currentReadingIndex = readIndex;
        }

        /// <summary>
        /// Move data cursor to the right by readingSize
        /// </summary>
        /// <param name="readingSize">number of byte we will 'jump' before next reading</param>
        public void DummyRead(uint readingSize)
        {
            currentReadingIndex += (int)readingSize;
        }

        /// <summary>
        /// Move data cursor to the left by readingSize
        /// </summary>
        /// <param name="readingSize">number of byte we will 'jump back' before next reading</param>
        public void DummyUnRead(uint readingSize)
        {
            currentReadingIndex -= (int)readingSize;
        }

        public bool CanGetByte()
        {
            return currentReadingIndex + 1 <= Data.Length;
        }

        public byte GetByte()
        {
            currentReadingIndex++;
            return Data[currentReadingIndex - 1];
        }

        public void Get(ref byte val)
        {
            val = Data[currentReadingIndex];
            currentReadingIndex++;
        }

        public bool CanGetShort()
        {
            return currentReadingIndex + 2 <= Data.Length;
        }

        public short GetShort()
        {
            currentReadingIndex += 2;
            return BitConverter.ToInt16(Data, currentReadingIndex - 2);
        }

        public void Get(ref short val)
        {
            val = BitConverter.ToInt16(Data, currentReadingIndex);
            currentReadingIndex += 2;
        }

        public bool CanGetint()
        {
            return currentReadingIndex + 4 <= Data.Length;
        }

        public int GetInt()
        {
            currentReadingIndex += 4;
            return BitConverter.ToInt32(Data, currentReadingIndex - 4);
        }

        public void Get(ref int val)
        {
            val = BitConverter.ToInt32(Data, currentReadingIndex);
            currentReadingIndex += 4;
        }

        public bool CanGetLong()
        {
            return currentReadingIndex + 8 <= Data.Length;
        }

        public long GetLong()
        {
            currentReadingIndex += 8;
            return BitConverter.ToInt64(Data, currentReadingIndex - 8);
        }

        public void Get(ref long val)
        {
            val = BitConverter.ToInt64(Data, currentReadingIndex);
            currentReadingIndex += 8;
        }

        public bool CanGetUShort()
        {
            return currentReadingIndex + 2 <= Data.Length;
        }

        public ushort GetUShort()
        {
            currentReadingIndex += 2;
            return BitConverter.ToUInt16(Data, currentReadingIndex - 2);
        }

        public void Get(ref ushort val)
        {
            val = BitConverter.ToUInt16(Data, currentReadingIndex);
            currentReadingIndex += 2;
        }

        public bool CanGetUInt()
        {
            return currentReadingIndex + 4 <= Data.Length;
        }

        public uint GetUInt()
        {
            currentReadingIndex += 4;
            return BitConverter.ToUInt32(Data, currentReadingIndex - 4);
        }

        public void Get(ref uint val)
        {
            val = BitConverter.ToUInt32(Data, currentReadingIndex);
            currentReadingIndex += 4;
        }

        public bool CanGetULong()
        {
            return currentReadingIndex + 8 <= Data.Length;
        }

        public ulong GetULong()
        {
            currentReadingIndex += 8;
            return BitConverter.ToUInt64(Data, currentReadingIndex - 8);
        }

        public void Get(ref ulong val)
        {
            val = BitConverter.ToUInt64(Data, currentReadingIndex);
            currentReadingIndex += 8;
        }

        public bool CanGetFloat()
        {
            return currentReadingIndex + 4 <= Data.Length;
        }

        public float GetFloat()
        {
            currentReadingIndex += 4;
            return BitConverter.ToSingle(Data, currentReadingIndex - 4);
        }

        public void Get(ref float val)
        {
            val = BitConverter.ToSingle(Data, currentReadingIndex);
            currentReadingIndex += 4;
        }

        public bool CanGetBool()
        {
            return currentReadingIndex + 1 <= Data.Length;
        }

        public bool GetBool()
        {
            currentReadingIndex++;
            return BitConverter.ToBoolean(Data, currentReadingIndex - 1);
        }

        public void Get(ref bool val)
        {
            val = BitConverter.ToBoolean(Data, currentReadingIndex);
            currentReadingIndex += 1;
        }

        public bool CanGetChar()
        {
            return currentReadingIndex + 1 <= Data.Length;
        }

        public char GetChar()
        {
            currentReadingIndex += 2;
            return BitConverter.ToChar(Data, currentReadingIndex - 2);
        }

        public void Get(ref char val)
        {
            val = BitConverter.ToChar(Data, currentReadingIndex);
            currentReadingIndex += 2;
        }

        public bool CanGetString()
        {
            return currentReadingIndex + 8 <= Data.Length;
        }

        public string GetString()
        {
            ushort size = GetUShort();
            string val = System.Text.Encoding.Default.GetString(Data, currentReadingIndex, size);
            currentReadingIndex += size;
            return val;
        }

        public void Get(ref string val)
        {
            ushort size = GetUShort();
            val = System.Text.Encoding.Default.GetString(Data, currentReadingIndex, size);
            currentReadingIndex += size;
        }

        public bool CanGetObject()
        {
            return currentReadingIndex + 8 <= Data.Length;
        }

        /// <summary>
        /// Get a complex or custom object. Will be Json serialized
        /// </summary>
        public T GetObject<T>()
        {
            ushort size = 0;
            Get(ref size);
            using (var stream = new MemoryStream(Data, currentReadingIndex, size))
            {
                currentReadingIndex += size;
                return Messages.NetSquareMessageSerialization.Serializer.Deserialize<T>(stream);
            }
           // return JsonConvert.DeserializeObject<T>(GetString());
        }
        #endregion

        #region Set Data
        public NetworkMessage Set(byte[] val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetBytes(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(byte val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetByte(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(short val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetShort(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(int val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetInt(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(long val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetLong(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(ushort val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetUShort(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(uint val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetUInt(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(ulong val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetULong(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(float val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetFloat(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(bool val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetBool(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(char val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetChar(val);
            Blocks.Add(block);
            return this;
        }

        public NetworkMessage Set(string val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetString(val);
            Blocks.Add(block);
            return this;
        }

        /// <summary>
        /// Must be called only once by message and alwayse at the end
        /// </summary>
        /// <param name="val">serializable object (will be json and byte[] by Default))</param>
        public unsafe NetworkMessage SetObject<T>(T val)
        {
            MessageBlockData block = new MessageBlockData();
            block.SetObject(val);
            Blocks.Add(block);
            return this;
        }
        #endregion

        /// <summary>
        /// Tram Definition :
        ///     - FullMessageSize : Int32   4 bytes
        ///     - ClientID :        Int32   4 bytes
        ///     - HeadAction :      Int16   2 bytes
        ///     - TypeID :         Int32   4 bytes
        ///     - Data :            var     FullMessageSize - 14 bytes
        ///     
        /// Data Definition :
        ///     - Primitive type, size by type => Function Enum to Size
        ///     - Custom or String : HEAD : size Int32 4 bytes  |   BODY : deserialize by type
        /// </summary>
        /// <returns></returns>
        public byte[] Serialize()
        {
            // Reserialize a message, just add size
            if (Blocks == null && Data != null)
            {
                Data = ProtocoleManager.Encrypt(Data);
                Data = ProtocoleManager.Compress(Data);
                Header = GetHead();
                return GetFullMessageData();
            }

            // process Size
            int dataSize = 0;
            foreach (MessageBlockData block in Blocks)
            {
                switch (block.Type)
                {
                    // primitiv types, simply copy data block to message buffer
                    case MessageBlockType.Byte:
                    case MessageBlockType.Short:
                    case MessageBlockType.Int:
                    case MessageBlockType.Long:
                    case MessageBlockType.Float:
                    case MessageBlockType.Bool:
                    case MessageBlockType.Char:
                    case MessageBlockType.UShort:
                    case MessageBlockType.UInt:
                    case MessageBlockType.ULong:
                    case MessageBlockType.ByteArray:
                        dataSize += block.data.Length;
                        break;

                    // primitiv types, simply copy data block to message buffer
                    default:
                    case MessageBlockType.String:
                    case MessageBlockType.Custom:
                        dataSize += block.data.Length + 2;
                        break;
                }
            }
            // create full empty array
            Data = new byte[dataSize]; // head size

            // Write Blocks
            int currentIndex = 0;
            foreach (MessageBlockData block in Blocks)
            {
                // using switch for better perfs
                switch (block.Type)
                {
                    // primitiv types, simply copy data block to message buffer
                    case MessageBlockType.Byte:
                    case MessageBlockType.Short:
                    case MessageBlockType.Int:
                    case MessageBlockType.Long:
                    case MessageBlockType.Float:
                    case MessageBlockType.Bool:
                    case MessageBlockType.Char:
                    case MessageBlockType.UShort:
                    case MessageBlockType.UInt:
                    case MessageBlockType.ULong:
                    case MessageBlockType.ByteArray:
                        Buffer.BlockCopy(block.data, 0, Data, currentIndex, block.data.Length);
                        currentIndex += block.data.Length;
                        break;

                    // primitiv types, simply copy data block to message buffer
                    default:
                    case MessageBlockType.String:
                    case MessageBlockType.Custom:
                        // Write data size
                        byte[] sizeData = BitConverter.GetBytes((ushort)block.data.Length);
                        Buffer.BlockCopy(sizeData, 0, Data, currentIndex, 2);
                        Buffer.BlockCopy(block.data, 0, Data, currentIndex + 2, block.data.Length);
                        currentIndex += block.data.Length + 2;
                        break;
                }
            }

            Data = ProtocoleManager.Encrypt(Data);
            Data = ProtocoleManager.Compress(Data);

            // Get message Header
            Header = GetHead();
            return GetFullMessageData();
        }

        public unsafe byte[] ConcatArrays(byte[] array1, byte[] array2)
        {
            byte[] concated = new byte[array1.Length + array2.Length];
            Buffer.BlockCopy(array1, 0, concated, 0, array1.Length);
            Buffer.BlockCopy(array2, 0, concated, array1.Length, array2.Length);
            return concated;
        }

        internal byte[] GetFullMessageData()
        {
            byte[] final = ConcatArrays(Header, Data);
            Length = (ushort)final.Length;
            return final;
        }
    }
}