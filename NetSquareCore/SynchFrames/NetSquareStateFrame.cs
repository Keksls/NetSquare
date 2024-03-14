namespace NetSquare.Core
{
    public struct NetSquareStateFrame : INetSquareSynchFrame
    {
        private float time;
        public float Time { get => time; set => time = value; }
        private byte synchFrameType;
        public byte SynchFrameType { get => synchFrameType; set => synchFrameType = value; }
        int INetSquareSynchFrame.Size => NetSquareStateFrame.Size;
        public int States;
        public static int Size = 9;

        /// <summary>
        /// Create a new state frame
        /// </summary>
        /// <param name="time"> time of the frame</param>
        /// <param name="states"> states of the frame</param>
        public NetSquareStateFrame(float time, int states)
        {
            this.time = time;
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

            // read transform values using pointer
            float* f = (float*)ptr;
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

            // write transform values using pointer
            float* f = (float*)ptr;
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