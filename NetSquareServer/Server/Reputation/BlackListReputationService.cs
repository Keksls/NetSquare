using NetSquare.Server.Utils;
using System;
using System.Collections.Generic;
using System.Threading;

namespace NetSquare.Server
{
    /// <summary>
    /// Coordinates cached background IP reputation evaluations.
    /// </summary>
    internal sealed class BlackListReputationService
    {
        private readonly object synchronizationLock = new object();
        private readonly List<IIPReputationProvider> providers = new List<IIPReputationProvider>();
        private readonly Dictionary<string, BlackListReputationCacheEntry> cache = new Dictionary<string, BlackListReputationCacheEntry>();
        private readonly HashSet<string> pendingAddresses = new HashSet<string>();
        private readonly Dictionary<string, DateTime> nextProviderErrorLogUtc = new Dictionary<string, DateTime>();
        private readonly TimeSpan cacheDuration;
        private readonly TimeSpan failureCacheDuration;
        private readonly int maximumCacheEntries;
        private readonly int maximumPendingChecks;

        /// <summary>
        /// Initializes the reputation service and its configured providers.
        /// </summary>
        /// <param name="configuration">Active server configuration.</param>
        public BlackListReputationService(NetSquareConfiguration configuration)
        {
            cacheDuration = TimeSpan.FromMinutes(Math.Max(1, configuration.BlackListReputationCacheMinutes));
            failureCacheDuration = TimeSpan.FromSeconds(Math.Max(5, configuration.BlackListReputationFailureCacheSeconds));
            maximumCacheEntries = Math.Max(100, configuration.BlackListMaxReputationCacheEntries);
            maximumPendingChecks = Math.Max(1, configuration.BlackListMaxPendingReputationChecks);

            if (configuration.SpamhausDropEnabled)
                providers.Add(new SpamhausDropReputationProvider(configuration));
            if (configuration.DShieldEnabled)
                providers.Add(new DShieldReputationProvider(configuration));

            if (configuration.AbuseIPDBEnabled)
            {
                if (string.IsNullOrWhiteSpace(configuration.AbuseIPDBApiKey))
                    Writer.Write("AbuseIPDB is enabled but no API key is configured.", ConsoleColor.DarkYellow);
                else
                    providers.Add(new AbuseIPDBReputationProvider(configuration));
            }

            if (configuration.BlockListDeEnabled)
                providers.Add(new BlockListDeReputationProvider(configuration));
        }

        /// <summary>
        /// Registers an additional provider supplied by a consuming project.
        /// </summary>
        /// <param name="provider">Provider to register.</param>
        public void RegisterProvider(IIPReputationProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            lock (synchronizationLock)
            {
                if (!providers.Contains(provider))
                {
                    providers.Add(provider);
                    cache.Clear();
                }
            }
        }

        /// <summary>
        /// Removes a previously registered provider.
        /// </summary>
        /// <param name="provider">Provider to remove.</param>
        /// <returns>True when the provider was registered.</returns>
        public bool UnregisterProvider(IIPReputationProvider provider)
        {
            if (provider == null)
                return false;

            lock (synchronizationLock)
            {
                if (!providers.Remove(provider))
                    return false;

                // Cached decisions depend on the active provider set.
                cache.Clear();
                return true;
            }
        }

        /// <summary>
        /// Gets a non-expired cached reputation decision.
        /// </summary>
        /// <param name="ipAddress">Canonical IP address.</param>
        /// <param name="entry">Cached decision.</param>
        /// <returns>True when a cached decision exists.</returns>
        public bool TryGetCached(string ipAddress, out BlackListReputationCacheEntry entry)
        {
            lock (synchronizationLock)
            {
                if (cache.TryGetValue(ipAddress, out entry))
                {
                    if (DateTime.UtcNow < entry.ExpiresUtc)
                        return true;

                    cache.Remove(ipAddress);
                }

                entry = null;
                return false;
            }
        }

        /// <summary>
        /// Queues a cache refresh without blocking the caller.
        /// </summary>
        /// <param name="ipAddress">Canonical public IP address.</param>
        public void QueueRefresh(string ipAddress)
        {
            lock (synchronizationLock)
            {
                if (providers.Count == 0 ||
                    pendingAddresses.Contains(ipAddress) ||
                    pendingAddresses.Count >= maximumPendingChecks)
                {
                    return;
                }

                BlackListReputationCacheEntry cachedEntry;
                if (cache.TryGetValue(ipAddress, out cachedEntry) && DateTime.UtcNow < cachedEntry.ExpiresUtc)
                    return;

                pendingAddresses.Add(ipAddress);
            }

            // Providers may perform network I/O, so never execute them on the accepting connection worker.
            if (!ThreadPool.QueueUserWorkItem(sender => Refresh((string)sender), ipAddress))
            {
                lock (synchronizationLock)
                    pendingAddresses.Remove(ipAddress);
            }
        }

        /// <summary>
        /// Executes all providers until one lists the address, then caches the decision.
        /// </summary>
        /// <param name="ipAddress">Canonical public IP address.</param>
        private void Refresh(string ipAddress)
        {
            BlackListReputationCacheEntry cacheEntry = new BlackListReputationCacheEntry
            {
                IsListed = false,
                Source = "External reputation",
                Details = "No provider listed the address.",
                ExpiresUtc = DateTime.UtcNow.Add(cacheDuration)
            };

            bool providerSucceeded = false;
            try
            {
                IIPReputationProvider[] providerSnapshot;
                lock (synchronizationLock)
                    providerSnapshot = providers.ToArray();

                foreach (IIPReputationProvider provider in providerSnapshot)
                {
                    BlackListReputationResult result;
                    try
                    {
                        result = provider.Check(ipAddress);
                    }
                    catch (Exception ex)
                    {
                        result = BlackListReputationResult.Failure(ex.Message);
                    }

                    if (!result.Succeeded)
                    {
                        LogProviderFailure(provider.Name, result.Details);
                        continue;
                    }

                    providerSucceeded = true;
                    if (!result.IsListed)
                        continue;

                    cacheEntry.IsListed = true;
                    cacheEntry.Source = provider.Name;
                    cacheEntry.Details = result.Details;
                    break;
                }

                if (!providerSucceeded)
                {
                    cacheEntry.Details = "Every configured reputation provider failed.";
                    cacheEntry.ExpiresUtc = DateTime.UtcNow.Add(failureCacheDuration);
                }
            }
            finally
            {
                lock (synchronizationLock)
                {
                    pendingAddresses.Remove(ipAddress);
                    EnsureCacheCapacity();
                    cache[ipAddress] = cacheEntry;
                }
            }
        }

        /// <summary>
        /// Logs a provider failure at most once per minute for each provider.
        /// </summary>
        /// <param name="providerName">Provider name.</param>
        /// <param name="details">Failure details.</param>
        private void LogProviderFailure(string providerName, string details)
        {
            lock (synchronizationLock)
            {
                DateTime nextLogUtc;
                if (nextProviderErrorLogUtc.TryGetValue(providerName, out nextLogUtc) && DateTime.UtcNow < nextLogUtc)
                    return;

                nextProviderErrorLogUtc[providerName] = DateTime.UtcNow.AddMinutes(1);
            }

            Writer.Write(providerName + " reputation check failed: " + details, ConsoleColor.DarkYellow);
        }

        /// <summary>
        /// Removes one cache entry in constant time when capacity is reached.
        /// </summary>
        private void EnsureCacheCapacity()
        {
            if (cache.Count < maximumCacheEntries)
                return;

            string keyToRemove = null;
            foreach (KeyValuePair<string, BlackListReputationCacheEntry> pair in cache)
            {
                keyToRemove = pair.Key;
                break;
            }

            if (keyToRemove != null)
                cache.Remove(keyToRemove);
        }
    }
}
