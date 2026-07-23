using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NetSquare.Server
{
    /// <summary>
    /// Evaluates addresses against the free Spamhaus DROP IPv4 and IPv6 datasets.
    /// </summary>
    internal sealed class SpamhausDropReputationProvider : RemoteIPListReputationProvider
    {
        private static readonly Regex CidrPattern = new Regex(
            "\\\"cidr\\\"\\s*:\\s*\\\"(?<cidr>[^\\\"]+)\\\"",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public override string Name { get { return "Spamhaus DROP"; } }

        /// <summary>
        /// Initializes the Spamhaus DROP provider.
        /// </summary>
        /// <param name="configuration">Active server configuration.</param>
        public SpamhausDropReputationProvider(NetSquareConfiguration configuration)
            : base(
                new[]
                {
                    "https://www.spamhaus.org/drop/drop_v4.json",
                    "https://www.spamhaus.org/drop/drop_v6.json"
                },
                TimeSpan.FromHours(Math.Max(24, configuration.SpamhausDropRefreshHours)),
                configuration.BlackListExternalRequestTimeoutMilliseconds)
        {
        }

        /// <summary>
        /// Extracts CIDR values from the Spamhaus JSON-lines document.
        /// </summary>
        /// <param name="content">Spamhaus DROP document.</param>
        /// <returns>Extracted CIDR values.</returns>
        protected override IEnumerable<string> ExtractNetworks(string content)
        {
            List<string> values = new List<string>();
            foreach (Match match in CidrPattern.Matches(content))
                values.Add(match.Groups["cidr"].Value);

            return values;
        }
    }
}
