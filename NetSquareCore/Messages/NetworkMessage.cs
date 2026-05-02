using NetSquare.Core;
using System;
using System.Collections.Generic;

namespace NetSquare.Core
{
    /// <summary>
    /// Represents the network message component.
    /// </summary>
    public class NetworkMessage
    {
        /// <summary>
        /// Gets or sets the client id value.
        /// </summary>
        public uint ClientID { get; set; }
        /// <summary>
        /// Gets or sets the msg type value.
        /// </summary>
        public byte MsgType { get; set; }
        /// <summary>
        /// Gets or sets the reply id value.
        /// </summary>
        public uint ReplyID { get; set; }
        /// <summary>
        /// Gets or sets the head id value.
        /// </summary>
        public ushort HeadID { get; set; }
        /// <summary>
        /// Gets or sets the serializer value.
        /// </summary>
        public NetSquareSerializer Serializer { get; set; }
        /// <summary>
        /// Gets or sets the has write data value.
        /// </summary>
        public bool HasWriteData { get { return Serializer.HasWriteData; } }
        /// <summary>
        /// Gets or sets the client value.
        /// </summary>
        public ConnectedClient Client { get; set; }
        /// <summary>
        /// Gets or sets the is serialized value.
        /// </summary>
        public bool IsSerialized { get { return Serializer.SerializationMode == NetSquareSerializationMode.Read; } }
        /// <summary>
        /// Gets or sets the message length value.
        /// </summary>
        public int MessageLength { get; private set; }

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
            MessageLength = 0;
            MsgType = 0;
            ReplyID = 0;
            Serializer = new NetSquareSerializer();
            Serializer.StartWriting();
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
            MessageLength = 0;
            MsgType = 0;
            ReplyID = 0;
            Serializer = new NetSquareSerializer();
            Serializer.StartWriting();
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        public NetworkMessage()
        {
            HeadID = 0;
            ClientID = 0;
            MessageLength = 0;
            MsgType = 0;
            ReplyID = 0;
            Serializer = new NetSquareSerializer();
            Serializer.StartWriting();
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        public NetworkMessage(ushort headID)
        {
            HeadID = headID;
            ClientID = 0;
            MessageLength = 0;
            MsgType = 0;
            ReplyID = 0;
            Serializer = new NetSquareSerializer();
            Serializer.StartWriting();
        }

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        public NetworkMessage(Enum headEnum)
        {
            HeadID = Convert.ToUInt16(headEnum);
            ClientID = 0;
            MessageLength = 0;
            MsgType = 0;
            ReplyID = 0;
            Serializer = new NetSquareSerializer();
            Serializer.StartWriting();
        }

        /// <summary>
        /// New network message from data
        /// </summary>
        /// <param name="data"> data to set</param>
        public NetworkMessage(byte[] data)
        {
            if (data == null || data.Length < GetMinimumHeadSize())
                throw new Exception("Invalid network message buffer");
            Serializer = new NetSquareSerializer();
            DecryptDecompressData(ref data);
            if (data.Length < GetMinimumHeadSize())
                throw new Exception("Invalid network message buffer");
            ReadHead(data);
            if (MessageLength != data.Length)
                throw new Exception("Network message length mismatch");
            Serializer.StartReading(data);
            RestartRead();
        }
        #endregion

        #region Type and Reply
        /// <summary>
        /// this id will be keeped and pass throw new message by the server. Used for reply callback
        /// </summary>
        /// <param name="replyID">ID of the reply message</param>
        public void ReplyTo(uint replyID)
        {
            ReplyID = replyID;
            MsgType = (byte)NetSquareMessageType.Reply;
        }

        /// <summary>
        /// Set the type of this message. 
        /// Use this for custom message Type, overwise use MessageType enum
        /// </summary>
        /// <param name="typeID">ID of the type</param>
        public void SetType(byte typeID)
        {
            MsgType = (byte)(NetSquareMessageType.MAX + typeID);
        }

        /// <summary>
        /// Set the type of this message. 
        /// 0 => simple message send to server
        /// 1 => message that will be broadcasted to avery other clients on my lobby
        /// 
        /// 10 or + => message send to server, client wait for response. Response ID will be that ID
        /// </summary>
        /// <param name="typeID"></param>
        public void SetType(NetSquareMessageType type)
        {
            MsgType = (byte)type;
        }
        #endregion

        #region Head and Data 
        /// <summary>
        /// Check if we can read the next block but don't move the reading index
        /// </summary>
        /// <returns> if we can read the next block</returns>
        public bool CanGetNextBlock()
        {
            if (Serializer.CanGetUInt24())
            {
                UInt24 blockSize = new UInt24(Serializer.Buffer, Serializer.Position);
                return Serializer.CanReadFor(blockSize.UInt32);
            }
            return false;
        }

        /// <summary>
        /// get the next block but don't read it
        /// </summary>
        /// <returns></returns>
        public bool NextBlock()
        {
            if (Serializer.CanGetUInt24())
            {
                UInt24 blockSize = new UInt24(Serializer.Buffer, Serializer.Position);
                bool canGetNextBlock = Serializer.CanReadFor(blockSize.UInt32);
                if (canGetNextBlock)
                    Serializer.Position += 3;
                return canGetNextBlock;
            }
            return false;
        }

        /// <summary>
        /// reset the reading index. use it if you already have read this message and you want to read it again.
        /// </summary>
        public void RestartRead()
        {
            Serializer.Position = GetHeadSize();
        }

        /// <summary>
        /// Set the reading index. use it if you want to go a specific index of the data array.
        /// </summary>
        public void SetReadingIndex(int readIndex)
        {
            Serializer.Position = readIndex;
        }

        /// <summary>
        /// Get the size of the head of the message
        /// </summary>
        /// <returns>size of the head of the message</returns>
        public int GetHeadSize()
        {
            return MsgType == (byte)NetSquareMessageType.Reply ? 13 : 10;
        }

        /// <summary>
        /// Executes the get minimum head size operation.
        /// </summary>
        private static int GetMinimumHeadSize()
        {
            return 10;
        }

        /// <summary>
        /// Just set Data, no decryption / decompression, no head reading
        /// </summary>
        /// <param name="data">data to set</param>
        public void SetDataUnsafe(byte[] data)
        {
            Serializer.StartReading(data);
            RestartRead();
        }

        /// <summary>
        /// Get the body of the message
        /// </summary>
        /// <returns> body of the message </returns>
        public byte[] GetBody()
        {
            byte[] data = new byte[Serializer.Length - GetHeadSize()];
            Buffer.BlockCopy(Serializer.Buffer, GetHeadSize(), data, 0, data.Length);
            return data;
        }

        /// <summary>
        /// Set the body of the message
        /// </summary>
        /// <param name="data"> data to set</param>
        public void SetBody(byte[] data)
        {
            Serializer.StartWriting();
            Serializer.Set(data);
        }

        /// <summary>
        /// Encrypt and compress data
        /// </summary>
        /// <param name="data"> data to encrypt and compress</param>
        internal void EncryptCompressData(ref byte[] data)
        {
            if (ProtocoleManager.NoCompressorOrEncryptor)
                return;
            byte[] encrypted = ProtocoleManager.Encrypt(data);
            encrypted = ProtocoleManager.Compress(encrypted);
            byte[] Data = new byte[encrypted.Length + 4];
            // write lenght
            MessageLength = Data.Length;
            Data[0] = (byte)((MessageLength) & 0xFF);
            Data[1] = (byte)((MessageLength >> 8) & 0xFF);
            Data[2] = (byte)((MessageLength >> 16) & 0xFF);
            Data[3] = (byte)((MessageLength >> 24) & 0xFF);
            Buffer.BlockCopy(encrypted, 0, Data, 4, encrypted.Length);
            data = Data;
        }

        /// <summary>
        /// Decrypt and decompress data
        /// </summary>
        /// <param name="data"> data to decrypt and decompress</param>
        internal void DecryptDecompressData(ref byte[] data)
        {
            if (ProtocoleManager.NoCompressorOrEncryptor)
                return;
            if (data == null || data.Length < 4)
                throw new Exception("Invalid encrypted network message buffer");
            byte[] encrypted = new byte[data.Length - 4];
            Buffer.BlockCopy(data, 4, encrypted, 0, encrypted.Length);
            encrypted = ProtocoleManager.Decompress(encrypted);
            data = ProtocoleManager.Decrypt(encrypted);
        }

        /// <summary>
        /// Read the head of the message
        /// </summary>
        /// <param name="data"> data to read</param>
        internal void ReadHead(byte[] data)
        {
            if (data == null || data.Length < GetMinimumHeadSize())
                throw new Exception("Invalid network message header");
            MessageLength = BitConverter.ToInt32(data, 0);
            if (MessageLength < GetMinimumHeadSize() || MessageLength > data.Length)
                throw new Exception("Invalid network message length");
            ClientID = UInt24.GetUInt(data, 4);
            HeadID = BitConverter.ToUInt16(data, 7);
            MsgType = data[9];
            if (GetHeadSize() > data.Length)
                throw new Exception("Invalid network message header");
            if (MsgType == (byte)NetSquareMessageType.Reply)
                ReplyID = UInt24.GetUInt(data, 10);
            else
                ReplyID = 0;
        }

        /// <summary>
        /// Write the head of the message
        /// </summary>
        /// <param name="data"> data to write the head</param>
        internal void WriteHead(ref byte[] data)
        {
            WriteHead(data, data.Length);
        }

        /// <summary>
        /// Executes the write head operation.
        /// </summary>
        internal void WriteHead(byte[] data, int messageLength)
        {
            // write message Size
            data[0] = (byte)((messageLength) & 0xFF);
            data[1] = (byte)((messageLength >> 8) & 0xFF);
            data[2] = (byte)((messageLength >> 16) & 0xFF);
            data[3] = (byte)((messageLength >> 24) & 0xFF);
            // write Client ID
            data[4] = (byte)((ClientID) & 0xFF);
            data[5] = (byte)((ClientID >> 8) & 0xFF);
            data[6] = (byte)((ClientID >> 16) & 0xFF);
            // write Head Action
            data[7] = (byte)((HeadID) & 0xFF);
            data[8] = (byte)((HeadID >> 8) & 0xFF);
            // write Type ID
            data[9] = MsgType;
            // write Reply ID if needed
            if (MsgType == (byte)NetSquareMessageType.Reply)
            {
                data[10] = (byte)((ReplyID) & 0xFF);
                data[11] = (byte)((ReplyID >> 8) & 0xFF);
                data[12] = (byte)((ReplyID >> 16) & 0xFF);
            }
        }
        #endregion

        #region Datagram
        /// <summary>
        /// Executes the safe set datagram operation.
        /// </summary>
        public bool SafeSetDatagram(byte[] data)
        {
            try
            {
                // check if at least we got full head
                if (data == null || data.Length < GetMinimumHeadSize())
                    return false;
                DecryptDecompressData(ref data);
                if (data.Length < GetMinimumHeadSize())
                    return false;
                // read head
                ReadHead(data);
                // check if lenght == to datagram lenght
                if ((int)MessageLength != data.Length)
                    return false;
                // set data from datagram
                SetDataUnsafe(data);
                return true;
            }
            catch { return false; }
        }
        #endregion

        #region Set Data
        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(byte val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(short val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(int val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(long val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(ushort val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(uint val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(ulong val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(float val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(bool val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(char val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(string val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(UInt24 val)
        {
            Serializer.Set(val);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(byte[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(byte[] val, int offset, int count, bool writeLength = true)
        {
            Serializer.Set(val, offset, count, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(int[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(uint[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(long[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(ulong[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(short[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(ushort[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(float[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }

        /// <summary>
        /// Executes the set operation.
        /// </summary>
        public NetworkMessage Set(bool[] val, bool writeLength = true)
        {
            Serializer.Set(val, writeLength);
            return this;
        }
        #endregion

        /// <summary>
        /// Tram Definition :
        ///     - FullMessageSize : Int32   4 bytes
        ///     - ClientID :        Int32   4 bytes
        ///     - HeadAction :      Int16   2 bytes
        ///     - MsgType :         byte    1 bytes
        ///     - ReplyID :         Int32   4 bytes (only if MsgType == 1)
        ///     - Data :            var     FullMessageSize - 12 bytes or 15 if MsgType == 1
        ///     
        /// Data Definition :
        ///     - Primitive type, size by type => Function Enum to Size
        ///     - Custom or String : HEAD : size Int32 4 bytes  |   BODY : deserialize by type
        /// </summary>
        /// <returns></returns>
        public byte[] Serialize(bool ignoreCompression = false)
        {
            if (IsSerialized)
                return Serializer.ToArray();

            int currentIndex = GetHeadSize();
            MessageLength = currentIndex + Serializer.Length;
            // create full empty array
            byte[] data = new byte[MessageLength];
            // Write head
            WriteHead(ref data);
            // Write body
            Serializer.CopyTo(data, currentIndex);
            // Encrypt and compress data
            if (!ProtocoleManager.NoCompressorOrEncryptor && !ignoreCompression)
                EncryptCompressData(ref data);
            // set data ready to read
            Serializer.StartReading(data);
            return data;
        }

        /// <summary>
        /// Executes the serialize pooled operation.
        /// </summary>
        internal PooledByteBuffer SerializePooled(bool ignoreCompression = false)
        {
            if (IsSerialized)
                return PooledByteBuffer.Wrap(Serializer.ToArray());

            if (!ProtocoleManager.NoCompressorOrEncryptor && !ignoreCompression)
                return PooledByteBuffer.Wrap(Serialize(ignoreCompression));

            int currentIndex = GetHeadSize();
            MessageLength = currentIndex + Serializer.Length;
            PooledByteBuffer data = PooledByteBuffer.Rent(MessageLength);
            WriteHead(data.Buffer, MessageLength);
            Serializer.CopyTo(data.Buffer, currentIndex);
            return data;
        }

        /// <summary>
        /// Pack multiple messages into one
        /// </summary>
        /// <param name="messages"> messages to pack</param>
        /// <param name="alreadySerialized"> if messages are already serialized</param>
        /// <returns> packed message</returns>
        public NetworkMessage Pack(IEnumerable<NetworkMessage> messages, bool alreadySerialized = false)
        {
            // Packed message will be as follow
            // ======== HEAD =========
            //  - FullMessageSize : Int32   4 bytes
            //  - ClientID :        Int24   3 bytes
            //  - HeadAction :      Int16   2 bytes
            //  - MsgType :          byte    1 bytes
            //  - ReplyID :         Int24   3 bytes (only if MsgType == 1)
            // ======== DATA =========  <= For each message
            //  - BlockSize :       Int24   3 bytes
            //  - ClientID :        Int24   3 bytes
            //  - Data :            var     BlockSize bytes

            int prefixLength = 0;
            if (!IsSerialized && Serializer.Length > 0)
                prefixLength = Serializer.Length;

            // count packed message lenght
            int headSize = GetHeadSize();
            int lenght = headSize + prefixLength; // headSize (10 bits or 13 bits if MsgType == 1)
            int nb = 0;
            foreach (NetworkMessage message in messages)
            {
                int blockLength = GetPackBlockLength(message, alreadySerialized);
                if (blockLength > UInt24.MaxValue)
                    throw new Exception("Packed message block is too large");
                lenght += blockLength + 6; // blockSize (3 bits) + clientID (3 bits)
                nb++;
            }

            if (nb == 0 && prefixLength == 0)
                return this;

            // create full empty array
            byte[] data = new byte[lenght];
            // index start at headSize, because the head will be written at the end
            int index = headSize;
            if (prefixLength > 0)
            {
                Serializer.CopyTo(data, index);
                index += prefixLength;
            }
            // Write Blocks
            foreach (NetworkMessage message in messages)
            {
                // Write block Lenght
                int blockLength = GetPackBlockLength(message, alreadySerialized);
                UInt24 blockSize = new UInt24((uint)blockLength);
                data[index++] = blockSize.b0;
                data[index++] = blockSize.b1;
                data[index++] = blockSize.b2;

                // Write client ID
                data[index++] = (byte)((message.ClientID) & 0xFF);
                data[index++] = (byte)((message.ClientID >> 8) & 0xFF);
                data[index++] = (byte)((message.ClientID >> 16) & 0xFF);

                int sourceOffset = alreadySerialized ? message.GetHeadSize() : 0;
                Buffer.BlockCopy(message.Serializer.Buffer, sourceOffset, data, index, blockLength);
                index += blockLength;
            }

            // Write head
            WriteHead(ref data);
            // Encrypt and compress data
            if (!ProtocoleManager.NoCompressorOrEncryptor)
                EncryptCompressData(ref data);

            // set data ready to read
            Serializer.StartReading(data);
            RestartRead();
            return this;
        }

        /// <summary>
        /// Executes the get pack block length operation.
        /// </summary>
        private static int GetPackBlockLength(NetworkMessage message, bool alreadySerialized)
        {
            if (!alreadySerialized)
                return message.Serializer.Length;

            int length = message.Serializer.Length - message.GetHeadSize();
            return length < 0 ? 0 : length;
        }

        /// <summary>
        /// Unpack packed messages
        /// </summary>
        /// <returns> unpacked messages</returns>
        public List<NetworkMessage> Unpack()
        {
            // ======== DATA =========  <= For each message
            //  - BlockSize :       Int24   3 bytes
            //  - ClientID :        Int24   3 bytes
            //  - Data :            var     BlockSize bytes
            List<NetworkMessage> messages = new List<NetworkMessage>();
            // reading each packed blocks
            while (CanGetNextBlock())
            {
                // get block size
                int size = (int)Serializer.GetUInt24().UInt32;
                // get clientID
                uint clientID = Serializer.GetUInt24().UInt32;
                // create message
                NetworkMessage message = new NetworkMessage(HeadID, clientID);
                message.MsgType = MsgType;
                message.ReplyID = ReplyID;
                // copy block data into message
                byte[] data = new byte[size];
                Buffer.BlockCopy(Serializer.Buffer, Serializer.Position, data, 0, size);
                Serializer.DummyRead(size);
                // add message to list
                message.Serializer = new NetSquareSerializer();
                message.Serializer.StartReading(data);
                message.Serializer.Position = 0;
                messages.Add(message);
            }

            return messages;
        }
    }
}
