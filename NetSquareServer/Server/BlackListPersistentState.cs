using System.Collections.Generic;
using System.Runtime.Serialization;

namespace NetSquare.Server
{
    /// <summary>
    /// Represents the persisted blacklist document and its legacy migration payload.
    /// </summary>
    [DataContract]
    internal sealed class BlackListPersistentState
    {
        /// <summary>
        /// Gets or sets bans stored by the previous IP-only format.
        /// </summary>
        [DataMember(Order = 1, EmitDefaultValue = false)]
        public List<BlackListBanEntry> Bans { get; set; }

        /// <summary>
        /// Gets or sets generic subject states written by the current format.
        /// </summary>
        [DataMember(Order = 2)]
        public List<BlackListSubjectEntry> Subjects { get; set; }

        /// <summary>
        /// Initializes an empty persisted blacklist document.
        /// </summary>
        public BlackListPersistentState()
        {
            // New snapshots only write subjects; Bans remains available to read and migrate old files.
            Subjects = new List<BlackListSubjectEntry>();
        }
    }
}