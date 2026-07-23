using System;
using System.Collections.Generic;
using System.IO;

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Utils class to serialize and deserialize frames
    /// </summary>
    public static class NetSquareSynchFramesUtils
    {
        /// <summary>
        /// Stores the custom deserializers value.
        /// </summary>
        private static Dictionary<byte, Func<NetworkMessage, INetSquareSynchFrame>> customDeserializers = new Dictionary<byte, Func<NetworkMessage, INetSquareSynchFrame>>();
        /// <summary>
        /// Stores the custom sized value.
        /// </summary>
        private static Dictionary<byte, int> customSized = new Dictionary<byte, int>();
        /// <summary>
        /// Serializes registration and lookup of custom frame metadata.
        /// </summary>
        private static readonly object customFrameLock = new object();

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
        public static INetSquareSynchFrame[] GetFrames(NetworkMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            ushort nbFrames = message.Serializer.GetUShort();
            ValidateFrameCount(message.Serializer, nbFrames);
            INetSquareSynchFrame[] frames = new INetSquareSynchFrame[nbFrames];
            for (int i = 0; i < nbFrames; i++)
                frames[i] = DeserializeFrame(message);
            return frames;
        }

        /// <summary>
        /// Get the frame from a network message
        /// </summary>
        /// <param name="message"> message to get the frame</param>
        /// <returns> frame</returns>
        /// <exception cref="Exception"> if the frame type is unknown</exception>
        public static INetSquareSynchFrame GetFrame(NetworkMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            return DeserializeFrame(message);
        }

        /// <summary>
        /// Get the packed frames from a network message using pointer
        /// </summary>
        /// <param name="message"> message to get the packed frames</param>
        /// <param name="onGetFrames"> callback to call when the packed frames are read</param>
        /// <exception cref="Exception"> if the frame type is unknown</exception>
        public static void GetPackedFrames(NetworkMessage message, Action<uint, INetSquareSynchFrame[]> onGetFrames)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (onGetFrames == null)
                throw new ArgumentNullException(nameof(onGetFrames));

            message.RestartRead();
            while (!message.Serializer.EndOfStream)
            {
                if (!message.Serializer.CanReadFor(6))
                    throw new InvalidDataException("A packed synchronization frame header is truncated.");

                uint clientID = message.Serializer.GetUInt();
                ushort nbFrames = message.Serializer.GetUShort();
                ValidateFrameCount(message.Serializer, nbFrames);

                INetSquareSynchFrame[] frames = new INetSquareSynchFrame[nbFrames];
                for (int i = 0; i < nbFrames; i++)
                    frames[i] = DeserializeFrame(message);
                onGetFrames(clientID, frames);
            }
        }

        /// <summary>
        /// Serialize the frames to a byte array using pointer
        /// </summary>
        /// <param name="message"> message to serialize the frames</param>
        /// <param name="frames"> frames to serialize</param>
        public unsafe static void SerializeFrames(NetworkMessage message, List<INetSquareSynchFrame> frames)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            ushort nbFrames = ValidateOutboundFrames(frames, 2, out int size);
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
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            ushort nbFrames = ValidateOutboundFrames(frames, 6, out int size);
            byte[] bytes = new byte[size];
            // write transform values using pointer 
            fixed (byte* ptr = bytes)
            {
                byte* b = ptr;
                // write client id
                *b = (byte)clientID;
                b++;
                *b = (byte)(clientID >> 8);
                b++;
                *b = (byte)(clientID >> 16);
                b++;
                *b = (byte)(clientID >> 24);
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
            if (frameType <= 1)
                throw new ArgumentOutOfRangeException(nameof(frameType), "Built-in frame types cannot be replaced.");
            if (frameSize <= 0 || frameSize > NetworkMessage.MaxDecodedMessageSize)
                throw new ArgumentOutOfRangeException(nameof(frameSize));
            if (deserializer == null)
                throw new ArgumentNullException(nameof(deserializer));

            lock (customFrameLock)
            {
                customDeserializers[frameType] = deserializer;
                customSized[frameType] = frameSize;
            }
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
            if (frames == null)
                return false;

            for (int i = frames.Length - 1; i >= 0; i--)
            {
                INetSquareSynchFrame frame = frames[i];
                if (frame != null && frame.SynchFrameType == 0)
                {
                    transformFrame = (NetsquareTransformFrame)frame;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Validates a network-provided frame count before allocating its result array.
        /// </summary>
        /// <param name="serializer">Serializer positioned at the first frame.</param>
        /// <param name="frameCount">Number of frames declared by the payload.</param>
        private static void ValidateFrameCount(NetSquareSerializer serializer, ushort frameCount)
        {
            int maximumFrames = NetSquareSerializer.MaxCollectionLength;
            if (maximumFrames < 0 || frameCount > maximumFrames)
                throw new InvalidDataException("The synchronization frame count exceeds the configured limit.");
            if (!serializer.CanReadFor(frameCount))
                throw new InvalidDataException("The synchronization frame payload is truncated.");
        }

        /// <summary>
        /// Deserializes one frame only after its complete fixed-size payload is available.
        /// </summary>
        /// <param name="message">Message positioned at the frame type byte.</param>
        /// <returns>The validated frame.</returns>
        private unsafe static INetSquareSynchFrame DeserializeFrame(NetworkMessage message)
        {
            NetSquareSerializer serializer = message.Serializer;
            if (!serializer.CanGetByte())
                throw new InvalidDataException("The synchronization frame type is missing.");

            int startPosition = serializer.Position;
            byte frameType = serializer.Buffer[startPosition];
            int frameSize;
            Func<NetworkMessage, INetSquareSynchFrame> customDeserializer = null;
            switch (frameType)
            {
                case 0:
                    frameSize = NetsquareTransformFrame.Size;
                    break;
                case 1:
                    frameSize = NetSquareStateFrame.Size;
                    break;
                default:
                    lock (customFrameLock)
                    {
                        if (!customDeserializers.TryGetValue(frameType, out customDeserializer) ||
                            !customSized.TryGetValue(frameType, out frameSize))
                            throw new InvalidDataException("Unknown frame type (" + frameType + ").");
                    }
                    break;
            }

            if (frameSize <= 0 || !serializer.CanReadFor(frameSize))
                throw new InvalidDataException("The synchronization frame payload is truncated.");

            INetSquareSynchFrame frame;
            if (frameType == 0 || frameType == 1)
            {
                fixed (byte* pointer = serializer.Buffer)
                {
                    byte* framePointer = pointer + startPosition;
                    frame = frameType == 0
                        ? (INetSquareSynchFrame)new NetsquareTransformFrame(ref framePointer)
                        : new NetSquareStateFrame(ref framePointer);
                }
            }
            else
            {
                frame = customDeserializer(message);
                if (frame == null)
                    throw new InvalidDataException("The custom frame deserializer returned null.");
                if (serializer.Position > startPosition + frameSize)
                    throw new InvalidDataException("The custom frame deserializer read past its registered size.");
            }

            serializer.Position = startPosition + frameSize;
            return frame;
        }

        /// <summary>
        /// Validates outbound frames and computes their serialized size without integer overflow.
        /// </summary>
        /// <param name="frames">Frames to serialize.</param>
        /// <param name="headerSize">Serialized header size.</param>
        /// <param name="serializedSize">Computed total size.</param>
        /// <returns>The validated frame count.</returns>
        private static ushort ValidateOutboundFrames(
            List<INetSquareSynchFrame> frames,
            int headerSize,
            out int serializedSize)
        {
            if (frames == null)
                throw new ArgumentNullException(nameof(frames));
            if (frames.Count > ushort.MaxValue ||
                frames.Count > NetSquareSerializer.MaxCollectionLength)
                throw new InvalidOperationException("Too many synchronization frames.");

            int size = headerSize;
            for (int i = 0; i < frames.Count; i++)
            {
                INetSquareSynchFrame frame = frames[i];
                if (frame == null || frame.Size <= 0)
                    throw new InvalidOperationException("Synchronization frames must have a positive size.");

                size = checked(size + frame.Size);
                if (size > NetworkMessage.MaxDecodedMessageSize)
                    throw new InvalidOperationException("The synchronization frame payload is too large.");
            }

            serializedSize = size;
            return (ushort)frames.Count;
        }
    }
}
#endregion
