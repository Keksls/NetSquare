using System;
using System.Collections.Generic;
using System.IO;

namespace NetSquare.Core
{
    public class NetworkMessage
    {
        public uint ClientID { get; set; }
        public uint TypeID { get; set; }
        public ushort HeadID { get; set; }
        private List<byte[]> blocks = new List<byte[]>();
        public bool HasBlock { get { return blocks?.Count > 0; } }
        private int blocksSize = 0;
        public ConnectedClient Client { get; set; }
        public byte[] Data { get; private set; }
        public ushort Length { get; set; }
        public bool Packed { get; private set; }
        public int currentReadingIndex { get; set; } = 10;

        #region Type and Reply
        /// <summary>
        /// this id will be keeped and pass throw new message by the server. Used for reply callback
        /// </summary>
        /// <param name="id"></param>
        public void ReplyTo(uint id)
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
        public void SetType(uint typeID)
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
            TypeID = (uint)type;
        }

        /// <summary>
        /// Set this message as a synchronization message
        /// </summary>
        /// <returns>itself</returns>
        public NetworkMessage SetAsSynchronizationMessage()
        {
            SetType(MessageType.SynchronizeMessageCurrentWorld);
            return this;
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
        }
        #endregion

        #region Head and Data
        /// <summary>
        /// Set data, decrypt and decompress it, read head, message is ready for reading
        /// </summary>
        /// <param name="data">data to set</param>
        public void SetData(byte[] data)
        {
            Data = data;
            DecryptDecompressData();
            ReadHead();
            RestartRead();
        }

        /// <summary>
        /// Set data, encrypt and compress it, write head, message is ready to send
        /// </summary>
        /// <param name="data">data to set</param>
        public void SetSerializedData(byte[] data)
        {
            Data = data;
            WriteHead();
            EncryptCompressData();
            RestartRead();
        }

        /// <summary>
        /// Just set Data, no decryption / decompression, no head reading
        /// </summary>
        /// <param name="data">data to set</param>
        public void SetDataUnsafe(byte[] data)
        {
            Data = data;
            RestartRead();
        }

        /// <summary>
        /// Get the mesage data without head
        /// </summary>
        /// <returns></returns>
        public byte[] GetBody()
        {
            byte[] body = new byte[Data.Length - 10];
            Buffer.BlockCopy(Data, 10, body, 0, body.Length);
            return body;
        }

        internal void EncryptCompressData()
        {
            if (ProtocoleManager.NoCompressorOrEncryptor)
                return;
            byte[] encrypted = ProtocoleManager.Encrypt(Data);
            encrypted = ProtocoleManager.Compress(encrypted);
            Data = new byte[encrypted.Length + 2];
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)Data.Length), 0, Data, 0, 2);
            Buffer.BlockCopy(encrypted, 0, Data, 2, encrypted.Length);
        }

        internal void DecryptDecompressData()
        {
            if (ProtocoleManager.NoCompressorOrEncryptor)
                return;
            byte[] encrypted = new byte[Data.Length - 2];
            Buffer.BlockCopy(Data, 2, encrypted, 0, encrypted.Length);
            encrypted = ProtocoleManager.Decompress(encrypted);
            Data = ProtocoleManager.Decrypt(encrypted);
        }

        internal void ReadHead()
        {
            Length = BitConverter.ToUInt16(Data, 0);
            ClientID = UInt24.GetUInt(Data, 2);
            HeadID = BitConverter.ToUInt16(Data, 5);
            TypeID = UInt24.GetUInt(Data, 7);
        }

        internal void WriteHead()
        {
            // write message Size
            Data[0] = (byte)(ushort)Data.Length;
            Data[1] = (byte)((ushort)Data.Length >> 8);
            // write Client ID
            Data[2] = (byte)((ClientID) & 0xFF);
            Data[3] = (byte)((ClientID >> 8) & 0xFF);
            Data[4] = (byte)((ClientID >> 16) & 0xFF);
            // write Head Action
            Data[5] = (byte)HeadID;
            Data[6] = (byte)(HeadID >> 8);
            // write Type ID
            Data[7] = (byte)((TypeID) & 0xFF);
            Data[8] = (byte)((TypeID >> 8) & 0xFF);
            Data[9] = (byte)((TypeID >> 16) & 0xFF);
        }

        public void SetHeadIDIntoData(ushort headID)
        {
            // write Head Action
            Data[5] = (byte)headID;
            Data[6] = (byte)(headID >> 8);
        }
        #endregion

        #region Datagram
        public bool SafeSetDatagram(byte[] data)
        {
            try
            {
                // check if at least we got full head
                if (data.Length < 10)
                    return false;
                SetDataUnsafe(data);
                DecryptDecompressData();
                // read head
                ReadHead();
                RestartRead();
                // check if lenght == to datagram lenght
                if (Length != Data.Length)
                    return false;
                // set data from datagram
                //SetData(data);
                return true;
            }
            catch { return false; }
        }
        #endregion

        #region Reading
        /// <summary>
        /// reset the reading index. use it if you already have read this message and you want to read it again.
        /// </summary>
        public void RestartRead()
        {
            currentReadingIndex = 10;
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
        public void DummyRead(int readingSize)
        {
            currentReadingIndex += readingSize;
        }

        /// <summary>
        /// Move data cursor to the left by readingSize
        /// </summary>
        /// <param name="readingSize">number of byte we will 'jump back' before next reading</param>
        public void DummyUnRead(int readingSize)
        {
            currentReadingIndex -= readingSize;
        }

        public bool CanReadFor(int size)
        {
            return currentReadingIndex + size < Data.Length;
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

        public bool CanGetUInt24()
        {
            return currentReadingIndex + 3 <= Data.Length;
        }

        public bool CanGetNextBlock()
        {
            if (CanGetUInt24())
            {
                UInt24 blockSize = new UInt24(Data, currentReadingIndex);
                return Data.Length - currentReadingIndex > blockSize.UInt32;
            }
            return false;
        }

        public bool NextBlock()
        {
            if (CanGetUInt24())
            {
                UInt24 blockSize = new UInt24(Data, currentReadingIndex);
                bool canGetNextBlock = Data.Length - currentReadingIndex > blockSize.UInt32;
                if (canGetNextBlock)
                    currentReadingIndex += 3;
                return canGetNextBlock;
            }
            return false;
        }

        public bool IsBlockMessage()
        {
            if (CanGetUInt24())
            {
                int cri = currentReadingIndex;
                RestartRead();
                UInt24 blockSize = new UInt24(Data, currentReadingIndex);
                bool canGetNextBlock = Data.Length - currentReadingIndex > blockSize.UInt32;
                if (canGetNextBlock)
                    currentReadingIndex += 3;
                currentReadingIndex = cri;
                return canGetNextBlock;
            }
            return false;
        }

        public void Get(ref UInt24 val)
        {
            val.b0 = Data[++currentReadingIndex];
            val.b1 = Data[++currentReadingIndex];
            val.b2 = Data[++currentReadingIndex];
        }

        public UInt24 GetUInt24()
        {
            currentReadingIndex += 3;
            return new UInt24(Data, currentReadingIndex - 3);
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
            return currentReadingIndex + BitConverter.ToUInt16(Data, currentReadingIndex) + 2 <= Data.Length;
        }

        public string GetString()
        {
            ushort size = GetUShort();
            string val = System.Text.Encoding.UTF8.GetString(Data, currentReadingIndex, size);
            currentReadingIndex += size;
            return val;
        }

        public string GetStringUTF32()
        {
            ushort size = GetUShort();
            string val = System.Text.Encoding.UTF32.GetString(Data, currentReadingIndex, size);
            currentReadingIndex += size;
            return val;
        }

        public void Get(ref string val)
        {
            ushort size = GetUShort();
            val = System.Text.Encoding.UTF8.GetString(Data, currentReadingIndex, size);
            currentReadingIndex += size;
        }

        public bool CanGetObject()
        {
            return currentReadingIndex + BitConverter.ToUInt16(Data, currentReadingIndex) + 2 <= Data.Length;
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
                return Utf8Json.JsonSerializer.Deserialize<T>(stream, Utf8Json.Resolvers.StandardResolver.AllowPrivateCamelCase);
            }
        }
        public bool CanGetArray()
        {
            ushort lenght = GetUShort();
            currentReadingIndex -= 2;
            return currentReadingIndex + lenght <= Data.Length;
        }

        public int[] GetIntArray()
        {
            ushort lenght = GetUShort();
            int[] data = new int[lenght / 4];
            Buffer.BlockCopy(Data, currentReadingIndex, data, 0, lenght);
            currentReadingIndex += lenght;
            return data;
        }

        public byte[] GetByteArray()
        {
            ushort lenght = GetUShort();
            byte[] data = new byte[lenght];
            Buffer.BlockCopy(Data, currentReadingIndex, data, 0, lenght);
            currentReadingIndex += lenght;
            return data;
        }

        public uint[] GetUIntArray()
        {
            ushort lenght = GetUShort();
            uint[] data = new uint[lenght / 4];
            Buffer.BlockCopy(Data, currentReadingIndex, data, 0, lenght);
            currentReadingIndex += lenght;
            return data;
        }

        public long[] GetLongArray()
        {
            ushort lenght = GetUShort();
            long[] data = new long[lenght / 8];
            Buffer.BlockCopy(Data, currentReadingIndex, data, 0, lenght);
            currentReadingIndex += lenght;
            return data;
        }

        public ulong[] GetULongArray()
        {
            ushort lenght = GetUShort();
            ulong[] data = new ulong[lenght / 8];
            Buffer.BlockCopy(Data, currentReadingIndex, data, 0, lenght);
            currentReadingIndex += lenght;
            return data;
        }

        public short[] GetShortArray()
        {
            ushort lenght = GetUShort();
            short[] data = new short[lenght / 2];
            Buffer.BlockCopy(Data, currentReadingIndex, data, 0, lenght);
            currentReadingIndex += lenght;
            return data;
        }

        public ushort[] GetUShortArray()
        {
            ushort lenght = GetUShort();
            ushort[] data = new ushort[lenght / 2];
            Buffer.BlockCopy(Data, currentReadingIndex, data, 0, lenght);
            currentReadingIndex += lenght;
            return data;
        }

        public float[] GetFloatArray()
        {
            ushort lenght = GetUShort();
            float[] data = new float[lenght / 4];
            Buffer.BlockCopy(Data, currentReadingIndex, data, 0, lenght);
            currentReadingIndex += lenght;
            return data;
        }

        public bool[] GetBoolArray()
        {
            int lenght = (int)GetUShort();
            bool[] data = new bool[lenght];
            for (int i = 0; i < data.Length; i++, currentReadingIndex++)
                data[i] = Data[currentReadingIndex] == (byte)1 ? true : false;
            return data;
        }
        #endregion

        #region Set Data
        public void AlreadySerialized()
        {
            Packed = true;
        }

        public NetworkMessage Set(byte[] val)
        {
            blocks.Add(BitConverter.GetBytes((ushort)val.Length));
            blocks.Add(val);
            blocksSize += val.Length + 2;
            return this;
        }

        public NetworkMessage Set(UInt24 val)
        {
            blocksSize += 3;
            blocks.Add(new byte[] { val.b0, val.b1, val.b2 });
            return this;
        }

        public NetworkMessage Set(byte val)
        {
            blocks.Add(new byte[] { val });
            blocksSize++;
            return this;
        }

        public NetworkMessage Set(short val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            blocksSize += 2;
            return this;
        }

        public NetworkMessage Set(int val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            blocksSize += 4;
            return this;
        }

        public NetworkMessage Set(long val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            blocksSize += 8;
            return this;
        }

        public NetworkMessage Set(ushort val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            blocksSize += 2;
            return this;
        }

        public NetworkMessage Set(uint val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            blocksSize += 4;
            return this;
        }

        public NetworkMessage Set(ulong val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            return this;
        }

        public NetworkMessage Set(float val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            blocksSize += 4;
            return this;
        }

        public NetworkMessage Set(bool val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            blocksSize++;
            return this;
        }

        public NetworkMessage Set(char val)
        {
            blocks.Add(BitConverter.GetBytes(val));
            blocksSize += 4;
            return this;
        }

        public NetworkMessage Set(string val)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(val);
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage SetUTF32(string val)
        {
            byte[] data = System.Text.Encoding.UTF32.GetBytes(val);
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage Set(int[] val)
        {
            byte[] data = new byte[val.Length * 4];
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            Buffer.BlockCopy(val, 0, data, 0, data.Length);
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage Set(uint[] val)
        {
            byte[] data = new byte[val.Length * 4];
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            Buffer.BlockCopy(val, 0, data, 0, data.Length);
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage Set(long[] val)
        {
            byte[] data = new byte[val.Length * 8];
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            Buffer.BlockCopy(val, 0, data, 0, data.Length);
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage Set(ulong[] val)
        {
            byte[] data = new byte[val.Length * 8];
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            Buffer.BlockCopy(val, 0, data, 0, data.Length);
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage Set(short[] val)
        {
            byte[] data = new byte[val.Length * 2];
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            Buffer.BlockCopy(val, 0, data, 0, data.Length);
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage Set(ushort[] val)
        {
            byte[] data = new byte[val.Length * 2];
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            Buffer.BlockCopy(val, 0, data, 0, data.Length);
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage Set(float[] val)
        {
            byte[] data = new byte[val.Length * 4];
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            Buffer.BlockCopy(val, 0, data, 0, data.Length);
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        public NetworkMessage Set(bool[] val)
        {
            byte[] data = new byte[val.Length];
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            for (int i = 0; i < val.Length; i++)
                data[i] = val[i] ? (byte)1 : (byte)0;
            blocks.Add(data);
            blocksSize += data.Length + 2;
            return this;
        }

        /// <summary>
        /// Must be called only once by message and alwayse at the end
        /// </summary>
        /// <param name="val">serializable object (will be json and byte[] by Default))</param>
        public NetworkMessage SetObject<T>(T val)
        {
            byte[] data = Utf8Json.JsonSerializer.Serialize<T>(val, Utf8Json.Resolvers.StandardResolver.AllowPrivateCamelCase);
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
            blocks.Add(data);
            blocksSize += data.Length + 2;
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
        public byte[] Serialize(bool ignoreCompression = false)
        {
            if (Packed)
                return Data;

            // create full empty array
            Data = new byte[blocksSize + 10];
            // Write Blocks
            int currentIndex = 10;
            for (int i = 0; i < blocks.Count; i++)
            {
                Buffer.BlockCopy(blocks[i], 0, Data, currentIndex, blocks[i].Length);
                currentIndex += blocks[i].Length;
            }
            WriteHead();
            if (!ProtocoleManager.NoCompressorOrEncryptor && !ignoreCompression)
                EncryptCompressData();
            return Data;
        }

        public NetworkMessage Pack(IEnumerable<NetworkMessage> messages, bool alreadySerialized = false)
        {
            int lenght = 10;
            int nb = 0;
            foreach (NetworkMessage message in messages)
            {
                if (!alreadySerialized)
                    message.Serialize(true);
                lenght += message.Data.Length - 4; // blockSize (3 bits) + clientID (3 bits)  - headSize (10 bits)
                nb++;
            }

            if (nb == 0)
                return this;

            // create full empty array
            Data = new byte[blocksSize + 10 + lenght];
            // Write Blocks
            int index = 10;
            for (int i = 0; i < blocks.Count; i++)
            {
                Buffer.BlockCopy(blocks[i], 0, Data, index, blocks[i].Length);
                index += blocks[i].Length;
            }

            foreach (NetworkMessage message in messages)
            {
                // Write block Lenght
                UInt24 blockSize = new UInt24((uint)(message.Data.Length - 7));
                Data[index++] = blockSize.b0;
                Data[index++] = blockSize.b1;
                Data[index++] = blockSize.b2;

                // Write client ID
                Data[index++] = (byte)((message.ClientID) & 0xFF);
                Data[index++] = (byte)((message.ClientID >> 8) & 0xFF);
                Data[index++] = (byte)((message.ClientID >> 16) & 0xFF);

                Buffer.BlockCopy(message.Data, 10, Data, index, message.Data.Length - 10);
                index += message.Data.Length - 10;
            }

            WriteHead();
            if (!ProtocoleManager.NoCompressorOrEncryptor)
                EncryptCompressData();

            Packed = true;
            return this;
        }

        public List<NetworkMessage> Unpack()
        {
            List<NetworkMessage> messages = new List<NetworkMessage>();

            // reading each packed blocks
            while (CanGetNextBlock())
            {
                // get block size
                int size = (int)GetUInt24().UInt32;
                if (size == 0)
                    break;
                // create message
                NetworkMessage message = new NetworkMessage(HeadID, GetUInt24().UInt32);
                size -= 3;
                message.SetType(TypeID);
                // copy block data into message
                message.Data = new byte[size + 10];
                Buffer.BlockCopy(Data, currentReadingIndex, message.Data, 10, size);
                currentReadingIndex += size;
                // add message to list
                message.RestartRead();
                messages.Add(message);
            }

            return messages;
        }

        public List<NetworkMessage> UnpackWithoutHead()
        {
            List<NetworkMessage> messages = new List<NetworkMessage>();

            // reading each packed blocks
            while (CanGetNextBlock())
            {
                // get block size
                int size = (int)GetUInt24().UInt32;
                // create message
                NetworkMessage message = new NetworkMessage(HeadID, GetUInt24().UInt32);
                size -= 3;
                message.SetType(TypeID);
                // copy block data into message
                message.Data = new byte[size];
                Buffer.BlockCopy(Data, currentReadingIndex, message.Data, 0, size);
                currentReadingIndex += size;
                // add message to list
                message.SetReadingIndex(0);
                messages.Add(message);
            }

            return messages;
        }
    }
}