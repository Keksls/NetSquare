using System.Collections.Generic;

namespace NetSquare.Server
{
    /// <summary>
    /// Defines the escalation rules applied to a group of blacklist subjects.
    /// </summary>
    public sealed class BlackListPolicy
    {
        public string Name { get; set; }
        public int HitWindowSeconds { get; set; }
        public int EscalationResetAfterSeconds { get; set; }
        public List<BlackListEscalationStage> Stages { get; set; }

        /// <summary>
        /// Initializes an empty policy that can be populated from configuration.
        /// </summary>
        public BlackListPolicy()
        {
            Stages = new List<BlackListEscalationStage>();
        }
    }
}
