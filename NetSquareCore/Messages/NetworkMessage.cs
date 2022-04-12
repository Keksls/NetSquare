using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace NetSquare.Core
{
    public class NetworkMessage
    {
        public uint ClientID { get; set; }
        public int TypeID { get; set; }
        public ushort Head { get; set; }
        public List<MessageBlockData> Blocks { get; set; }
        public ConnectedClient Client { get; set; }
        public byte[] Data { get; set; }
        public int Length { get; set; }
        private int currentReadingIndex = 0;

        /// <summary>
        /// this id will be keeped and pass thrown new message by the server. Used for reply callback
        /// </summary>
        /// <param name="id"></param>
        public void ReplyTo(int id)
        {
            SetType(id + 10);
        }

        /// <summary>
        /// Set the type of this message. 
        /// 0 => simple message send to server
        /// 1 => message that will be broadcasted to avery other clients on my lobby
        /// 
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

        /// <summary>
        /// New empty network message
        /// </summary>
        /// <param name="headID">HeadID of the message (used by dispatcher to invoke related callback)</param>
        /// <param name="clientID">ID of the client that send message</param>
        public NetworkMessage(ushort headID, uint clientID)
        {
            Head = headID;
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
            Head = 0;
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
            Head = headID;
            ClientID = 0;
            Length = 0;
            TypeID = 0;
            Blocks = new List<MessageBlockData>();
        }

        /// <summary>
        /// Get ClientID, HeadAction and Data from msg byte[]
        /// </summary>
        /// <param name="msg">byte array received from the TcpClient</param>
        /// <param name="client">TcpClient object that send this message</param>
        public NetworkMessage(byte[] msg, ConnectedClient client)
        {
            msg = ProtocoleManager.Decompress(msg);
            msg = ProtocoleManager.Decrypt(msg);
            Client = client;
            ClientID = BitConverter.ToUInt32(msg, 0);
            Head = BitConverter.ToUInt16(msg, 4);
            TypeID = BitConverter.ToInt32(msg, 6);
            Data = msg;
            Length = Data.Length;
            RestartRead();
        }

        /// <summary>
        /// Get ClientID, HeadAction and Data from msg byte[]
        /// </summary>
        /// <param name="msg">byte array received from the TcpClient</param>
        public NetworkMessage(byte[] msg)
        {
            msg = ProtocoleManager.Decompress(msg);
            msg = ProtocoleManager.Decrypt(msg);
            Client = null;
            ClientID = BitConverter.ToUInt32(msg, 0);
            Head = BitConverter.ToUInt16(msg, 4);
            TypeID = BitConverter.ToInt32(msg, 6);
            Data = msg;
            Length = Data.Length;
            RestartRead();
        }

        #region Reading
        /// <summary>
        /// reset the reading index. use it if you already have read this message and you want to read it again.
        /// </summary>
        public void RestartRead()
        {
            currentReadingIndex = 10; // start + ClientID + Head + ReplyID
        }

        /// <summary>
        /// Set the reading index. use it if you want to go a specific index of the data array.
        /// </summary>
        public void SetReadingIndex(int readIndex)
        {
            currentReadingIndex = readIndex; // start + ClientID + Head + ReplyID
        }

        public bool CanGetByte()
        {
            return currentReadingIndex + 1 <= Length;
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
            return currentReadingIndex + 2 <= Length;
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
            return currentReadingIndex + 4 <= Length;
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
            return currentReadingIndex + 8 <= Length;
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
            return currentReadingIndex + 2 <= Length;
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
            return currentReadingIndex + 4 <= Length;
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
            return currentReadingIndex + 8 <= Length;
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
            return currentReadingIndex + 4 <= Length;
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
            return currentReadingIndex + 1 <= Length;
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
            return currentReadingIndex + 1 <= Length;
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
            return currentReadingIndex + 8 <= Length;
        }

        public string GetString()
        {
            int size = 0;
            Get(ref size);
            string val = System.Text.Encoding.Default.GetString(Data, currentReadingIndex, size);
            currentReadingIndex += size;
            return val;
        }

        public void Get(ref string val)
        {
            int size = 0;
            Get(ref size);
            val = System.Text.Encoding.Default.GetString(Data, currentReadingIndex, size);
            currentReadingIndex += size;
        }

        public bool CanGetObject()
        {
            return currentReadingIndex + 8 <= Length;
        }

        /// <summary>
        /// Get a complex or custom object. Will be Json serialized
        /// </summary>
        public T GetObject<T>()
        {
            string json = "";
            Get(ref json);
            return JsonConvert.DeserializeObject<T>(json);
        }
        #endregion

        #region Set Data
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
        public NetworkMessage SetObject(object val)
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
        ///     - ReplyID :         Int32   4 bytes
        ///     - Data :            var     var
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
                // write size
                byte[] finalArray = new byte[Data.Length + 4];
                Array.Copy(BitConverter.GetBytes(Data.Length), 0, finalArray, 0, 4);
                Array.Copy(Data, 0, finalArray, 4, Data.Length);
                Array.Copy(BitConverter.GetBytes(Head), 0, finalArray, 8, 2);
                return finalArray;
            }

            // process Size
            Length = 10; // head size : ClientID + HeadAction + ReplyID (4 + 4 + 2 + 4)
            foreach (MessageBlockData block in Blocks)
                Length += block.Size;

            // create full empty array
            Data = new byte[Length];
            // write Client ID
            Array.Copy(BitConverter.GetBytes(ClientID), 0, Data, 0, 4);
            // write Head Action
            Array.Copy(BitConverter.GetBytes(Head), 0, Data, 4, 2);
            // write Client ID
            Array.Copy(BitConverter.GetBytes(TypeID), 0, Data, 6, 4);

            // Write Blocks
            Length = 10;
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
                        Array.Copy(block.data, 0, Data, Length, block.Size);
                        Length += block.Size;
                        break;

                    // primitiv types, simply copy data block to message buffer
                    default:
                    case MessageBlockType.String:
                    case MessageBlockType.Custom:
                        // Write data size
                        byte[] sizeData = BitConverter.GetBytes(block.Size - 4);
                        Array.Copy(sizeData, 0, Data, Length, 4);
                        Array.Copy(block.data, 0, Data, Length + 4, block.Size - 4);
                        Length += block.Size;
                        break;
                }
            }

            Data = ProtocoleManager.Encrypt(Data);
            Data = ProtocoleManager.Compress(Data);

            // write size
            byte[] encapsulatedArray = new byte[Data.Length + 4];
            Array.Copy(BitConverter.GetBytes(Data.Length), 0, encapsulatedArray, 0, 4);
            Array.Copy(Data, 0, encapsulatedArray, 4, Data.Length);

            return encapsulatedArray;
        }
    }
}