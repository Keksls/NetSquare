using System;
using System.Collections.Generic;
using System.Net;

namespace NetSquare.Server
{
    /// <summary>
    /// Provides cached CIDR list downloads for reputation providers.
    /// </summary>
    internal abstract class RemoteIPListReputationProvider : IIPReputationProvider
    {
        private readonly object synchronizationLock = new object();
        private readonly string[] urls;
        private readonly TimeSpan refreshInterval;
        private readonly int timeoutMilliseconds;
        private List<IPNetworkRange> networks = new List<IPNetworkRange>();
        private DateTime nextRefreshUtc = DateTime.MinValue;

        /// <summary>
        /// Gets the provider name.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Initializes a remote CIDR list provider.
        /// </summary>
        /// <param name="urls">List download URLs.</param>
        /// <param name="refreshInterval">Minimum refresh interval.</param>
        /// <param name="timeoutMilliseconds">HTTP request timeout.</param>
        protected RemoteIPListReputationProvider(string[] urls, TimeSpan refreshInterval, int timeoutMilliseconds)
        {
            this.urls = urls;
            this.refreshInterval = refreshInterval;
            this.timeoutMilliseconds = timeoutMilliseconds;
        }

        /// <summary>
        /// Evaluates an IP address against the cached CIDR list.
        /// </summary>
        /// <param name="ipAddress">Canonical public IP address.</param>
        /// <returns>The list membership result.</returns>
        public BlackListReputationResult Check(string ipAddress)
        {
            try
            {
                List<IPNetworkRange> snapshot = GetNetworkSnapshot();
                IPAddress parsedAddress;
                if (!IPAddress.TryParse(ipAddress, out parsedAddress))
                    return BlackListReputationResult.Failure("The IP address is invalid.");

                foreach (IPNetworkRange network in snapshot)
                {
                    if (network.Contains(parsedAddress))
                        return BlackListReputationResult.Success(true, "The address belongs to a listed network.");
                }

                return BlackListReputationResult.Success(false, "The address is not in the downloaded list.");
            }
            catch (Exception ex)
            {
                return BlackListReputationResult.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Extracts CIDR values from one downloaded source document.
        /// </summary>
        /// <param name="content">Downloaded source content.</param>
        /// <returns>Extracted CIDR values.</returns>
        protected abstract IEnumerable<string> ExtractNetworks(string content);

        /// <summary>
        /// Returns the current immutable list snapshot and refreshes it when due.
        /// </summary>
        /// <returns>Current network list snapshot.</returns>
        private List<IPNetworkRange> GetNetworkSnapshot()
        {
            lock (synchronizationLock)
            {
                if (DateTime.UtcNow < nextRefreshUtc && networks.Count > 0)
                    return networks;

                List<IPNetworkRange> refreshedNetworks = new List<IPNetworkRange>();
                foreach (string url in urls)
                {
                    string content = IPReputationHttpClient.DownloadString(url, timeoutMilliseconds);
                    foreach (string networkValue in ExtractNetworks(content))
                    {
                        IPNetworkRange network;
                        if (IPNetworkRange.TryParse(networkValue, out network))
                            refreshedNetworks.Add(network);
                    }
                }

                if (refreshedNetworks.Count == 0)
                    throw new InvalidOperationException(Name + " returned no valid network.");

                // Replace the complete snapshot only after every source was parsed successfully.
                networks = refreshedNetworks;
                nextRefreshUtc = DateTime.UtcNow.Add(refreshInterval);
                return networks;
            }
        }
    }
}
