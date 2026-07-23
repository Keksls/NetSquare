using NetSquare.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace NetSquare.Core
{
    /// <summary>
    /// Represents the network message component.
    /// </summary>
    public class NetworkMessage
    {
        /// <summary>
        /// Stores the maximum accepted message size after decompression and decryption.
        /// </summary>
        public static int MaxDecodedMessageSize = 16 * 1024 * 1024;
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
                return blockSize.UInt32 <= int.MaxValue - 7 &&
                    Serializer.CanReadFor((int)blockSize.UInt32 + 7);
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
                bool canGetNextBlock = blockSize.UInt32 <= int.MaxValue - 7 &&
                    Serializer.CanReadFor((int)blockSize.UInt32 + 7);
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
            return MsgType == (byte)NetSquareMessageType.Reply ? 14 : 11;
        }

        /// <summary>
        /// Executes the get minimum head size operation.
        /// </summary>
        private static int GetMinimumHeadSize()
        {
            return 11;
        }

        /// <summary>
        /// Just set Data, no decryption / decompression, no head reading
        /// </summary>
        /// <param name="data">data to set</param>
        /// <param name="length">Logical message length inside the backing buffer.</param>
        public void SetDataUnsafe(byte[] data, int length = 0)
        {
            Serializer.StartReading(data, length);
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
        /// <param name="length">Logical encrypted message length inside the backing buffer.</param>
        internal void DecryptDecompressData(ref byte[] data, int length = 0)
        {
            int effectiveLength = length > 0 ? length : (data?.Length ?? 0);
            int maxDecodedMessageSize = MaxDecodedMessageSize;
            if (maxDecodedMessageSize < GetMinimumHeadSize())
                throw new InvalidOperationException("MaxDecodedMessageSize is below the minimum message header size.");
            if (data == null ||
                effectiveLength > data.Length ||
                effectiveLength < 4 ||
                effectiveLength > maxDecodedMessageSize)
                throw new InvalidDataException("Invalid network message buffer.");

            if (ProtocoleManager.NoCompressorOrEncryptor)
                return;

            byte[] encrypted = new byte[effectiveLength - 4];
            Buffer.BlockCopy(data, 4, encrypted, 0, encrypted.Length);
            encrypted = ProtocoleManager.Decompress(encrypted, maxDecodedMessageSize);
            data = ProtocoleManager.Decrypt(encrypted);
            if (data == null || data.Length > maxDecodedMessageSize)
                throw new InvalidDataException("The decoded network message exceeds the configured limit.");
        }

        /// <summary>
        /// Read the head of the message
        /// </summary>
        /// <param name="data"> data to read</param>
        /// <param name="length">Logical message length inside the backing buffer.</param>
        internal void ReadHead(byte[] data, int length = 0)
        {
            int effectiveLength = length > 0 ? length : (data?.Length ?? 0);
            if (data == null || effectiveLength > data.Length || effectiveLength < GetMinimumHeadSize())
                throw new Exception("Invalid network message header");
            MessageLength = BitConverter.ToInt32(data, 0);
            if (MessageLength < GetMinimumHeadSize() || MessageLength > effectiveLength)
                throw new Exception("Invalid network message length");
            ClientID = BitConverter.ToUInt32(data, 4);
            HeadID = BitConverter.ToUInt16(data, 8);
            MsgType = data[10];
            if (GetHeadSize() > effectiveLength)
                throw new Exception("Invalid network message header");
            if (MsgType == (byte)NetSquareMessageType.Reply)
                ReplyID = UInt24.GetUInt(data, 11);
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
            data[7] = (byte)((ClientID >> 24) & 0xFF);
            // write Head Action
            data[8] = (byte)((HeadID) & 0xFF);
            data[9] = (byte)((HeadID >> 8) & 0xFF);
            // write Type ID
            data[10] = MsgType;
            // write Reply ID if needed
            if (MsgType == (byte)NetSquareMessageType.Reply)
            {
                data[11] = (byte)((ReplyID) & 0xFF);
                data[12] = (byte)((ReplyID >> 8) & 0xFF);
                data[13] = (byte)((ReplyID >> 16) & 0xFF);
            }
        }
        #endregion

        #region Datagram
        /// <summary>
        /// Executes the safe set datagram operation.
        /// </summary>
        public bool SafeSetDatagram(byte[] data)
        {
            return SafeSetDatagram(data, data?.Length ?? 0);
        }

        /// <summary>
        /// Reads one NetworkMessage stored at the beginning of a larger authenticated datagram buffer.
        /// </summary>
        /// <param name="data">Datagram backing buffer.</param>
        /// <param name="dataLength">Authenticated NetworkMessage length without the MAC trailer.</param>
        /// <returns>True when the message header and logical length are valid.</returns>
        public bool SafeSetDatagram(byte[] data, int dataLength)
        {
            try
            {
                if (data == null || dataLength < GetMinimumHeadSize() || dataLength > data.Length)
                    return false;
                bool transformsData = !ProtocoleManager.NoCompressorOrEncryptor;
                DecryptDecompressData(ref data, dataLength);
                if (transformsData)
                    dataLength = data.Length;
                if (dataLength < GetMinimumHeadSize())
                    return false;
                ReadHead(data, dataLength);
                if (MessageLength != dataLength)
                    return false;
                SetDataUnsafe(data, dataLength);
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
        public NetworkMessage Set(double val)
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
        public NetworkMessage Set(double[] val, bool writeLength = true)
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
        ///     - ReplyID :         Int24   3 bytes (only if MsgType == 1)
        ///     - Data :            var     FullMessageSize - 11 bytes or 14 if MsgType == 1
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
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            // Packed message will be as follow
            // ======== HEAD =========
            //  - FullMessageSize : Int32   4 bytes
            //  - ClientID :        UInt32  4 bytes
            //  - HeadAction :      Int16   2 bytes
            //  - MsgType :          byte    1 bytes
            //  - ReplyID :         Int24   3 bytes (only if MsgType == 1)
            // ======== DATA =========  <= For each message
            //  - BlockSize :       Int24   3 bytes
            //  - ClientID :        UInt32  4 bytes
            //  - Data :            var     BlockSize bytes

            int prefixLength = 0;
            if (!IsSerialized && Serializer.Length > 0)
                prefixLength = Serializer.Length;

            // Materialize once so one-shot enumerables cannot change between sizing and serialization.
            List<NetworkMessage> blocks = new List<NetworkMessage>();
            int headSize = GetHeadSize();
            int length = checked(headSize + prefixLength);
            foreach (NetworkMessage message in messages)
            {
                if (message == null)
                    throw new InvalidDataException("Packed messages cannot contain null entries.");
                if (blocks.Count >= NetSquareSerializer.MaxCollectionLength)
                    throw new InvalidDataException("The packed message contains too many blocks.");

                int blockLength = GetPackBlockLength(message, alreadySerialized);
                if (blockLength < 0 || blockLength > UInt24.MaxValue)
                    throw new InvalidDataException("Packed message block is too large.");

                length = checked(length + blockLength + 7);
                if (length > MaxDecodedMessageSize)
                    throw new InvalidDataException("The packed message exceeds the configured decoded size limit.");
                blocks.Add(message);
            }

            if (blocks.Count == 0 && prefixLength == 0)
                return this;

            byte[] data = new byte[length];
            // index start at headSize, because the head will be written at the end
            int index = headSize;
            if (prefixLength > 0)
            {
                Serializer.CopyTo(data, index);
                index += prefixLength;
            }
            // Write Blocks
            foreach (NetworkMessage message in blocks)
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
                data[index++] = (byte)((message.ClientID >> 24) & 0xFF);

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
            //  - ClientID :        UInt32  4 bytes
            //  - Data :            var     BlockSize bytes
            List<NetworkMessage> messages = new List<NetworkMessage>();
            while (CanGetNextBlock())
            {
                if (messages.Count >= NetSquareSerializer.MaxCollectionLength)
                    throw new InvalidDataException("The packed message contains too many blocks.");
                // get block size
                int size = (int)Serializer.GetUInt24().UInt32;
                // get clientID
                uint clientID = Serializer.GetUInt();
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

            if (!Serializer.EndOfStream)
                throw new InvalidDataException("The packed message contains a truncated block.");
            return messages;
        }
    }
}
