using System;
using System.Runtime.Serialization;

namespace NetSquare.Server
{
    /// <summary>
    /// Represents one persisted IP ban.
    /// </summary>
    [DataContract]
    internal sealed class BlackListBanEntry
    {
        /// <summary>
        /// Gets or sets the canonical IP address.
        /// </summary>
        [DataMember(Order = 1)]
        public string IPAddress { get; set; }

        /// <summary>
        /// Gets or sets the ban type.
        /// </summary>
        [DataMember(Order = 2)]
        public BlackListBanType BanType { get; set; }

        /// <summary>
        /// Gets or sets when a temporary ban expires.
        /// </summary>
        [DataMember(Order = 3, EmitDefaultValue = false)]
        public DateTime? ExpiresUtc { get; set; }

        /// <summary>
        /// Gets or sets when the ban was created.
        /// </summary>
        [DataMember(Order = 4)]
        public DateTime CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the reason supplied by the caller.
        /// </summary>
        [DataMember(Order = 5, EmitDefaultValue = false)]
        public string Reason { get; set; }

        /// <summary>
        /// Gets or sets the component that created the ban.
        /// </summary>
        [DataMember(Order = 6, EmitDefaultValue = false)]
        public string Source { get; set; }
    }
}
