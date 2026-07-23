using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace NetSquare.Server
{
    /// <summary>
    /// Evaluates addresses against the SANS Internet Storm Center DShield block feed.
    /// </summary>
    internal sealed class DShieldReputationProvider : RemoteIPListReputationProvider
    {
        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public override string Name { get { return "DShield"; } }

        /// <summary>
        /// Initializes the DShield provider.
        /// </summary>
        /// <param name="configuration">Active server configuration.</param>
        public DShieldReputationProvider(NetSquareConfiguration configuration)
            : base(
                new[] { "https://feeds.dshield.org/block.txt" },
                TimeSpan.FromHours(Math.Max(1, configuration.DShieldRefreshHours)),
                configuration.BlackListExternalRequestTimeoutMilliseconds)
        {
        }

        /// <summary>
        /// Extracts CIDR values from the DShield tabular feed.
        /// </summary>
        /// <param name="content">DShield feed content.</param>
        /// <returns>Extracted CIDR values.</returns>
        protected override IEnumerable<string> ExtractNetworks(string content)
        {
            List<string> values = new List<string>();
            using (StringReader reader = new StringReader(content))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    string[] columns = Regex.Split(line, "\\s+", RegexOptions.CultureInvariant);
                    int prefixLength;
                    if (columns.Length >= 3 && int.TryParse(columns[2], out prefixLength))
                        values.Add(columns[0] + "/" + prefixLength);
                }
            }

            return values;
        }
    }
}
