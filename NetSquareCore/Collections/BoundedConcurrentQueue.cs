using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NetSquare.Core.Collections
{
    /// <summary>
    /// Provides a bounded multi-producer, single-consumer queue with producer backpressure.
    /// </summary>
    public sealed class BoundedConcurrentQueue<T>
    {
        #region Fields
        private readonly ConcurrentQueue<T> queue = new ConcurrentQueue<T>();
        private readonly SemaphoreSlim availableItems = new SemaphoreSlim(0);
        private readonly SemaphoreSlim availableSlots;
        private readonly CancellationTokenSource addingCancellation = new CancellationTokenSource();
        private int accepting = 1;
        private int count;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the maximum number of items retained by the queue.
        /// </summary>
        public int Capacity { get; private set; }

        /// <summary>
        /// Gets the number of items currently retained by the queue.
        /// </summary>
        public int Count { get { return Volatile.Read(ref count); } }

        /// <summary>
        /// Gets whether producers are no longer accepted and all retained items were consumed.
        /// </summary>
        public bool IsCompleted
        {
            get { return Volatile.Read(ref accepting) == 0 && Volatile.Read(ref count) == 0; }
        }
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a bounded concurrent queue.
        /// </summary>
        /// <param name="capacity">Maximum number of retained items.</param>
        public BoundedConcurrentQueue(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            Capacity = capacity;
            availableSlots = new SemaphoreSlim(capacity, capacity);
        }
        #endregion

        #region Queue operations
        /// <summary>
        /// Enqueues an item, applying backpressure while the queue is full.
        /// </summary>
        /// <param name="item">Item to enqueue.</param>
        /// <returns>True when accepted, or false after adding was completed.</returns>
        public bool Enqueue(T item)
        {
            if (Volatile.Read(ref accepting) == 0)
                return false;

            try
            {
                // The slot semaphore bounds memory and naturally slows network producers under load.
                availableSlots.Wait(addingCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (Volatile.Read(ref accepting) == 0)
            {
                availableSlots.Release();
                return false;
            }

            queue.Enqueue(item);
            Interlocked.Increment(ref count);
            availableItems.Release();
            return true;
        }

        /// <summary>
        /// Waits for and dequeues one item.
        /// </summary>
        /// <param name="item">Dequeued item when available.</param>
        /// <param name="cancellationToken">Consumer cancellation token.</param>
        /// <returns>True when an item was dequeued, or false after completion.</returns>
        public bool TryDequeue(out T item, CancellationToken cancellationToken)
        {
            item = default(T);
            while (true)
            {
                if (IsCompleted)
                    return false;

                availableItems.Wait(cancellationToken);
                if (!queue.TryDequeue(out item))
                {
                    if (IsCompleted)
                        return false;
                    continue;
                }

                Interlocked.Decrement(ref count);
                availableSlots.Release();
                return true;
            }
        }

        /// <summary>
        /// Stops accepting producers and wakes the consumer so retained items can be drained.
        /// </summary>
        public void CompleteAdding()
        {
            if (Interlocked.Exchange(ref accepting, 0) == 0)
                return;

            // Cancellation releases producers blocked by backpressure during shutdown.
            addingCancellation.Cancel();
            availableItems.Release();
        }
        #endregion
    }
}
