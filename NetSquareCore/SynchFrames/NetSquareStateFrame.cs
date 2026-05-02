#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Represents the net square state frame value.
    /// </summary>
    public struct NetSquareStateFrame : INetSquareSynchFrame
    {
        /// <summary>
        /// Stores the time value.
        /// </summary>
        private float time;
        /// <summary>
        /// Gets or sets the time value.
        /// </summary>
        public float Time { get => time; set => time = value; }
        /// <summary>
        /// Stores the sequence id value.
        /// </summary>
        private uint sequenceID;
        /// <summary>
        /// Gets or sets the sequence id value.
        /// </summary>
        public uint SequenceID { get => sequenceID; set => sequenceID = value; }
        /// <summary>
        /// Stores the synch frame type value.
        /// </summary>
        private byte synchFrameType;
        /// <summary>
        /// Gets or sets the synch frame type value.
        /// </summary>
        public byte SynchFrameType { get => synchFrameType; set => synchFrameType = value; }
        int INetSquareSynchFrame.Size => NetSquareStateFrame.Size;
        /// <summary>
        /// Stores the states value.
        /// </summary>
        public int States;
        /// <summary>
        /// Stores the size value.
        /// </summary>
        public const int Size = 13;

        /// <summary>
        /// Create a new state frame
        /// </summary>
        /// <param name="time"> time of the frame</param>
        /// <param name="states"> states of the frame</param>
        public NetSquareStateFrame(float time, int states, uint sequenceID = 0)
        {
            this.time = time;
            this.sequenceID = sequenceID;
            synchFrameType = 1;
            States = states;
        }

        /// <summary>
        /// Create a new state frame from a byte array using pointer
        /// </summary>
        /// <param name="ptr"> pointer to the byte array</param>
        public unsafe NetSquareStateFrame(ref byte* ptr)
        {
            time = 0;
            sequenceID = 0;
            synchFrameType = 0;
            States = 0;
            Deserialize(ref ptr);
        }

        /// <summary>
        /// Create a new state frame from a network message
        /// </summary>
        /// <param name="message"> message to create the state frame</param>
        public unsafe NetSquareStateFrame(NetworkMessage message)
        {
            States = 0;
            time = 0f;
            sequenceID = 0;
            synchFrameType = 0;

            // ensure we have enough data to read
            if (!message.Serializer.CanReadFor(Size))
            {
                return;
            }

            // get a pointer to the message data
            fixed (byte* ptr = message.Serializer.Buffer)
            {
                byte* b = ptr;
                b += message.Serializer.Position;
                Deserialize(ref b);
            }
            // move the reading index of the message
            message.Serializer.DummyRead(Size);
        }

        /// <summary>
        /// Serialize the state frame to a network message
        /// </summary>
        /// <param name="message"> message to serialize the state frame</param>
        public unsafe void Serialize(NetworkMessage message)
        {
            byte[] bytes = new byte[Size];
            // write transform values using pointer
            fixed (byte* ptr = bytes)
            {
                byte* b = ptr;
                Serialize(ref b);
            }
            // set the message data
            message.Set(bytes, false);
        }

        /// <summary>
        /// Deserialize the state frame from a byte array using pointer
        /// </summary>
        /// <param name="message"> message to deserialize the state frame</param>
        public unsafe void Deserialize(NetworkMessage message)
        {
            if (message.Serializer.CanReadFor(Size))
            {
                // write transform values using pointer
                fixed (byte* ptr = message.Serializer.Buffer)
                {
                    byte* b = ptr;
                    b += message.Serializer.Position;
                    Deserialize(ref b);
                }
                message.Serializer.DummyRead(Size);
            }
        }

        /// <summary>
        /// Deserialize the state frame from a byte array using pointer
        /// </summary>
        /// <param name="ptr"> pointer to the byte array</param>
        public unsafe void Deserialize(ref byte* ptr)
        {
            // read frame type using pointer
            synchFrameType = *ptr;
            ptr++;

            uint* u = (uint*)ptr;
            sequenceID = *u;
            u++;

            // read transform values using pointer
            float* f = (float*)u;
            time = *f;
            f++;

            // read states using pointer
            int* i = (int*)f;
            States = *i;
            i++;

            ptr = (byte*)i;
        }

        /// <summary>
        /// Serialize the state frame to a byte array using pointer
        /// </summary>
        /// <param name="ptr"> pointer to the byte array</param>
        public unsafe void Serialize(ref byte* ptr)
        {
            // write frame type using pointer
            *ptr = synchFrameType;
            ptr++;

            uint* u = (uint*)ptr;
            *u = SequenceID;
            u++;

            // write transform values using pointer
            float* f = (float*)u;
            *f = Time;
            f++;

            // write states using pointer
            int* i = (int*)f;
            *i = States;
            i++;

            ptr = (byte*)i;
        }
    }
}
#endregion
