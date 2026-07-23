using System;
using System.Threading;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Provides a bounded lock-free multi-producer, single-consumer log queue.
    /// </summary>
    internal sealed class WriterLogRingBuffer
    {
        private readonly WriterLogSlot[] slots;
        private readonly int indexMask;
        private readonly int capacity;
        private long enqueuePosition;
        private long dequeuePosition;
        private int count;

        internal int Capacity => capacity;
        internal int Count => Volatile.Read(ref count);

        /// <summary>
        /// Initializes every slot sequence of a power-of-two ring buffer.
        /// </summary>
        internal WriterLogRingBuffer(int capacity)
        {
            // Power-of-two capacity replaces the modulo operation with a bit mask.
            if (capacity < 2 || (capacity & (capacity - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "The ring capacity must be a power of two.");

            this.capacity = capacity;
            indexMask = capacity - 1;
            slots = new WriterLogSlot[capacity];
            for (int index = 0; index < capacity; index++)
                slots[index].Sequence = index;
        }

        /// <summary>
        /// Attempts to enqueue an entry without blocking or allocating.
        /// </summary>
        internal bool TryEnqueue(in NetSquareLogEntry entry)
        {
            // Sequence numbers distinguish a free slot from a slot awaiting consumption.
            while (true)
            {
                long position = Volatile.Read(ref enqueuePosition);
                int slotIndex = (int)(position & indexMask);
                ref WriterLogSlot slot = ref slots[slotIndex];
                long sequence = Volatile.Read(ref slot.Sequence);
                long difference = sequence - position;

                if (difference == 0)
                {
                    if (Interlocked.CompareExchange(ref enqueuePosition, position + 1, position) != position)
                        continue;

                    slot.Entry = entry;
                    Interlocked.Increment(ref count);
                    Volatile.Write(ref slot.Sequence, position + 1);
                    return true;
                }

                if (difference < 0)
                    return false;
            }
        }

        /// <summary>
        /// Attempts to dequeue the next committed entry on the single consumer.
        /// </summary>
        internal bool TryDequeue(out NetSquareLogEntry entry)
        {
            // Only the Writer worker updates dequeuePosition, so no consumer CAS is required.
            long position = dequeuePosition;
            int slotIndex = (int)(position & indexMask);
            ref WriterLogSlot slot = ref slots[slotIndex];
            long sequence = Volatile.Read(ref slot.Sequence);
            if (sequence - (position + 1) != 0)
            {
                entry = default(NetSquareLogEntry);
                return false;
            }

            entry = slot.Entry;
            slot.Entry = default(NetSquareLogEntry);
            dequeuePosition = position + 1;
            Interlocked.Decrement(ref count);
            Volatile.Write(ref slot.Sequence, position + capacity);
            return true;
        }
    }
}
