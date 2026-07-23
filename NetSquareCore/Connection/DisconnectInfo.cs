using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Describes why an established connection ended and any associated ban expiration.
    /// </summary>
    public sealed class DisconnectInfo
    {
        public DisconnectReason Reason { get; private set; }
        public string Message { get; private set; }
        public DateTime? ExpiresUtc { get; private set; }
        public bool IsBanned { get { return Reason == DisconnectReason.BannedTemporary || Reason == DisconnectReason.BannedPermanent; } }
        public bool IsTemporaryBan { get { return Reason == DisconnectReason.BannedTemporary; } }
        public bool IsPermanentBan { get { return Reason == DisconnectReason.BannedPermanent; } }

        /// <summary>
        /// Creates disconnection information.
        /// </summary>
        /// <param name="reason">Reason for ending the connection.</param>
        /// <param name="message">Optional human-readable details.</param>
        /// <param name="expiresUtc">Optional UTC expiration for a temporary ban.</param>
        public DisconnectInfo(DisconnectReason reason, string message = null, DateTime? expiresUtc = null)
        {
            Reason = reason;
            Message = message ?? string.Empty;
            ExpiresUtc = NormalizeUtc(expiresUtc);
        }

        /// <summary>
        /// Normalizes an optional expiration timestamp to UTC.
        /// </summary>
        /// <param name="value">Timestamp to normalize.</param>
        /// <returns>The normalized UTC timestamp.</returns>
        private static DateTime? NormalizeUtc(DateTime? value)
        {
            if (!value.HasValue)
                return null;

            return value.Value.Kind == DateTimeKind.Utc
                ? value
                : value.Value.ToUniversalTime();
        }
    }
}
