using NetSquare.Core;
using System;

namespace NetSquare.Server
{
    /// <summary>
    /// Describes the result of adding hits to a generic blacklist subject.
    /// </summary>
    public sealed class BlackListHitResult
    {
        public BlackListSubject Subject { get; internal set; }
        public string IPAddress
        {
            get
            {
                return Subject != null && Subject.Type == BlackListSubject.IPAddressType
                    ? Subject.Identifier
                    : null;
            }
        }
        public string PolicyName { get; internal set; }
        public int EscalationLevel { get; internal set; }
        public int? AppliedStageIndex { get; internal set; }
        public int HitCount { get; internal set; }
        public int HitThreshold { get; internal set; }
        public DateTime? HitWindowExpiresUtc { get; internal set; }
        public bool IsBanned { get; internal set; }
        public bool BanCreated { get; internal set; }
        public BlackListBanType? BanType { get; internal set; }
        public DateTime? BanExpiresUtc { get; internal set; }
        public string Reason { get; internal set; }
        public string Source { get; internal set; }

        /// <summary>
        /// Creates typed connection feedback matching the active ban.
        /// </summary>
        /// <returns>The disconnect information to pass to NetSquareServer.DisconnectClient.</returns>
        public DisconnectInfo CreateDisconnectInfo()
        {
            if (!IsBanned || !BanType.HasValue)
                throw new InvalidOperationException("The blacklist result does not contain an active local ban.");

            DisconnectReason reason = BanType.Value == BlackListBanType.Temporary
                ? DisconnectReason.BannedTemporary
                : DisconnectReason.BannedPermanent;
            return new DisconnectInfo(reason, Reason, BanExpiresUtc);
        }
    }
}