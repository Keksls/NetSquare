using NetSquare.Core;

namespace NetSquare.Client
{
    /// <summary>
    /// Stores one bounded reply callback and its monotonic expiration timestamp.
    /// </summary>
    internal sealed class PendingReplyCallback
    {
        /// <summary>
        /// Gets the callback invoked for the matching reply.
        /// </summary>
        public NetSquareAction Callback { get; private set; }

        /// <summary>
        /// Gets whether the callback must run on the network processing thread.
        /// </summary>
        public bool ExecuteInline { get; private set; }

        /// <summary>
        /// Gets the monotonic timestamp after which the callback is discarded.
        /// </summary>
        public long ExpirationTimestamp { get; private set; }

        /// <summary>
        /// Initializes one pending reply callback.
        /// </summary>
        /// <param name="callback">Callback invoked for the reply.</param>
        /// <param name="executeInline">Whether to execute on the network processing thread.</param>
        /// <param name="expirationTimestamp">Monotonic expiration timestamp.</param>
        public PendingReplyCallback(
            NetSquareAction callback,
            bool executeInline,
            long expirationTimestamp)
        {
            // Keep callback metadata together so registration and removal stay atomic.
            Callback = callback;
            ExecuteInline = executeInline;
            ExpirationTimestamp = expirationTimestamp;
        }

        /// <summary>
        /// Returns whether this callback has reached its expiration timestamp.
        /// </summary>
        /// <param name="currentTimestamp">Current monotonic timestamp.</param>
        /// <returns>True when the callback must no longer be invoked.</returns>
        public bool IsExpired(long currentTimestamp)
        {
            // Stopwatch timestamps avoid wall-clock changes extending callback lifetime.
            return currentTimestamp >= ExpirationTimestamp;
        }
    }
}
