using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NetSquare.Server
{
    /// <summary>
    /// Evaluates IP addresses through the official AbuseIPDB API v2.
    /// </summary>
    internal sealed class AbuseIPDBReputationProvider : IIPReputationProvider
    {
        private static readonly Regex ConfidenceScorePattern = new Regex(
            "\\\"abuseConfidenceScore\\\"\\s*:\\s*(?<score>[0-9]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string apiKey;
        private readonly int confidenceThreshold;
        private readonly int maxAgeInDays;
        private readonly int timeoutMilliseconds;
        private readonly object dailyLimitLock = new object();
        private readonly int maximumDailyChecks;
        private DateTime dailyLimitDateUtc = DateTime.MinValue;
        private int dailyChecks;


        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public string Name { get { return "AbuseIPDB"; } }

        /// <summary>
        /// Initializes the AbuseIPDB provider.
        /// </summary>
        /// <param name="configuration">Active server configuration.</param>
        public AbuseIPDBReputationProvider(NetSquareConfiguration configuration)
        {
            apiKey = configuration.AbuseIPDBApiKey;
            confidenceThreshold = Math.Max(0, Math.Min(100, configuration.AbuseIPDBConfidenceThreshold));
            maxAgeInDays = Math.Max(1, Math.Min(365, configuration.AbuseIPDBMaxAgeInDays));
            timeoutMilliseconds = configuration.BlackListExternalRequestTimeoutMilliseconds;
            maximumDailyChecks = Math.Max(0, configuration.AbuseIPDBMaximumDailyChecks);
        }

        /// <summary>
        /// Checks one address through the authenticated AbuseIPDB API v2 endpoint.
        /// </summary>
        /// <param name="ipAddress">Canonical public IP address.</param>
        /// <returns>The AbuseIPDB confidence decision.</returns>
        public BlackListReputationResult Check(string ipAddress)
        {
            if (!TryReserveDailyCheck())
                return BlackListReputationResult.Failure("The configured AbuseIPDB daily limit has been reached.");

            try
            {
                string url = "https://api.abuseipdb.com/api/v2/check?ipAddress=" +
                             Uri.EscapeDataString(ipAddress) +
                             "&maxAgeInDays=" + maxAgeInDays;
                Dictionary<string, string> headers = new Dictionary<string, string>
                {
                    { "Key", apiKey }
                };
                string response = IPReputationHttpClient.DownloadString(url, timeoutMilliseconds, headers);
                Match match = ConfidenceScorePattern.Match(response);
                int score;
                if (!match.Success || !int.TryParse(match.Groups["score"].Value, out score))
                    return BlackListReputationResult.Failure("AbuseIPDB returned no confidence score.");

                return BlackListReputationResult.Success(
                    score >= confidenceThreshold,
                    "Abuse confidence score: " + score + "/100.");
            }
            catch (Exception ex)
            {
                return BlackListReputationResult.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Reserves one request from the configured UTC daily quota.
        /// </summary>
        /// <returns>True when the request can be issued.</returns>
        private bool TryReserveDailyCheck()
        {
            lock (dailyLimitLock)
            {
                DateTime currentDateUtc = DateTime.UtcNow.Date;
                if (dailyLimitDateUtc != currentDateUtc)
                {
                    dailyLimitDateUtc = currentDateUtc;
                    dailyChecks = 0;
                }

                if (maximumDailyChecks <= 0 || dailyChecks >= maximumDailyChecks)
                    return false;

                dailyChecks++;
                return true;
            }
        }
    }
}
