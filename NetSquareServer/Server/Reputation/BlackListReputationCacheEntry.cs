using System;

namespace NetSquare.Server
{
    /// <summary>
    /// Represents one cached external reputation decision.
    /// </summary>
    internal sealed class BlackListReputationCacheEntry
    {
        /// <summary>
        /// Gets or sets whether the address is listed.
        /// </summary>
        public bool IsListed { get; set; }

        /// <summary>
        /// Gets or sets the provider that produced the decision.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the decision details.
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Gets or sets when the cached decision expires.
        /// </summary>
        public DateTime ExpiresUtc { get; set; }
    }
}
