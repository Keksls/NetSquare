using System;
using System.Text.RegularExpressions;

namespace NetSquare.Server
{
    /// <summary>
    /// Evaluates IP addresses through the public BlockList.de lookup endpoint.
    /// </summary>
    internal sealed class BlockListDeReputationProvider : IIPReputationProvider
    {
        private static readonly Regex ResponsePattern = new Regex(
            "attacks\\s*:\\s*(?<attacks>[0-9]+)\\s*<br\\s*/?>\\s*reports\\s*:\\s*(?<reports>[0-9]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly int minimumAttacks;
        private readonly int minimumReports;
        private readonly int timeoutMilliseconds;

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public string Name { get { return "BlockList.de"; } }

        /// <summary>
        /// Initializes the BlockList.de provider.
        /// </summary>
        /// <param name="configuration">Active server configuration.</param>
        public BlockListDeReputationProvider(NetSquareConfiguration configuration)
        {
            minimumAttacks = Math.Max(0, configuration.BlockListDeMinimumAttacks);
            minimumReports = Math.Max(0, configuration.BlockListDeMinimumReports);
            timeoutMilliseconds = configuration.BlackListExternalRequestTimeoutMilliseconds;
        }

        /// <summary>
        /// Checks one address through the public BlockList.de endpoint.
        /// </summary>
        /// <param name="ipAddress">Canonical public IP address.</param>
        /// <returns>The BlockList.de counter decision.</returns>
        public BlackListReputationResult Check(string ipAddress)
        {
            try
            {
                string url = "https://api.blocklist.de/api.php?ip=" + Uri.EscapeDataString(ipAddress) + "&start=1";
                string response = IPReputationHttpClient.DownloadString(url, timeoutMilliseconds);
                Match match = ResponsePattern.Match(response);
                int attacks;
                int reports;
                if (!match.Success ||
                    !int.TryParse(match.Groups["attacks"].Value, out attacks) ||
                    !int.TryParse(match.Groups["reports"].Value, out reports))
                {
                    return BlackListReputationResult.Failure("BlockList.de returned an unexpected response.");
                }

                bool attackThresholdReached = minimumAttacks > 0 && attacks >= minimumAttacks;
                bool reportThresholdReached = minimumReports > 0 && reports >= minimumReports;
                return BlackListReputationResult.Success(
                    attackThresholdReached || reportThresholdReached,
                    "Attacks: " + attacks + ", reports: " + reports + ".");
            }
            catch (Exception ex)
            {
                return BlackListReputationResult.Failure(ex.Message);
            }
        }
    }
}
