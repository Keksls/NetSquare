using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Describes why a connection was rejected and any associated ban expiration.
    /// </summary>
    public sealed class ConnectionRejectionInfo
    {
        public ConnectionRejectionReason Reason { get; private set; }
        public string Message { get; private set; }
        public DateTime? ExpiresUtc { get; private set; }
        public bool IsBanned { get { return Reason == ConnectionRejectionReason.BannedTemporary || Reason == ConnectionRejectionReason.BannedPermanent; } }
        public bool IsTemporaryBan { get { return Reason == ConnectionRejectionReason.BannedTemporary; } }
        public bool IsPermanentBan { get { return Reason == ConnectionRejectionReason.BannedPermanent; } }

        /// <summary>
        /// Creates connection rejection information.
        /// </summary>
        /// <param name="reason">Reason for refusing the connection.</param>
        /// <param name="message">Optional human-readable details.</param>
        /// <param name="expiresUtc">Optional UTC expiration for a temporary ban.</param>
        public ConnectionRejectionInfo(ConnectionRejectionReason reason, string message = null, DateTime? expiresUtc = null)
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
