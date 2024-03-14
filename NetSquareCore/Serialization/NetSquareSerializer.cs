using System;
using System.Collections.Generic;
using System.Text;

namespace NetSquare.Core
{
    public class NetSquareSerializer
    {
        private byte[] _buffer;
        private int _position;
        private int _length;
        private int _growMin;
        private NetSquareSerializationMode _serializationMode = NetSquareSerializationMode.None;

        /// <summary>
        /// Start reading from a new buffer
        /// </summary>
        /// <param name="buffer"> The buffer to read from </param>
        /// <param name="length"> The length of the buffer </param>
        /// <param name="position"> The position in the buffer </param>
        public void StartReading(byte[] buffer, int length = 0, int position = 0)
        {
            _buffer = buffer;
            _position = position;
            _length = length > 0 ? Math.Min(length, _buffer.Length) : _buffer.Length;
            _serializationMode = NetSquareSerializationMode.Read;
        }

        /// <summary>
        /// Start reading by keeping the current buffer
        /// </summary>
        /// <param name="length"> The length of the buffer </param>
        /// <param name="position"> The position in the buffer </param>
        public void StartReading(int length = 0, int position = 0)
        {
            _length = length > 0 ? Math.Min(length, _buffer.Length) : Math.Min(position, _buffer.Length);
            _position = position;
            _serializationMode = NetSquareSerializationMode.Read;
        }

        /// <summary>
        /// Start writing to a new buffer
        /// </summary>
        /// <param name="capacity"> The initial capacity of the buffer </param>
        /// <param name="growMin"> The minimum grow size of the buffer. if = 0, the buffer will double in size when it needs to grow, otherwise it will grow by the specified amount </param>
        public void StartWriting(int capacity = 1024, int growMin = 0)
        {
            _buffer = new byte[capacity];
            _position = 0;
            _growMin = growMin;
            _serializationMode = NetSquareSerializationMode.Write;
        }

        #region Get Methods
        /// <summary>
        /// Get a byte from the buffer
        /// </summary>
        /// <returns> The byte Get </returns>
        /// <exception cref="Exception"></exception>
        public byte GetByte()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 1 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    return _buffer[_position++];

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get an int from the buffer
        /// </summary>
        /// <returns> The int Get </returns>
        /// <exception cref="Exception"></exception>
        public int GetInt()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    int value = BitConverter.ToInt32(_buffer, _position);
                    _position += 4;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get an uint from the buffer
        /// </summary>
        /// <returns> The uint Get </returns>
        /// <exception cref="Exception"></exception>
        public uint GetUInt()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    uint value = BitConverter.ToUInt32(_buffer, _position);
                    _position += 4;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a short from the buffer
        /// </summary>
        /// <returns> The short Get </returns>
        /// <exception cref="Exception"></exception>
        public short GetShort()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 2 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    short value = BitConverter.ToInt16(_buffer, _position);
                    _position += 2;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get an ushort from the buffer
        /// </summary>
        /// <returns> The ushort Get </returns>
        /// <exception cref="Exception"></exception>
        public ushort GetUShort()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 2 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    ushort value = BitConverter.ToUInt16(_buffer, _position);
                    _position += 2;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a UInt24 from the buffer
        /// </summary>
        /// <returns> The UInt24 Get </returns>
        /// <exception cref="Exception"></exception>
        public UInt24 GetUInt24()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 3 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    UInt24 value = new UInt24(_buffer[_position], _buffer[_position + 1], _buffer[_position + 2]);
                    _position += 3;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a long from the buffer
        /// </summary>
        /// <returns> The long Get </returns>
        /// <exception cref="Exception"></exception>
        public long GetLong()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 8 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    long value = BitConverter.ToInt64(_buffer, _position);
                    _position += 8;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get an ulong from the buffer
        /// </summary>
        /// <returns> The ulong Get </returns>
        /// <exception cref="Exception"></exception>
        public ulong GetULong()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 8 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    ulong value = BitConverter.ToUInt64(_buffer, _position);
                    _position += 8;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a float from the buffer
        /// </summary>
        /// <returns> The float Get </returns>
        /// <exception cref="Exception"></exception>
        public float GetFloat()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    float value = BitConverter.ToSingle(_buffer, _position);
                    _position += 4;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a double from the buffer
        /// </summary>
        /// <returns> The double Get </returns>
        /// <exception cref="Exception"></exception>
        public double GetDouble()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 8 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    double value = BitConverter.ToDouble(_buffer, _position);
                    _position += 8;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a string from the buffer
        /// </summary>
        /// <returns> The string Get </returns>
        /// <exception cref="Exception"></exception>
        public string GetString()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    string value = Encoding.UTF8.GetString(_buffer, _position, length);
                    _position += length;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a char from the buffer
        /// </summary>
        /// <returns> The char Get </returns>
        /// <exception cref="Exception"></exception>
        public char GetChar()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 2 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    char value = BitConverter.ToChar(_buffer, _position);
                    _position += 2;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a ushort array from the buffer
        /// </summary>
        /// <returns> The ushort array Get </returns>
        /// <exception cref="Exception"></exception>
        public byte[] GetByteArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    byte[] value = new byte[length];
                    Array.Copy(_buffer, _position, value, 0, length);
                    _position += length;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a ushort array from the buffer
        /// </summary>
        /// <returns> The ushort array Get </returns>
        /// <exception cref="Exception"></exception>
        public ushort[] GetUShortArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length * 2 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    ushort[] value = new ushort[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = BitConverter.ToUInt16(_buffer, _position);
                        _position += 2;
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a short array from the buffer
        /// </summary>
        /// <returns> The short array Get </returns>
        /// <exception cref="Exception"></exception>
        public short[] GetShortArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length * 2 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    short[] value = new short[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = BitConverter.ToInt16(_buffer, _position);
                        _position += 2;
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get an int array from the buffer
        /// </summary>
        /// <returns> The int array Get </returns>
        /// <exception cref="Exception"></exception>
        public int[] GetIntArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length * 4 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    int[] value = new int[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = BitConverter.ToInt32(_buffer, _position);
                        _position += 4;
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a uint array from the buffer
        /// </summary>
        /// <returns> The uint array Get </returns>
        /// <exception cref="Exception"></exception>
        public uint[] GetUIntArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length * 4 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    uint[] value = new uint[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = BitConverter.ToUInt32(_buffer, _position);
                        _position += 4;
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a long array from the buffer
        /// </summary>
        /// <returns> The long array Get </returns>
        /// <exception cref="Exception"></exception>
        public long[] GetLongArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length * 8 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    long[] value = new long[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = BitConverter.ToInt64(_buffer, _position);
                        _position += 8;
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a ulong array from the buffer
        /// </summary>
        /// <returns> The ulong array Get </returns>
        /// <exception cref="Exception"></exception>
        public ulong[] GetULongArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length * 8 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    ulong[] value = new ulong[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = BitConverter.ToUInt64(_buffer, _position);
                        _position += 8;
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a float array from the buffer
        /// </summary>
        /// <returns> The float array Get </returns>
        /// <exception cref="Exception"></exception>
        public float[] GetFloatArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length * 4 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    float[] value = new float[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = BitConverter.ToSingle(_buffer, _position);
                        _position += 4;
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a double array from the buffer
        /// </summary>
        /// <returns> The double array Get </returns>
        /// <exception cref="Exception"></exception>
        public double[] GetDoubleArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    if (_position + length * 8 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    double[] value = new double[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = BitConverter.ToDouble(_buffer, _position);
                        _position += 8;
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a string array from the buffer
        /// </summary>
        /// <returns> The string array Get </returns>
        /// <exception cref="Exception"></exception>
        public string[] GetStringArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    string[] value = new string[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = GetString();
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a bool from the buffer
        /// </summary>
        /// <returns> The bool Get </returns>
        /// <exception cref="Exception"></exception>
        public bool GetBool()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 1 > _length)
                    {
                        throw new Exception("Buffer overflow");
                    }
                    bool value = BitConverter.ToBoolean(_buffer, _position);
                    _position += 1;
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a serializable object from the buffer
        /// </summary>
        /// <typeparam name="T"> The type of the object </typeparam>
        /// <returns> The object Get </returns>
        public T GetSerializable<T>() where T : INetSquareSerializable, new()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    T value = new T();
                    value.Deserialize(this);
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get an array of serializable objects from the buffer
        /// </summary>
        /// <typeparam name="T"> The type of the objects </typeparam>
        /// <returns> The array Get </returns>
        public T[] GetArray<T>() where T : INetSquareSerializable, new()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    T[] value = new T[length];
                    for (int i = 0; i < length; i++)
                    {
                        value[i] = GetSerializable<T>();
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a list of serializable objects from the buffer
        /// </summary>
        /// <typeparam name="T"> The type of the objects </typeparam>
        /// <returns> The list Get </returns>
        public List<T> GetList<T>() where T : INetSquareSerializable, new()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    List<T> value = new List<T>(length);
                    for (int i = 0; i < length; i++)
                    {
                        value.Add(GetSerializable<T>());
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Get a dictionary of serializable objects from the buffer
        /// </summary>
        /// <typeparam name="TKey"> The type of the keys </typeparam>
        /// <typeparam name="TValue"> The type of the values </typeparam>
        /// <returns> The dictionary Get </returns>
        public Dictionary<TKey, TValue> GetDictionary<TKey, TValue>() where TKey : INetSquareSerializable, new() where TValue : INetSquareSerializable, new()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    int length = GetInt();
                    Dictionary<TKey, TValue> value = new Dictionary<TKey, TValue>(length);
                    for (int i = 0; i < length; i++)
                    {
                        TKey key = GetSerializable<TKey>();
                        TValue val = GetSerializable<TValue>();
                        value.Add(key, val);
                    }
                    return value;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }
        #endregion

        #region Can Get
        /// <summary>
        /// Check if the buffer is long enough to Get a byte
        /// </summary>
        /// <param name="length"> The length of the byte </param>
        /// <returns> True if the buffer is long enough, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanReadFor(int length)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + length <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if the buffer is long enough to Get a byte
        /// </summary>
        /// <param name="length"> The length of the byte </param>
        /// <returns> True if the buffer is long enough, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanReadFor(uint length)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + length <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a byte can be Get from the buffer
        /// </summary>
        /// <returns> True if a byte can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetByte()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 1 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if an int can be Get from the buffer
        /// </summary>
        /// <returns> True if an int can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetInt()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if an uint can be Get from the buffer
        /// </summary>
        /// <returns> True if an uint can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetUInt()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a short can be Get from the buffer
        /// </summary>
        /// <returns> True if a short can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetShort()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 2 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if an ushort can be Get from the buffer
        /// </summary>
        /// <returns> True if an ushort can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetUShort()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 2 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a UInt24 can be Get from the buffer
        /// </summary>
        /// <returns> True if a UInt24 can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetUInt24()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 3 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a long can be Get from the buffer
        /// </summary>
        /// <returns> True if a long can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetLong()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 8 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if an ulong can be Get from the buffer
        /// </summary>
        /// <returns> True if an ulong can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetULong()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 8 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a float can be Get from the buffer
        /// </summary>
        /// <returns> True if a float can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetFloat()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a double can be Get from the buffer
        /// </summary>
        /// <returns> True if a double can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetDouble()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 8 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a string can be Get from the buffer
        /// </summary>
        /// <returns> True if a string can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetString()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a char can be Get from the buffer
        /// </summary>
        /// <returns> True if a char can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetChar()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 2 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a byte array can be Get from the buffer
        /// </summary>
        /// <returns> True if a byte array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetByteArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a ushort array can be Get from the buffer
        /// </summary>
        /// <returns> True if a ushort array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetUShortArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length * 2 + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a short array can be Get from the buffer
        /// </summary>
        /// <returns> True if a short array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetShortArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length * 2 + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if an int array can be Get from the buffer
        /// </summary>
        /// <returns> True if an int array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetIntArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length * 4 + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }
        
        /// <summary>
        /// Check if an uint array can be Get from the buffer
        /// </summary>
        /// <returns> True if an uint array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetUIntArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length * 4 + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a long array can be Get from the buffer
        /// </summary>
        /// <returns> True if a long array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetLongArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length * 8 + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if an ulong array can be Get from the buffer
        /// </summary>
        /// <returns> True if an ulong array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetULongArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length * 8 + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a float array can be Get from the buffer
        /// </summary>
        /// <returns> True if a float array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetFloatArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length * 4 + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a double array can be Get from the buffer
        /// </summary>
        /// <returns> True if a double array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetDoubleArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    return _position + length * 8 + 4 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a string array can be Get from the buffer
        /// </summary>
        /// <returns> True if a string array can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetStringArray()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    if (_position + 4 > _length)
                    {
                        return false;
                    }
                    int length = BitConverter.ToInt32(_buffer, _position);
                    for (int i = 0; i < length; i++)
                    {
                        if (!CanGetString())
                        {
                            return false;
                        }
                    }
                    return true;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Check if a bool can be Get from the buffer
        /// </summary>
        /// <returns> True if a bool can be Get, false otherwise </returns>
        /// <exception cref="Exception"></exception>
        public bool CanGetBool()
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Read:
                    return _position + 1 <= _length;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }
        #endregion

        #region Set values
        /// <summary>
        /// Set a byte value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(byte value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(1);
                    _buffer[_position++] = value;
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a sbyte value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(sbyte value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(1);
                    _buffer[_position++] = (byte)value;
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a short value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(short value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(2);
                    _buffer[_position++] = (byte)value;
                    _buffer[_position++] = (byte)(value >> 8);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a ushort value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(ushort value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(2);
                    _buffer[_position++] = (byte)value;
                    _buffer[_position++] = (byte)(value >> 8);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a UInt24 value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        /// <exception cref="Exception"></exception>
        public void Set(UInt24 value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(3);
                    _buffer[_position++] = value.b0;
                    _buffer[_position++] = value.b1;
                    _buffer[_position++] = value.b2;
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set an int value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(int value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(4);
                    _buffer[_position++] = (byte)value;
                    _buffer[_position++] = (byte)(value >> 8);
                    _buffer[_position++] = (byte)(value >> 16);
                    _buffer[_position++] = (byte)(value >> 24);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a uint value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(uint value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(4);
                    _buffer[_position++] = (byte)value;
                    _buffer[_position++] = (byte)(value >> 8);
                    _buffer[_position++] = (byte)(value >> 16);
                    _buffer[_position++] = (byte)(value >> 24);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a long value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(long value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(8);
                    _buffer[_position++] = (byte)value;
                    _buffer[_position++] = (byte)(value >> 8);
                    _buffer[_position++] = (byte)(value >> 16);
                    _buffer[_position++] = (byte)(value >> 24);
                    _buffer[_position++] = (byte)(value >> 32);
                    _buffer[_position++] = (byte)(value >> 40);
                    _buffer[_position++] = (byte)(value >> 48);
                    _buffer[_position++] = (byte)(value >> 56);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a ulong value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(ulong value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(8);
                    _buffer[_position++] = (byte)value;
                    _buffer[_position++] = (byte)(value >> 8);
                    _buffer[_position++] = (byte)(value >> 16);
                    _buffer[_position++] = (byte)(value >> 24);
                    _buffer[_position++] = (byte)(value >> 32);
                    _buffer[_position++] = (byte)(value >> 40);
                    _buffer[_position++] = (byte)(value >> 48);
                    _buffer[_position++] = (byte)(value >> 56);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a float value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(float value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    byte[] bytes = BitConverter.GetBytes(value);
                    EnsureCapacity(4);
                    _buffer[_position++] = bytes[0];
                    _buffer[_position++] = bytes[1];
                    _buffer[_position++] = bytes[2];
                    _buffer[_position++] = bytes[3];
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a double value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(double value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    byte[] bytes = BitConverter.GetBytes(value);
                    EnsureCapacity(8);
                    _buffer[_position++] = bytes[0];
                    _buffer[_position++] = bytes[1];
                    _buffer[_position++] = bytes[2];
                    _buffer[_position++] = bytes[3];
                    _buffer[_position++] = bytes[4];
                    _buffer[_position++] = bytes[5];
                    _buffer[_position++] = bytes[6];
                    _buffer[_position++] = bytes[7];
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a string value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(string value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    byte[] bytes = Encoding.UTF8.GetBytes(value);
                    Set(bytes.Length);
                    EnsureCapacity(bytes.Length);
                    Array.Copy(bytes, 0, _buffer, _position, bytes.Length);
                    _position += bytes.Length;
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a byte array to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(byte[] value, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(value.Length);
                            break;
                    }
                    EnsureCapacity(value.Length);
                    Array.Copy(value, 0, _buffer, _position, value.Length);
                    _position += value.Length;
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a sbyte array to the buffer
        /// </summary>
        /// <param name="ushorts"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(ushort[] ushorts, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(ushorts.Length);
                            break;
                    }
                    EnsureCapacity(ushorts.Length * 2);
                    for (int i = 0; i < ushorts.Length; i++)
                    {
                        _buffer[_position++] = (byte)ushorts[i];
                        _buffer[_position++] = (byte)(ushorts[i] >> 8);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a short array to the buffer
        /// </summary>
        /// <param name="shorts"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(short[] shorts, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(shorts.Length);
                            break;
                    }
                    EnsureCapacity(shorts.Length * 2);
                    for (int i = 0; i < shorts.Length; i++)
                    {
                        _buffer[_position++] = (byte)shorts[i];
                        _buffer[_position++] = (byte)(shorts[i] >> 8);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set an int array to the buffer
        /// </summary>
        /// <param name="ints"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(int[] ints, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(ints.Length);
                            break;
                    }
                    EnsureCapacity(ints.Length * 4);
                    for (int i = 0; i < ints.Length; i++)
                    {
                        _buffer[_position++] = (byte)ints[i];
                        _buffer[_position++] = (byte)(ints[i] >> 8);
                        _buffer[_position++] = (byte)(ints[i] >> 16);
                        _buffer[_position++] = (byte)(ints[i] >> 24);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a uint array to the buffer
        /// </summary>
        /// <param name="uints"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(uint[] uints, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(uints.Length);
                            break;
                    }
                    EnsureCapacity(uints.Length * 4);
                    for (int i = 0; i < uints.Length; i++)
                    {
                        _buffer[_position++] = (byte)uints[i];
                        _buffer[_position++] = (byte)(uints[i] >> 8);
                        _buffer[_position++] = (byte)(uints[i] >> 16);
                        _buffer[_position++] = (byte)(uints[i] >> 24);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a long array to the buffer
        /// </summary>
        /// <param name="longs"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(long[] longs, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(longs.Length);
                            break;
                    }
                    EnsureCapacity(longs.Length * 8);
                    for (int i = 0; i < longs.Length; i++)
                    {
                        _buffer[_position++] = (byte)longs[i];
                        _buffer[_position++] = (byte)(longs[i] >> 8);
                        _buffer[_position++] = (byte)(longs[i] >> 16);
                        _buffer[_position++] = (byte)(longs[i] >> 24);
                        _buffer[_position++] = (byte)(longs[i] >> 32);
                        _buffer[_position++] = (byte)(longs[i] >> 40);
                        _buffer[_position++] = (byte)(longs[i] >> 48);
                        _buffer[_position++] = (byte)(longs[i] >> 56);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a ulong array to the buffer
        /// </summary>
        /// <param name="ulongs"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(ulong[] ulongs, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(ulongs.Length);
                            break;
                    }
                    EnsureCapacity(ulongs.Length * 8);
                    for (int i = 0; i < ulongs.Length; i++)
                    {
                        _buffer[_position++] = (byte)ulongs[i];
                        _buffer[_position++] = (byte)(ulongs[i] >> 8);
                        _buffer[_position++] = (byte)(ulongs[i] >> 16);
                        _buffer[_position++] = (byte)(ulongs[i] >> 24);
                        _buffer[_position++] = (byte)(ulongs[i] >> 32);
                        _buffer[_position++] = (byte)(ulongs[i] >> 40);
                        _buffer[_position++] = (byte)(ulongs[i] >> 48);
                        _buffer[_position++] = (byte)(ulongs[i] >> 56);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a float array to the buffer
        /// </summary>
        /// <param name="floats"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(float[] floats, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(floats.Length);
                            break;
                    }
                    EnsureCapacity(floats.Length * 4);
                    for (int i = 0; i < floats.Length; i++)
                    {
                        byte[] bytes = BitConverter.GetBytes(floats[i]);
                        _buffer[_position++] = bytes[0];
                        _buffer[_position++] = bytes[1];
                        _buffer[_position++] = bytes[2];
                        _buffer[_position++] = bytes[3];
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a double array to the buffer
        /// </summary>
        /// <param name="doubles"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(double[] doubles, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(doubles.Length);
                            break;
                    }
                    EnsureCapacity(doubles.Length * 8);
                    for (int i = 0; i < doubles.Length; i++)
                    {
                        byte[] bytes = BitConverter.GetBytes(doubles[i]);
                        _buffer[_position++] = bytes[0];
                        _buffer[_position++] = bytes[1];
                        _buffer[_position++] = bytes[2];
                        _buffer[_position++] = bytes[3];
                        _buffer[_position++] = bytes[4];
                        _buffer[_position++] = bytes[5];
                        _buffer[_position++] = bytes[6];
                        _buffer[_position++] = bytes[7];
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a string array to the buffer
        /// </summary>
        /// <param name="strings"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(string[] strings, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(strings.Length);
                            break;
                    }
                    for (int i = 0; i < strings.Length; i++)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(strings[i]);
                        Set(bytes.Length);
                        EnsureCapacity(bytes.Length);
                        Array.Copy(bytes, 0, _buffer, _position, bytes.Length);
                        _position += bytes.Length;
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a bool array to the buffer
        /// </summary>
        /// <param name="bools"> The value to Set </param>
        /// <param name="writeLenght"> Write the length of the array </param>
        /// <exception cref="Exception"></exception>
        public void Set(bool[] bools, bool writeLenght = true)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    switch (writeLenght)
                    {
                        case true:
                            Set(bools.Length);
                            break;
                    }
                    EnsureCapacity(bools.Length);
                    for (int i = 0; i < bools.Length; i++)
                    {
                        _buffer[_position++] = (byte)(bools[i] ? 1 : 0);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a bool value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(bool value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(1);
                    _buffer[_position++] = (byte)(value ? 1 : 0);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a char value to the buffer
        /// </summary>
        /// <param name="value"> The value to Set </param>
        public void Set(char value)
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    EnsureCapacity(2);
                    _buffer[_position++] = (byte)value;
                    _buffer[_position++] = (byte)(value >> 8);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a custom serializable object
        /// </summary>
        /// <typeparam name="T"> The type of the object </typeparam>
        /// <param name="value"> The value to Set </param>
        public void Set<T>(T value) where T : INetSquareSerializable
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    value.Serialize(this);
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a custom serializable object array
        /// </summary>
        /// <typeparam name="T"> The type of the object </typeparam>
        /// <param name="value"> The value to Set </param>
        public void Set<T>(T[] value) where T : INetSquareSerializable
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    Set(value.Length);
                    for (int i = 0; i < value.Length; i++)
                    {
                        value[i].Serialize(this);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a custom serializable object list
        /// </summary>
        /// <typeparam name="T"> The type of the object </typeparam>
        /// <param name="values"> The values to Set </param>
        public void Set<T>(List<T> values) where T : INetSquareSerializable
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    Set(values.Count);
                    for (int i = 0; i < values.Count; i++)
                    {
                        values[i].Serialize(this);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }

        /// <summary>
        /// Set a custom serializable object dictionary
        /// </summary>
        /// <typeparam name="T"> The type of the object </typeparam>
        /// <param name="values"> The values to Set </param>
        public void Set<T>(Dictionary<string, T> values) where T : INetSquareSerializable
        {
            switch (_serializationMode)
            {
                case NetSquareSerializationMode.Write:
                    Set(values.Count);
                    foreach (var value in values)
                    {
                        Set(value.Key);
                        value.Value.Serialize(this);
                    }
                    break;

                default:
                    throw new Exception("Invalid serialization mode");
            }
        }
        #endregion

        /// <summary>
        /// Get the byte array
        /// </summary>
        /// <returns> The byte array </returns>
        public byte[] ToArray()
        {
            if (_serializationMode == NetSquareSerializationMode.Read)
                return _buffer;

            byte[] result = new byte[_position];
            Array.Copy(_buffer, result, _position);
            return result;
        }

        /// <summary>
        /// Ensure the buffer capacity
        /// </summary>
        /// <param name="length"> The length to ensure </param>
        private void EnsureCapacity(int length)
        {
            if (_position + length > _buffer.Length)
            {
                byte[] newBuffer = new byte[_growMin == 0 ? _buffer.Length * 2 : _buffer.Length + length + _growMin];
                Array.Copy(_buffer, newBuffer, _position);
                _buffer = newBuffer;
            }
        }

        /// <summary>
        /// Reset the buffer position
        /// </summary>
        public void Reset()
        {
            _position = 0;
        }

        /// <summary>
        /// Skip a number of bytes in the buffer
        /// </summary>
        /// <param name="length"> The number of bytes to skip </param>
        /// <exception cref="Exception"></exception>
        public void DummyRead(int length)
        {
            if (_position + length > _length)
            {
                throw new Exception("Buffer overflow");
            }
            _position += length;
        }

        /// <summary>
        /// Check if the end of the buffer has been reached
        /// </summary>
        public bool EndOfStream
        {
            get { return _position >= _length; }
        }

        /// <summary>
        /// Get the length of the buffer in the current serialization mode
        /// </summary>
        public int Length
        {
            get
            {
                switch (_serializationMode)
                {
                    default:
                    case NetSquareSerializationMode.None:
                        return 0;

                    case NetSquareSerializationMode.Read:
                        return _length;

                    case NetSquareSerializationMode.Write:
                        return _position;
                }
            }
        }

        /// <summary>
        /// Get or set the position in the buffer
        /// </summary>
        public int Position
        {
            get { return _position; }
            set
            {
                if (value < 0 || value > _length)
                {
                    throw new Exception("Invalid position");
                }
                _position = value;
            }
        }

        /// <summary>
        /// Get the buffer
        /// </summary>
        public byte[] Buffer
        {
            get { return _buffer; }
        }

        /// <summary>
        /// Get the remaining bytes in the buffer
        /// </summary>
        public byte[] RemainingBytes
        {
            get
            {
                int length = _length - _position;
                byte[] value = new byte[length];
                Array.Copy(_buffer, _position, value, 0, length);
                return value;
            }
        }

        /// <summary>
        /// Get the current Read/Write serialization mode
        /// </summary>
        public NetSquareSerializationMode SerializationMode
        {
            get { return _serializationMode; }
        }

        /// <summary>
        /// Check if the buffer has write some data into buffer
        /// </summary>
        public bool HasWriteData
        {
            get { return _serializationMode == NetSquareSerializationMode.Write && _position > 0; }
        }
    }
}