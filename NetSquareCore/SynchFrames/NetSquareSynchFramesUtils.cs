using System;
using System.Collections.Generic;

namespace NetSquare.Core
{
    /// <summary>
    /// Utils class to serialize and deserialize frames
    /// </summary>
    public static class NetSquareSynchFramesUtils
    {
        private static Dictionary<byte, Func<NetworkMessage, INetSquareSynchFrame>> customDeserializers = new Dictionary<byte, Func<NetworkMessage, INetSquareSynchFrame>>();
        private static Dictionary<byte, int> customSized = new Dictionary<byte, int>();

        /// <summary>
        /// Static Utils constructor
        /// </summary>
        static NetSquareSynchFramesUtils()
        {
            customDeserializers = new Dictionary<byte, Func<NetworkMessage, INetSquareSynchFrame>>();
            customSized = new Dictionary<byte, int>();
        }

        /// <summary>
        /// Get the frames from a network message
        /// </summary>
        /// <param name="message"> message to get the frames</param>
        /// <returns> array of frames</returns>
        /// <exception cref="Exception"> if the frame type is unknown</exception>
        public unsafe static INetSquareSynchFrame[] GetFrames(NetworkMessage message)
        {
            ushort nbFrames = message.Serializer.GetUShort();
            INetSquareSynchFrame[] frames = new INetSquareSynchFrame[nbFrames];

            fixed (byte* ptr = message.Serializer.Buffer)
            {
                byte* b = ptr;
                b += message.Serializer.Position;
                int readingIndex = 0;

                for (int i = 0; i < nbFrames; i++)
                {
                    byte frameType = *b; // do NOT increment b here because we need to read the frame type again in Deserialize
                    readingIndex = 0;
                    switch (frameType)
                    {
                        // trasform frame
                        case 0:
                            frames[i] = new NetsquareTransformFrame();
                            frames[i].Deserialize(ref b);
                            readingIndex += NetsquareTransformFrame.Size;
                            break;

                        // states frame
                        case 1:
                            frames[i] = new NetSquareStateFrame();
                            frames[i].Deserialize(ref b);
                            readingIndex += NetSquareStateFrame.Size;
                            break;

                        // custom frame
                        default:
                            if (customDeserializers.ContainsKey(frameType))
                            {
                                frames[i] = customDeserializers[frameType](message);
                                readingIndex += customSized[frameType];
                            }
                            else
                            {
                                throw new Exception("Unknown frame type (" + frameType + ")");
                            }
                            break;
                    }
                    message.Serializer.DummyRead(readingIndex);
                }
            }

            return frames;
        }

        /// <summary>
        /// Get the frame from a network message
        /// </summary>
        /// <param name="message"> message to get the frame</param>
        /// <returns> frame</returns>
        /// <exception cref="Exception"> if the frame type is unknown</exception>
        public unsafe static INetSquareSynchFrame GetFrame(NetworkMessage message)
        {
            fixed (byte* ptr = message.Serializer.Buffer)
            {
                byte* b = ptr;
                b += message.Serializer.Position;
                byte frameType = *b; // do NOT increment b here because we need to read the frame type again in Deserialize
                switch (frameType)
                {
                    // trasform frame
                    case 0:
                        message.Serializer.DummyRead(NetsquareTransformFrame.Size);
                        return new NetsquareTransformFrame(ref b);
                    // states frame
                    case 1:
                        message.Serializer.DummyRead(NetSquareStateFrame.Size);
                        return new NetSquareStateFrame(ref b);
                    // custom frame
                    default:
                        if (customDeserializers.ContainsKey(frameType))
                        {
                            message.Serializer.DummyRead(customSized[frameType]);
                            return customDeserializers[frameType](message);
                        }
                        else
                        {
                            throw new Exception("Unknown frame type (" + frameType + ")");
                        }
                }
            }
        }

        /// <summary>
        /// Get the packed frames from a network message using pointer
        /// </summary>
        /// <param name="message"> message to get the packed frames</param>
        /// <param name="onGetFrames"> callback to call when the packed frames are read</param>
        /// <exception cref="Exception"> if the frame type is unknown</exception>
        public unsafe static void GetPackedFrames(NetworkMessage message, Action<uint, INetSquareSynchFrame[]> onGetFrames)
        {
            message.RestartRead();
            fixed (byte* ptr = message.Serializer.Buffer)
            {
                byte* b = ptr + message.Serializer.Position;
                while (message.Serializer.CanGetUInt24())
                {
                    uint clientID = (uint)(*b | (*(b + 1) << 8) | (*(b + 2) << 16));
                    b += 3;
                    ushort nbFrames = *(ushort*)(b);
                    b += 2;
                    int readingIndex = 5;
                    INetSquareSynchFrame[] frames = new INetSquareSynchFrame[nbFrames];
                    for (int i = 0; i < nbFrames; i++)
                    {
                        byte frameType = *b; // do NOT increment b here because we need to read the frame type again in Deserialize
                        switch (frameType)
                        {
                            // trasform frame
                            case 0:
                                frames[i] = new NetsquareTransformFrame(ref b);
                                readingIndex += NetsquareTransformFrame.Size;
                                break;

                            // states frame
                            case 1:
                                frames[i] = new NetSquareStateFrame(ref b);
                                readingIndex += NetSquareStateFrame.Size;
                                break;

                            // custom frame
                            default:
                                if (customDeserializers.ContainsKey(frameType))
                                {
                                    frames[i] = customDeserializers[frameType](message);
                                    readingIndex += customSized[frameType];
                                }
                                else
                                {
                                    throw new Exception("Unknown frame type (" + frameType + ")");
                                }
                                break;
                        }
                    }
                    onGetFrames(clientID, frames);
                    message.Serializer.DummyRead(readingIndex);
                }
            }
        }

        /// <summary>
        /// Serialize the frames to a byte array using pointer
        /// </summary>
        /// <param name="message"> message to serialize the frames</param>
        /// <param name="frames"> frames to serialize</param>
        public unsafe static void SerializeFrames(NetworkMessage message, List<INetSquareSynchFrame> frames)
        {
            ushort nbFrames = (ushort)frames.Count;

            // calculate the size of the byte array to allocate
            int size = 2;
            for (ushort i = 0; i < nbFrames; i++)
            {
                size += frames[i].Size;
            }

            byte[] bytes = new byte[size];
            // write transform values using pointer
            fixed (byte* ptr = bytes)
            {
                byte* b = ptr;
                // write frames count
                *b = (byte)nbFrames;
                b++;
                *b = (byte)(nbFrames >> 8);
                b++;
                // iterate on each frames of the client to pack them
                for (ushort i = 0; i < nbFrames; i++)
                {
                    frames[i].Serialize(ref b);
                }
            }
            message.Set(bytes, false);
        }

        /// <summary>
        /// Serialize the packed frames to a byte array using pointer
        /// Add client id to the byte array
        /// Used by the server to send packed frames to a client
        /// </summary>
        /// <param name="message"> message to serialize the packed frames</param>
        /// <param name="clientID"> client id to add to the byte array</param>
        /// <param name="frames"> frames to pack</param>
        public unsafe static void SerializePackedFrames(NetworkMessage message, uint clientID, List<INetSquareSynchFrame> frames)
        {
            // create new byte array to pack transform frames for this client
            UInt24 clientId = new UInt24(clientID);
            ushort nbFrames = (ushort)frames.Count;

            // calculate the size of the byte array to allocate
            int size = 5;
            for (ushort i = 0; i < nbFrames; i++)
            {
                size += frames[i].Size;
            }

            // allocate the byte array
            byte[] bytes = new byte[size];
            // write transform values using pointer 
            fixed (byte* ptr = bytes)
            {
                byte* b = ptr;
                // write client id
                *b = clientId.b0;
                b++;
                *b = clientId.b1;
                b++;
                *b = clientId.b2;
                b++;
                // write frames count
                *b = (byte)nbFrames;
                b++;
                *b = (byte)(nbFrames >> 8);
                b++;

                // iterate on each frames of the client to pack them
                for (ushort i = 0; i < nbFrames; i++)
                {
                    frames[i].Serialize(ref b);
                }
            }

            // set the byte array to the message
            message.Set(bytes, false);
        }

        /// <summary>
        /// Register a custom deserializer for a frame type
        /// </summary>
        /// <param name="frameType"> frame type to register the deserializer</param>
        /// <param name="frameSize"> frame size to register the deserializer</param>
        /// <param name="deserializer"> deserializer callback to register</param>
        public static void RegisterCustomDeserializer(byte frameType, int frameSize, Func<NetworkMessage, INetSquareSynchFrame> deserializer)
        {
            customDeserializers[frameType] = deserializer;
            customSized[frameType] = frameSize;
        }

        /// <summary>
        /// Try to get the most recent transform frame from an array of frames
        /// </summary>
        /// <param name="frames"> array of frames</param>
        /// <param name="transformFrame"> most recent transform frame</param>
        /// <returns> true if the most recent transform frame is found, false otherwise</returns>
        public static bool TryGetMostRecentTransformFrame(INetSquareSynchFrame[] frames, out NetsquareTransformFrame transformFrame)
        {
            transformFrame = default;
            for (int i = frames.Length - 1; i >= 0; i--)
            {
                switch (frames[i].SynchFrameType)
                {
                    case 0:
                        transformFrame = (NetsquareTransformFrame)frames[i];
                        return true;
                }
            }
            return false;
        }
    }
}