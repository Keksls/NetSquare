using System;

namespace NetSquare.Server
{
    /// <summary>
    /// Represents the active hit window for one IP address.
    /// </summary>
    internal sealed class BlackListHitCounter
    {
        /// <summary>
        /// Gets or sets the current hit count.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets when the hit window expires.
        /// </summary>
        public DateTime ExpiresUtc { get; set; }
    }
}
