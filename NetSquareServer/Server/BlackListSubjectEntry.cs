using System;
using System.Runtime.Serialization;

namespace NetSquare.Server
{
    /// <summary>
    /// Stores the persistent escalation, hit progress and active ban state of one generic subject.
    /// </summary>
    [DataContract]
    internal sealed class BlackListSubjectEntry
    {
        [DataMember(Order = 1)]
        public string SubjectType { get; set; }

        [DataMember(Order = 2)]
        public string SubjectIdentifier { get; set; }

        [DataMember(Order = 3)]
        public string PolicyName { get; set; }

        [DataMember(Order = 4)]
        public int EscalationLevel { get; set; }

        [DataMember(Order = 5)]
        public int HitCount { get; set; }

        [DataMember(Order = 6, EmitDefaultValue = false)]
        public DateTime? HitWindowExpiresUtc { get; set; }

        [DataMember(Order = 7, EmitDefaultValue = false)]
        public DateTime? LastIncidentUtc { get; set; }

        [DataMember(Order = 8, EmitDefaultValue = false)]
        public BlackListBanType? BanType { get; set; }

        [DataMember(Order = 9, EmitDefaultValue = false)]
        public DateTime? BanExpiresUtc { get; set; }

        [DataMember(Order = 10, EmitDefaultValue = false)]
        public DateTime? BanCreatedUtc { get; set; }

        [DataMember(Order = 11, EmitDefaultValue = false)]
        public string Reason { get; set; }

        [DataMember(Order = 12, EmitDefaultValue = false)]
        public string Source { get; set; }
    }
}