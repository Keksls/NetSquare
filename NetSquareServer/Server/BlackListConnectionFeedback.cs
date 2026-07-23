using NetSquare.Core;
using System;

namespace NetSquare.Server
{
    /// <summary>
    /// Converts blacklist state into the generic NetSquare connection feedback contracts.
    /// </summary>
    internal static class BlackListConnectionFeedback
    {
        /// <summary>
        /// Creates a connection rejection matching a temporary, permanent, or external blacklist entry.
        /// </summary>
        /// <param name="status">Current blacklist status.</param>
        /// <returns>The typed rejection sent before closing the socket.</returns>
        public static ConnectionRejectionInfo CreateRejection(BlackListStatus status)
        {
            if (status == null)
                return new ConnectionRejectionInfo(ConnectionRejectionReason.BannedPermanent);

            ConnectionRejectionReason reason = status.BanType == BlackListBanType.Temporary
                ? ConnectionRejectionReason.BannedTemporary
                : ConnectionRejectionReason.BannedPermanent;
            return new ConnectionRejectionInfo(reason, status.Reason, status.BanExpiresUtc);
        }

        /// <summary>
        /// Creates a disconnection matching an active blacklist ban.
        /// </summary>
        /// <param name="banType">Active ban type.</param>
        /// <param name="expiresUtc">Optional temporary ban expiration.</param>
        /// <param name="message">Optional ban details.</param>
        /// <returns>The typed disconnection information.</returns>
        public static DisconnectInfo CreateDisconnection(
            BlackListBanType banType,
            DateTime? expiresUtc,
            string message)
        {
            DisconnectReason reason = banType == BlackListBanType.Temporary
                ? DisconnectReason.BannedTemporary
                : DisconnectReason.BannedPermanent;
            return new DisconnectInfo(reason, message, expiresUtc);
        }
    }
}
