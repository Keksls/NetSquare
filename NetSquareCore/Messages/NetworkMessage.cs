using NetSquare.Core.Messages;
using System;
using System.Collections.Generic;
using System.IO;

namespace NetSquare.Core
{
    public class NetworkMessage
    {
        public UInt24 ClientID { get; set; }
        public UInt24 TypeID { get; set; }
        public ushort HeadID { get; set; }
        private List<byte[]> blocks = new List<byte[]>();
        private int blocksSize = 0;
        public ConnectedClient Client { get; set; }
        public byte[] Data { get; private set; }
        public ushort Length { get; set; }
        public bool Packed { get; private set; }
        private int currentReadingIndex = 0;

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
            TypeID = new UInt24(typeID);
        }

        /// <summary>
        /// Set the type of this message. 
        /// 0 => simple message send to server
        /// 1 => message that will be broadcasted to every other clients on my lobby
        /// 2 => message that will be packed and syncronized on every clients on my lobby
        /// 10 or + => message send to server, client wait for response. Response ID will be that ID
        /// </summary>
        /// <param name="typeID"></param>
        public void SetType(UInt24 typeID)
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
            TypeID = new UInt24((uint)type);
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
            ClientID = new UInt24(clientID);
            Length = 0;
            TypeID = new UInt24(0);
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        /// <param name="clientID">ID of the client that send message</param>
        public NetworkMessage(ushort headID, UInt24 clientID)
        {
            HeadID = headID;
            ClientID = clientID;
            Length = 0;
            TypeID = new UInt24(0);
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        /// <param name="clientID">ID of the client that send message</param>
        public NetworkMessage(Enum headID, uint clientID)
        {
            HeadID = Convert.ToUInt16(headID);
            ClientID = new UInt24(clientID);
            Length = 0;
            TypeID = new UInt24(0);
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        /// <param name="clientID">ID of the client that send message</param>
        public NetworkMessage(Enum headID, UInt24 clientID)
        {
            HeadID = Convert.ToUInt16(headID);
            ClientID = clientID;
            Length = 0;
            TypeID = new UInt24(0);
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        public NetworkMessage()
        {
            HeadID = 0;
            ClientID = new UInt24(0);
            Length = 0;
            TypeID = new UInt24(0);
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        public NetworkMessage(ushort headID)
        {
            HeadID = headID;
            ClientID = new UInt24(0);
            Length = 0;
            TypeID = new UInt24(0);
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        public NetworkMessage(Enum headEnum)
        {
            HeadID = Convert.ToUInt16(headEnum);
            ClientID = new UInt24(0);
            Length = 0;
            TypeID = new UInt24(0);
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
            ClientID = new UInt24(Data, 2);
            HeadID = BitConverter.ToUInt16(Data, 5);
            TypeID = new UInt24(Data, 7);
        }

        internal void WriteHead()
        {
            // write message Size
            Data[0] = (byte)(ushort)Data.Length;
            Data[1] = (byte)((ushort)Data.Length >> 8);
            // write Client ID
            Data[2] = ClientID.b0;
            Data[3] = ClientID.b1;
            Data[4] = ClientID.b2;
            // write Head Action
            Data[5] = (byte)HeadID;
            Data[6] = (byte)(HeadID >> 8);
            // write Type ID
            Data[7] = TypeID.b0;
            Data[8] = TypeID.b1;
            Data[9] = TypeID.b2;
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

        public bool CanGetUInt24()
        {
            return currentReadingIndex + 3 <= Data.Length;
        }

        public bool CanGetNextBlock()
        {
            if (CanGetUInt24())
            {
                UInt24 blockSize = GetUInt24();
                currentReadingIndex -= 3;
                return Data.Length - currentReadingIndex > blockSize.UInt32;
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
                return NetSquareMessageSerialization.Serializer.Deserialize<T>(stream);
            }
            // return JsonConvert.DeserializeObject<T>(GetString());
        }
        #endregion

        #region Set Data
        public NetworkMessage Set(byte[] val)
        {
            blocks.Add(val);
            blocksSize += val.Length;
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
            byte[] data = System.Text.Encoding.Default.GetBytes(val);
            blocks.Add(BitConverter.GetBytes((ushort)data.Length));
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
            using (var stream = new MemoryStream())
            {
                NetSquareMessageSerialization.Serializer.Serialize(val, stream);
                byte[] data = stream.ToArray();
                blocks.Add(BitConverter.GetBytes((ushort)data.Length));
                blocks.Add(data);
                blocksSize += data.Length + 2;
                return this;
            }
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
            Packed = true;
            int lenght = 10;
            foreach (NetworkMessage message in messages)
            {
                if (!alreadySerialized)
                    message.Serialize(true);
                lenght += message.Data.Length - 4; // blockSize (3 bits) + clientID (3 bits)  - headSize (10 bits)
            }

            Data = new byte[lenght];
            int index = 10;

            foreach (NetworkMessage message in messages)
            {
                // Write block Lenght
                UInt24 blockSize = new UInt24((uint)(message.Data.Length - 7));
                Data[index] = blockSize.b0;
                index++;
                Data[index] = blockSize.b1;
                index++;
                Data[index] = blockSize.b2;
                index++;

                // Write client ID
                Data[index] = message.ClientID.b0;
                index++;
                Data[index] = message.ClientID.b1;
                index++;
                Data[index] = message.ClientID.b2;
                index++;

                Buffer.BlockCopy(message.Data, 10, Data, index, message.Data.Length - 10);
                index += message.Data.Length - 10;
            }

            WriteHead();
            if (!ProtocoleManager.NoCompressorOrEncryptor)
                EncryptCompressData();

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
                // create message
                NetworkMessage message = new NetworkMessage(HeadID, GetUInt24());
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
    }
}