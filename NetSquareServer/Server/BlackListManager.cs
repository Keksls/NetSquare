using NetSquare.Core;
using NetSquare.Core.Configuration;
using NetSquare.Server.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;

namespace NetSquare.Server
{
    /// <summary>
    /// Manages generic subjects, escalation policies, bans, IP reputation and persistent history.
    /// </summary>
    public static class BlackListManager
    {
        #region State
        private static readonly object SynchronizationLock = new object();
        private static readonly Dictionary<BlackListSubject, BlackListSubjectEntry> SubjectEntries =
            new Dictionary<BlackListSubject, BlackListSubjectEntry>();
        private static readonly Dictionary<BlackListSubject, BlackListHitCounter> Hits =
            new Dictionary<BlackListSubject, BlackListHitCounter>();
        private static readonly Dictionary<string, BlackListPolicy> Policies =
            new Dictionary<string, BlackListPolicy>(StringComparer.OrdinalIgnoreCase);
        private static NetSquareConfiguration configuration;
        private static BlackListReputationService reputationService;
        private static string defaultPolicyName;
        private static bool initialized;
        #endregion

        #region Public state
        public static IReadOnlyCollection<string> IPBlackList
        {
            get
            {
                EnsureInitialized();
                lock (SynchronizationLock)
                {
                    DateTime nowUtc = DateTime.UtcNow;
                    if (MaintainStateLocked(nowUtc))
                        TrySaveAfterMaintenanceLocked();

                    // Preserve the historical IP-only view while generic subjects use GetStatus.
                    return SubjectEntries
                        .Where(pair => pair.Key.Type == BlackListSubject.IPAddressType &&
                                       IsActiveBanLocked(pair.Value, nowUtc))
                        .Select(pair => pair.Key.Identifier)
                        .ToList();
                }
            }
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes policies, persisted subject history and external IP reputation.
        /// </summary>
        public static void Initialize()
        {
            lock (SynchronizationLock)
            {
                if (initialized)
                    return;

                configuration = NetSquareConfigurationManager.Get<NetSquareConfiguration>();
                ValidateConfiguration(configuration);
                LoadPoliciesLocked();
                Writer.Write_Physical("Loading blacklist subjects...", ConsoleColor.DarkYellow, false);
                LoadLocked();
                reputationService = new BlackListReputationService(configuration);
                initialized = true;
                Writer.Write(SubjectEntries.Count.ToString(), ConsoleColor.Green);
            }
        }

        /// <summary>
        /// Ensures the manager is ready for calls made before server start.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (!initialized)
                Initialize();
        }

        /// <summary>
        /// Validates required blacklist configuration values.
        /// </summary>
        /// <param name="activeConfiguration">Active server configuration.</param>
        private static void ValidateConfiguration(NetSquareConfiguration activeConfiguration)
        {
            if (activeConfiguration == null)
                throw new InvalidOperationException("The NetSquare configuration is not initialized.");
            if (string.IsNullOrWhiteSpace(activeConfiguration.BlackListFilePath))
                throw new InvalidOperationException("BlackListFilePath must identify a blacklist file.");
        }
        #endregion

        #region Policies
        /// <summary>
        /// Loads and validates configured escalation policies.
        /// </summary>
        private static void LoadPoliciesLocked()
        {
            Policies.Clear();
            IEnumerable<BlackListPolicy> configuredPolicies = configuration.BlackListPolicies;
            if (configuredPolicies == null || !configuredPolicies.Any())
                configuredPolicies = new[] { CreateLegacyPolicy() };

            foreach (BlackListPolicy policy in configuredPolicies)
            {
                ValidatePolicy(policy);
                if (Policies.ContainsKey(policy.Name))
                    throw new InvalidOperationException("Duplicate blacklist policy name: " + policy.Name);
                Policies.Add(policy.Name, policy);
            }

            defaultPolicyName = string.IsNullOrWhiteSpace(configuration.BlackListDefaultPolicyName)
                ? Policies.Keys.First()
                : configuration.BlackListDefaultPolicyName.Trim();
            if (!Policies.ContainsKey(defaultPolicyName))
                throw new InvalidOperationException(
                    "BlackListDefaultPolicyName does not match a configured policy: " + defaultPolicyName);
        }

        /// <summary>
        /// Builds a one-stage policy from the previous scalar configuration.
        /// </summary>
        /// <returns>The compatibility policy used when no policy list is configured.</returns>
        private static BlackListPolicy CreateLegacyPolicy()
        {
            BlackListPolicy policy = new BlackListPolicy
            {
                Name = string.IsNullOrWhiteSpace(configuration.BlackListDefaultPolicyName)
                    ? "default"
                    : configuration.BlackListDefaultPolicyName.Trim(),
                HitWindowSeconds = Math.Max(1, configuration.BlackListHitWindowSeconds),
                EscalationResetAfterSeconds = 0
            };

            if (configuration.BlackListHitThreshold > 0)
            {
                policy.Stages.Add(new BlackListEscalationStage
                {
                    HitThreshold = configuration.BlackListHitThreshold,
                    BanType = configuration.BlackListDefaultBanType,
                    BanDurationSeconds = Math.Max(1, configuration.BlackListTemporaryBanDurationSeconds)
                });
            }

            return policy;
        }

        /// <summary>
        /// Validates one configured policy and every escalation stage.
        /// </summary>
        /// <param name="policy">Policy to validate.</param>
        private static void ValidatePolicy(BlackListPolicy policy)
        {
            if (policy == null)
                throw new InvalidOperationException("BlackListPolicies cannot contain null entries.");
            if (string.IsNullOrWhiteSpace(policy.Name))
                throw new InvalidOperationException("Every blacklist policy requires a name.");
            if (policy.HitWindowSeconds <= 0)
                throw new InvalidOperationException("Blacklist policy hit windows must be greater than zero.");
            if (policy.EscalationResetAfterSeconds < 0)
                throw new InvalidOperationException("Blacklist policy reset delays cannot be negative.");

            policy.Name = policy.Name.Trim();
            policy.Stages = policy.Stages ?? new List<BlackListEscalationStage>();
            foreach (BlackListEscalationStage stage in policy.Stages)
            {
                if (stage == null)
                    throw new InvalidOperationException("Blacklist policy stages cannot be null.");
                if (stage.HitThreshold <= 0)
                    throw new InvalidOperationException("Blacklist stage hit thresholds must be greater than zero.");
                if (stage.BanType == BlackListBanType.Temporary && stage.BanDurationSeconds <= 0)
                    throw new InvalidOperationException("Temporary blacklist stages require a positive duration.");
            }
        }

        /// <summary>
        /// Resolves an explicit, persisted, type-specific or default policy in that order.
        /// </summary>
        /// <param name="subject">Subject receiving the policy.</param>
        /// <param name="requestedPolicyName">Optional caller-selected policy.</param>
        /// <param name="entry">Optional persisted subject entry.</param>
        /// <returns>The selected policy.</returns>
        private static BlackListPolicy ResolvePolicyLocked(
            BlackListSubject subject,
            string requestedPolicyName,
            BlackListSubjectEntry entry)
        {
            string policyName = requestedPolicyName;
            if (string.IsNullOrWhiteSpace(policyName) && entry != null)
                policyName = entry.PolicyName;
            if (string.IsNullOrWhiteSpace(policyName) && Policies.ContainsKey(subject.Type))
                policyName = subject.Type;
            if (string.IsNullOrWhiteSpace(policyName))
                policyName = defaultPolicyName;

            BlackListPolicy policy;
            if (!Policies.TryGetValue(policyName.Trim(), out policy))
                throw new ArgumentException("Unknown blacklist policy: " + policyName, nameof(requestedPolicyName));

            if (entry != null && !string.Equals(entry.PolicyName, policy.Name, StringComparison.OrdinalIgnoreCase))
            {
                // An explicit policy switch preserves history but clamps it to the new policy.
                entry.PolicyName = policy.Name;
                entry.EscalationLevel = ClampEscalationLevel(entry.EscalationLevel, policy);
            }

            return policy;
        }

        /// <summary>
        /// Returns the stage currently assigned to an entry.
        /// </summary>
        /// <param name="policy">Resolved policy.</param>
        /// <param name="entry">Optional subject state.</param>
        /// <returns>The current stage, or null when hit tracking is disabled.</returns>
        private static BlackListEscalationStage GetCurrentStage(
            BlackListPolicy policy,
            BlackListSubjectEntry entry)
        {
            if (policy.Stages.Count == 0)
                return null;

            int level = ClampEscalationLevel(entry != null ? entry.EscalationLevel : 0, policy);
            return policy.Stages[level];
        }

        /// <summary>
        /// Clamps a persisted escalation level to the available stage range.
        /// </summary>
        /// <param name="level">Stored escalation level.</param>
        /// <param name="policy">Resolved policy.</param>
        /// <returns>A valid stage index, or zero for an empty policy.</returns>
        private static int ClampEscalationLevel(int level, BlackListPolicy policy)
        {
            if (policy.Stages.Count == 0)
                return 0;
            return Math.Max(0, Math.Min(level, policy.Stages.Count - 1));
        }
        #endregion

        #region Generic hits
        /// <summary>
        /// Adds hits to a generic subject and applies the current escalation stage when its threshold is reached.
        /// </summary>
        /// <param name="subject">Account, IP, device or other project-defined subject.</param>
        /// <param name="hitCount">Number of hits to add.</param>
        /// <param name="reason">Optional reason stored with a created ban.</param>
        /// <param name="policyName">Optional configured policy name.</param>
        /// <param name="banType">Optional ban type override for the triggered stage.</param>
        /// <param name="temporaryBanDuration">Optional temporary duration override.</param>
        /// <returns>The resulting hit, escalation and ban state.</returns>
        public static BlackListHitResult AddHit(
            BlackListSubject subject,
            int hitCount = 1,
            string reason = null,
            string policyName = null,
            BlackListBanType? banType = null,
            TimeSpan? temporaryBanDuration = null)
        {
            EnsureInitialized();
            ValidateSubject(subject);
            if (hitCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(hitCount));

            lock (SynchronizationLock)
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (MaintainStateLocked(nowUtc))
                    TrySaveAfterMaintenanceLocked();

                BlackListSubjectEntry entry;
                SubjectEntries.TryGetValue(subject, out entry);
                BlackListPolicy policy = ResolvePolicyLocked(subject, policyName, entry);
                if (entry != null && ApplyRehabilitationLocked(subject, entry, policy, nowUtc))
                {
                    TrySaveAfterMaintenanceLocked();
                    SubjectEntries.TryGetValue(subject, out entry);
                }

                if (entry != null && IsActiveBanLocked(entry, nowUtc))
                    return CreateHitResult(subject, entry, policy, null, false, null);

                BlackListEscalationStage stage = GetCurrentStage(policy, entry);
                if (stage == null || ShouldIgnoreHits(subject))
                    return CreateHitResult(subject, entry, policy, null, false, null);

                bool hadEntryBeforeHit = entry != null;
                BlackListSubjectEntry entryBeforeHit = hadEntryBeforeHit ? CloneEntry(entry) : null;
                if (entry == null)
                {
                    entry = CreateEntry(subject, policy.Name);
                    SubjectEntries[subject] = entry;
                }

                BlackListHitCounter counter;
                if (!Hits.TryGetValue(subject, out counter))
                {
                    EnsureHitCapacityLocked();
                    counter = new BlackListHitCounter
                    {
                        ExpiresUtc = nowUtc.AddSeconds(policy.HitWindowSeconds)
                    };
                    Hits[subject] = counter;
                }

                counter.Count = counter.Count > int.MaxValue - hitCount
                    ? int.MaxValue
                    : counter.Count + hitCount;
                entry.LastIncidentUtc = nowUtc;
                StoreHitProgress(entry, counter);
                if (counter.Count < stage.HitThreshold)
                {
                    if (configuration.BlackListPersistHitProgress)
                    {
                        try
                        {
                            SaveLocked();
                        }
                        catch
                        {
                            RestoreHitMutationLocked(subject, hadEntryBeforeHit, entryBeforeHit, counter);
                            throw;
                        }
                    }

                    return CreateHitResult(subject, entry, policy, counter, false, null);
                }

                int appliedStageIndex = ClampEscalationLevel(entry.EscalationLevel, policy);
                BlackListSubjectEntry previousEntry = CloneEntry(entry);
                Hits.Remove(subject);
                ClearStoredHitProgress(entry);
                ApplyBanLocked(
                    entry,
                    banType ?? stage.BanType,
                    ResolveStageDuration(stage, banType, temporaryBanDuration),
                    reason,
                    "Hit threshold",
                    nowUtc);
                if (appliedStageIndex < policy.Stages.Count - 1)
                    entry.EscalationLevel = appliedStageIndex + 1;

                try
                {
                    SaveLocked();
                }
                catch
                {
                    // Restore the complete hit and escalation state when persistence fails.
                    SubjectEntries[subject] = previousEntry;
                    Hits[subject] = counter;
                    throw;
                }

                Writer.Write(
                    "Subject " + subject + " banned at escalation stage " + appliedStageIndex +
                    " after " + counter.Count + " hits.",
                    ConsoleColor.Red);
                return CreateHitResult(subject, entry, policy, null, true, appliedStageIndex);
            }
        }

        /// <summary>
        /// Clears the active hit window without changing escalation history.
        /// </summary>
        /// <param name="subject">Subject whose current hits are cleared.</param>
        /// <returns>True when an active hit window was removed.</returns>
        public static bool ClearHits(BlackListSubject subject)
        {
            EnsureInitialized();
            ValidateSubject(subject);
            lock (SynchronizationLock)
            {
                BlackListHitCounter counter;
                if (!Hits.TryGetValue(subject, out counter))
                    return false;

                BlackListSubjectEntry entry;
                SubjectEntries.TryGetValue(subject, out entry);
                BlackListSubjectEntry previousEntry = entry != null ? CloneEntry(entry) : null;
                Hits.Remove(subject);
                if (entry != null)
                    ClearStoredHitProgress(entry);
                RemoveEmptyEntryLocked(subject);
                try
                {
                    if (configuration.BlackListPersistHitProgress)
                        SaveLocked();
                }
                catch
                {
                    Hits[subject] = counter;
                    if (previousEntry != null)
                        SubjectEntries[subject] = previousEntry;
                    throw;
                }

                return true;
            }
        }

        /// <summary>
        /// Clears escalation history and current hits while preserving an active ban.
        /// </summary>
        /// <param name="subject">Subject whose history is reset.</param>
        /// <returns>True when stored history or hits changed.</returns>
        public static bool ClearHistory(BlackListSubject subject)
        {
            EnsureInitialized();
            ValidateSubject(subject);
            lock (SynchronizationLock)
            {
                bool changed = Hits.Remove(subject);
                BlackListSubjectEntry entry;
                if (SubjectEntries.TryGetValue(subject, out entry))
                {
                    changed = changed || entry.EscalationLevel != 0 || entry.LastIncidentUtc.HasValue;
                    entry.EscalationLevel = 0;
                    entry.LastIncidentUtc = null;
                    ClearStoredHitProgress(entry);
                    RemoveEmptyEntryLocked(subject);
                }

                if (changed)
                    SaveLocked();
                return changed;
            }
        }
        #endregion

        #region IP hit adapters
        /// <summary>
        /// Adds hits to an IP using the generic subject engine.
        /// </summary>
        /// <param name="ipAddress">IP address receiving the hits.</param>
        /// <param name="hitCount">Number of hits to add.</param>
        /// <param name="reason">Optional reason stored with a created ban.</param>
        /// <param name="banType">Optional ban type override.</param>
        /// <param name="temporaryBanDuration">Optional temporary duration override.</param>
        /// <returns>The resulting hit and ban state.</returns>
        public static BlackListHitResult AddHit(
            string ipAddress,
            int hitCount = 1,
            string reason = null,
            BlackListBanType? banType = null,
            TimeSpan? temporaryBanDuration = null)
        {
            return AddHit(
                BlackListSubject.ForIPAddress(ipAddress),
                hitCount,
                reason,
                null,
                banType,
                temporaryBanDuration);
        }

        /// <summary>
        /// Adds hits to the remote IP of a connected client and disconnects it when banned.
        /// </summary>
        /// <param name="client">Connected NetSquare client.</param>
        /// <param name="hitCount">Number of hits to add.</param>
        /// <param name="reason">Optional reason stored with a created ban.</param>
        /// <param name="banType">Optional ban type override.</param>
        /// <param name="temporaryBanDuration">Optional temporary duration override.</param>
        /// <returns>The resulting hit and ban state.</returns>
        public static BlackListHitResult AddHit(
            ConnectedClient client,
            int hitCount = 1,
            string reason = null,
            BlackListBanType? banType = null,
            TimeSpan? temporaryBanDuration = null)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            BlackListHitResult result = AddHit(
                IPAddressUtilities.GetRemoteAddress(client.TcpSocket),
                hitCount,
                reason,
                banType,
                temporaryBanDuration);
            if (result.IsBanned && result.BanType.HasValue)
                DisconnectBannedClient(client, result.CreateDisconnectInfo());
            return result;
        }

        /// <summary>
        /// Clears the active hit window for an IP.
        /// </summary>
        /// <param name="ipAddress">IP address whose hits are cleared.</param>
        /// <returns>True when an active hit window was removed.</returns>
        public static bool ClearHits(string ipAddress)
        {
            return ClearHits(BlackListSubject.ForIPAddress(ipAddress));
        }
        #endregion

        #region Generic bans
        /// <summary>
        /// Applies a direct temporary or permanent ban to a generic subject.
        /// </summary>
        /// <param name="subject">Subject to ban.</param>
        /// <param name="banType">Ban type.</param>
        /// <param name="temporaryBanDuration">Optional temporary duration override.</param>
        /// <param name="reason">Optional ban reason.</param>
        /// <param name="source">Component creating the ban.</param>
        /// <param name="policyName">Optional policy associated with future hits.</param>
        /// <returns>The resulting subject status.</returns>
        public static BlackListStatus Ban(
            BlackListSubject subject,
            BlackListBanType banType,
            TimeSpan? temporaryBanDuration = null,
            string reason = null,
            string source = "Application",
            string policyName = null)
        {
            EnsureInitialized();
            ValidateSubject(subject);
            lock (SynchronizationLock)
            {
                DateTime nowUtc = DateTime.UtcNow;
                BlackListSubjectEntry entry;
                bool hadPrevious = SubjectEntries.TryGetValue(subject, out entry);
                BlackListHitCounter previousCounter;
                Hits.TryGetValue(subject, out previousCounter);
                BlackListSubjectEntry previousEntry = hadPrevious ? CloneEntry(entry) : null;
                BlackListPolicy policy = ResolvePolicyLocked(subject, policyName, entry);
                if (!hadPrevious)
                {
                    entry = CreateEntry(subject, policy.Name);
                    SubjectEntries[subject] = entry;
                }

                ApplyBanLocked(entry, banType, temporaryBanDuration, reason, source, nowUtc);
                Hits.Remove(subject);
                ClearStoredHitProgress(entry);
                try
                {
                    SaveLocked();
                }
                catch
                {
                    if (hadPrevious)
                        SubjectEntries[subject] = previousEntry;
                    else
                        SubjectEntries.Remove(subject);
                    if (previousCounter != null)
                        Hits[subject] = previousCounter;
                    throw;
                }

                Writer.Write(
                    "Blacklisted subject " + subject +
                    (banType == BlackListBanType.Temporary
                        ? " until " + entry.BanExpiresUtc.Value.ToString("O")
                        : " permanently"),
                    ConsoleColor.Red);
                return CreateStatusLocked(subject, entry, policy, nowUtc);
            }
        }

        /// <summary>
        /// Removes an active ban while preserving escalation history.
        /// </summary>
        /// <param name="subject">Subject to unban.</param>
        /// <returns>True when an active ban was removed.</returns>
        public static bool Unban(BlackListSubject subject)
        {
            EnsureInitialized();
            ValidateSubject(subject);
            lock (SynchronizationLock)
            {
                BlackListSubjectEntry entry;
                if (!SubjectEntries.TryGetValue(subject, out entry) ||
                    !IsActiveBanLocked(entry, DateTime.UtcNow))
                {
                    return false;
                }

                BlackListSubjectEntry previousEntry = CloneEntry(entry);
                ClearActiveBanLocked(entry, DateTime.UtcNow);
                RemoveEmptyEntryLocked(subject);
                try
                {
                    SaveLocked();
                }
                catch
                {
                    SubjectEntries[subject] = previousEntry;
                    throw;
                }

                return true;
            }
        }

        /// <summary>
        /// Gets the local and optional IP reputation status of a generic subject.
        /// </summary>
        /// <param name="subject">Subject to inspect.</param>
        /// <returns>The current hit, escalation and ban status.</returns>
        public static BlackListStatus GetStatus(BlackListSubject subject)
        {
            EnsureInitialized();
            ValidateSubject(subject);
            DateTime nowUtc = DateTime.UtcNow;
            BlackListStatus status;

            lock (SynchronizationLock)
            {
                if (MaintainStateLocked(nowUtc))
                    TrySaveAfterMaintenanceLocked();

                BlackListSubjectEntry entry;
                SubjectEntries.TryGetValue(subject, out entry);
                BlackListPolicy policy = ResolvePolicyLocked(subject, null, entry);
                status = CreateStatusLocked(subject, entry, policy, nowUtc);
            }

            if (!status.IsBanned && subject.Type == BlackListSubject.IPAddressType)
            {
                BlackListReputationCacheEntry external;
                if (reputationService.TryGetCached(subject.Identifier, out external) && external.IsListed)
                {
                    status.IsBanned = true;
                    status.Reason = external.Details;
                    status.Source = external.Source;
                }
            }

            return status;
        }

        /// <summary>
        /// Returns whether a generic subject has an active local ban.
        /// </summary>
        /// <param name="subject">Subject to inspect.</param>
        /// <returns>True when a local ban is active.</returns>
        public static bool IsLocallyBlackListed(BlackListSubject subject)
        {
            EnsureInitialized();
            ValidateSubject(subject);
            lock (SynchronizationLock)
            {
                DateTime nowUtc = DateTime.UtcNow;
                if (MaintainStateLocked(nowUtc))
                    TrySaveAfterMaintenanceLocked();

                BlackListSubjectEntry entry;
                bool isBanned = SubjectEntries.TryGetValue(subject, out entry) &&
                                IsActiveBanLocked(entry, nowUtc);
                if (isBanned)
                    Writer.Write("[" + subject + "] Locally blacklisted.", ConsoleColor.Red);
                return isBanned;
            }
        }

        /// <summary>
        /// Returns whether a generic subject is banned, including reputation for IP subjects.
        /// </summary>
        /// <param name="subject">Subject to inspect.</param>
        /// <returns>True when the subject must be rejected.</returns>
        public static bool IsBlackListed(BlackListSubject subject)
        {
            ValidateSubject(subject);
            if (subject.Type == BlackListSubject.IPAddressType)
                return IsBlackListed(subject.Identifier);
            return IsLocallyBlackListed(subject);
        }
        #endregion

        #region IP ban adapters
        /// <summary>
        /// Bans an IP using the configured default type.
        /// </summary>
        /// <param name="ipAddress">IP address to ban.</param>
        /// <param name="reason">Optional ban reason.</param>
        public static void BanIP(string ipAddress, string reason = null)
        {
            EnsureInitialized();
            BanIP(ipAddress, configuration.BlackListDefaultBanType, null, reason, "Manual");
        }

        /// <summary>
        /// Bans an IP with an explicit type and optional duration.
        /// </summary>
        /// <param name="ipAddress">IP address to ban.</param>
        /// <param name="banType">Ban type.</param>
        /// <param name="temporaryBanDuration">Optional temporary duration override.</param>
        /// <param name="reason">Optional ban reason.</param>
        /// <param name="source">Component creating the ban.</param>
        public static void BanIP(
            string ipAddress,
            BlackListBanType banType,
            TimeSpan? temporaryBanDuration = null,
            string reason = null,
            string source = "Manual")
        {
            Ban(
                BlackListSubject.ForIPAddress(ipAddress),
                banType,
                temporaryBanDuration,
                reason,
                source);
        }

        /// <summary>
        /// Bans and disconnects the remote IP of a connected client.
        /// </summary>
        /// <param name="client">Connected NetSquare client.</param>
        /// <param name="banType">Ban type.</param>
        /// <param name="temporaryBanDuration">Optional temporary duration override.</param>
        /// <param name="reason">Optional ban reason.</param>
        public static void Ban(
            ConnectedClient client,
            BlackListBanType banType,
            TimeSpan? temporaryBanDuration = null,
            string reason = null)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            BlackListStatus status = Ban(
                BlackListSubject.ForIPAddress(IPAddressUtilities.GetRemoteAddress(client.TcpSocket)),
                banType,
                temporaryBanDuration,
                reason,
                "Application");
            DisconnectBannedClient(client, status.CreateDisconnectInfo());
        }

        /// <summary>
        /// Removes an IP ban while preserving its escalation history.
        /// </summary>
        /// <param name="ipAddress">IP address to unban.</param>
        /// <returns>True when an active ban was removed.</returns>
        public static bool UnbanIP(string ipAddress)
        {
            return Unban(BlackListSubject.ForIPAddress(ipAddress));
        }

        /// <summary>
        /// Gets the local and cached external status for an IP.
        /// </summary>
        /// <param name="ipAddress">IP address to inspect.</param>
        /// <returns>The current blacklist status.</returns>
        public static BlackListStatus GetStatus(string ipAddress)
        {
            return GetStatus(BlackListSubject.ForIPAddress(ipAddress));
        }

        /// <summary>
        /// Returns whether a socket remote address is banned.
        /// </summary>
        /// <param name="client">Connected socket to inspect.</param>
        /// <returns>True when the remote address must be rejected.</returns>
        public static bool IsBlackListed(Socket client)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            return IsBlackListed(IPAddressUtilities.GetRemoteAddress(client));
        }

        /// <summary>
        /// Returns whether an IP is locally banned or listed by cached reputation.
        /// </summary>
        /// <param name="ipAddress">IP address to inspect.</param>
        /// <returns>True when the address must be rejected.</returns>
        public static bool IsBlackListed(string ipAddress)
        {
            EnsureInitialized();
            BlackListSubject subject = BlackListSubject.ForIPAddress(ipAddress);
            if (IsLocallyBlackListed(subject))
                return true;
            if (IPAddressUtilities.IsNonPublic(subject.Identifier))
                return false;

            BlackListReputationCacheEntry external;
            if (reputationService.TryGetCached(subject.Identifier, out external))
            {
                if (external.IsListed)
                {
                    Writer.Write(
                        "[" + subject.Identifier + "] " + external.Source +
                        " blacklisted. " + external.Details,
                        ConsoleColor.Red);
                }
                return external.IsListed;
            }

            // First-seen public addresses are allowed while the asynchronous cache is populated.
            reputationService.QueueRefresh(subject.Identifier);
            return false;
        }

        /// <summary>
        /// Returns whether an IP has an active local ban.
        /// </summary>
        /// <param name="ipAddress">IP address to inspect.</param>
        /// <returns>True when an active local ban exists.</returns>
        public static bool IsBlackListed_Local(string ipAddress)
        {
            return IsLocallyBlackListed(BlackListSubject.ForIPAddress(ipAddress));
        }

        /// <summary>
        /// Permanently blacklists an IP address.
        /// </summary>
        /// <param name="ipAddress">IP address to blacklist permanently.</param>
        public static void BlackListIP(string ipAddress)
        {
            BanIP(ipAddress, BlackListBanType.Permanent, null, null, "Direct blacklist API");
        }

        /// <summary>
        /// Permanently blacklists the remote address of a TCP client.
        /// </summary>
        /// <param name="client">TCP client to blacklist permanently.</param>
        public static void BlackList(TcpClient client)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            BlackListIP(IPAddressUtilities.GetRemoteAddress(client.Client));
        }

        /// <summary>
        /// Returns whether an address must stay outside public reputation services.
        /// </summary>
        /// <param name="ipAddress">IP address to classify.</param>
        /// <returns>True when the address is non-public.</returns>
        public static bool IsLocalAddress(string ipAddress)
        {
            string address;
            return !IPAddressUtilities.TryNormalize(ipAddress, out address) ||
                   IPAddressUtilities.IsNonPublic(address);
        }
        #endregion

        #region Reputation providers
        /// <summary>
        /// Registers a custom external IP reputation provider.
        /// </summary>
        /// <param name="provider">Provider supplied by the consuming project.</param>
        public static void RegisterReputationProvider(IIPReputationProvider provider)
        {
            EnsureInitialized();
            reputationService.RegisterProvider(provider);
        }

        /// <summary>
        /// Removes a custom external IP reputation provider.
        /// </summary>
        /// <param name="provider">Previously registered provider.</param>
        /// <returns>True when the provider was registered.</returns>
        public static bool UnregisterReputationProvider(IIPReputationProvider provider)
        {
            EnsureInitialized();
            return reputationService.UnregisterProvider(provider);
        }

        /// <summary>
        /// Queues a non-blocking external reputation refresh for a public IP.
        /// </summary>
        /// <param name="ipAddress">IP address to refresh.</param>
        public static void QueueReputationRefresh(string ipAddress)
        {
            EnsureInitialized();
            BlackListSubject subject = BlackListSubject.ForIPAddress(ipAddress);
            if (!IPAddressUtilities.IsNonPublic(subject.Identifier))
                reputationService.QueueRefresh(subject.Identifier);
        }
        #endregion

        #region Persistence
        /// <summary>
        /// Loads generic subject state and migrates both previous IP-only formats.
        /// </summary>
        private static void LoadLocked()
        {
            string filePath = Path.GetFullPath(configuration.BlackListFilePath);
            string directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryPath))
                Directory.CreateDirectory(directoryPath);

            SubjectEntries.Clear();
            Hits.Clear();
            if (!File.Exists(filePath))
            {
                SaveLocked();
                return;
            }

            string json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("The blacklist file is empty.");

            BlackListPersistentState state;
            try
            {
                state = NetSquareJsonSerializer.Deserialize<BlackListPersistentState>(json);
            }
            catch
            {
                // The oldest format stored a plain JSON string array of permanent IP bans.
                string[] oldAddresses = NetSquareJsonSerializer.Deserialize<string[]>(json);
                state = new BlackListPersistentState
                {
                    Bans = new List<BlackListBanEntry>()
                };
                foreach (string oldAddress in oldAddresses ?? Array.Empty<string>())
                {
                    string address;
                    if (IPAddressUtilities.TryNormalize(oldAddress, out address))
                    {
                        state.Bans.Add(new BlackListBanEntry
                        {
                            IPAddress = address,
                            BanType = BlackListBanType.Permanent,
                            CreatedUtc = DateTime.UtcNow,
                            Source = "Legacy file migration"
                        });
                    }
                }
            }

            if (state == null)
                throw new InvalidDataException("The blacklist file contains no state object.");

            foreach (BlackListSubjectEntry entry in state.Subjects ?? new List<BlackListSubjectEntry>())
                LoadSubjectEntryLocked(entry);
            foreach (BlackListBanEntry legacyBan in state.Bans ?? new List<BlackListBanEntry>())
                MigrateLegacyBanLocked(legacyBan);

            MaintainStateLocked(DateTime.UtcNow);
            // Rewrite once so successful migration removes all legacy fields and expired active bans.
            SaveLocked();
        }

        /// <summary>
        /// Loads and normalizes one persisted generic subject entry.
        /// </summary>
        /// <param name="storedEntry">Persisted entry to load.</param>
        private static void LoadSubjectEntryLocked(BlackListSubjectEntry storedEntry)
        {
            if (storedEntry == null)
                return;

            BlackListSubject subject;
            try
            {
                subject = CreateNormalizedSubject(storedEntry.SubjectType, storedEntry.SubjectIdentifier);
            }
            catch (ArgumentException)
            {
                return;
            }

            BlackListPolicy policy;
            try
            {
                policy = ResolvePolicyLocked(subject, storedEntry.PolicyName, null);
            }
            catch (ArgumentException)
            {
                policy = ResolvePolicyLocked(subject, null, null);
            }

            BlackListSubjectEntry entry = CloneEntry(storedEntry);
            entry.SubjectType = subject.Type;
            entry.SubjectIdentifier = subject.Identifier;
            entry.PolicyName = policy.Name;
            entry.EscalationLevel = ClampEscalationLevel(entry.EscalationLevel, policy);
            entry.LastIncidentUtc = NormalizeUtc(entry.LastIncidentUtc);
            entry.BanCreatedUtc = NormalizeUtc(entry.BanCreatedUtc);
            entry.BanExpiresUtc = NormalizeUtc(entry.BanExpiresUtc);
            entry.HitWindowExpiresUtc = NormalizeUtc(entry.HitWindowExpiresUtc);

            if (entry.BanType == BlackListBanType.Temporary &&
                (!entry.BanExpiresUtc.HasValue || entry.BanExpiresUtc.Value <= DateTime.UtcNow))
            {
                ClearActiveBanLocked(entry, entry.BanExpiresUtc ?? DateTime.UtcNow);
            }

            if (configuration.BlackListPersistHitProgress &&
                entry.HitCount > 0 &&
                entry.HitWindowExpiresUtc.HasValue &&
                entry.HitWindowExpiresUtc.Value > DateTime.UtcNow)
            {
                Hits[subject] = new BlackListHitCounter
                {
                    Count = entry.HitCount,
                    ExpiresUtc = entry.HitWindowExpiresUtc.Value
                };
            }
            else
                ClearStoredHitProgress(entry);

            if (entry.BanType.HasValue || entry.EscalationLevel > 0 || entry.HitCount > 0)
                SubjectEntries[subject] = entry;
        }

        /// <summary>
        /// Converts one previous-format IP ban into a generic subject state.
        /// </summary>
        /// <param name="legacyBan">IP ban to migrate.</param>
        private static void MigrateLegacyBanLocked(BlackListBanEntry legacyBan)
        {
            if (legacyBan == null)
                return;

            BlackListSubject subject;
            try
            {
                subject = BlackListSubject.ForIPAddress(legacyBan.IPAddress);
            }
            catch (ArgumentException)
            {
                return;
            }

            BlackListPolicy policy = ResolvePolicyLocked(subject, null, null);
            BlackListSubjectEntry entry = CreateEntry(subject, policy.Name);
            entry.BanType = legacyBan.BanType;
            entry.BanCreatedUtc = NormalizeUtc(legacyBan.CreatedUtc);
            entry.BanExpiresUtc = NormalizeUtc(legacyBan.ExpiresUtc);
            entry.LastIncidentUtc = entry.BanCreatedUtc;
            entry.Reason = legacyBan.Reason;
            entry.Source = legacyBan.Source;
            if (string.Equals(legacyBan.Source, "Hit threshold", StringComparison.OrdinalIgnoreCase) &&
                policy.Stages.Count > 1)
            {
                entry.EscalationLevel = 1;
            }

            if (entry.BanType == BlackListBanType.Temporary &&
                (!entry.BanExpiresUtc.HasValue || entry.BanExpiresUtc.Value <= DateTime.UtcNow))
            {
                ClearActiveBanLocked(entry, entry.BanExpiresUtc ?? DateTime.UtcNow);
            }

            if (entry.BanType.HasValue || entry.EscalationLevel > 0)
                SubjectEntries[subject] = entry;
        }

        /// <summary>
        /// Persists generic escalation history and active bans using atomic replacement when supported.
        /// </summary>
        private static void SaveLocked()
        {
            string filePath = Path.GetFullPath(configuration.BlackListFilePath);
            string directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryPath))
                Directory.CreateDirectory(directoryPath);

            BlackListPersistentState state = new BlackListPersistentState();
            foreach (KeyValuePair<BlackListSubject, BlackListSubjectEntry> pair in SubjectEntries
                         .OrderBy(value => value.Key.Type, StringComparer.Ordinal)
                         .ThenBy(value => value.Key.Identifier, StringComparer.Ordinal))
            {
                BlackListSubjectEntry snapshot = CreatePersistentSnapshot(pair.Value);
                if (snapshot != null)
                    state.Subjects.Add(snapshot);
            }

            string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, NetSquareJsonSerializer.Serialize(state));
                if (!File.Exists(filePath))
                {
                    File.Move(temporaryPath, filePath);
                    return;
                }

                try
                {
                    File.Replace(temporaryPath, filePath, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temporaryPath, filePath, true);
                }
                catch (IOException)
                {
                    // Some filesystems do not expose atomic replace.
                    File.Copy(temporaryPath, filePath, true);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        /// <summary>
        /// Creates the persisted projection of a runtime entry.
        /// </summary>
        /// <param name="entry">Runtime subject entry.</param>
        /// <returns>A serializable snapshot, or null when nothing persistent remains.</returns>
        private static BlackListSubjectEntry CreatePersistentSnapshot(BlackListSubjectEntry entry)
        {
            BlackListSubjectEntry snapshot = CloneEntry(entry);
            if (snapshot.BanType == BlackListBanType.Temporary &&
                !configuration.BlackListPersistTemporaryBans)
            {
                ClearActiveBanLocked(snapshot, snapshot.BanExpiresUtc ?? DateTime.UtcNow);
            }
            if (!configuration.BlackListPersistHitProgress)
                ClearStoredHitProgress(snapshot);

            return snapshot.BanType.HasValue || snapshot.EscalationLevel > 0 || snapshot.HitCount > 0
                ? snapshot
                : null;
        }

        /// <summary>
        /// Saves maintenance changes without failing an incoming connection on disk errors.
        /// </summary>
        private static void TrySaveAfterMaintenanceLocked()
        {
            try
            {
                SaveLocked();
            }
            catch (Exception ex)
            {
                Writer.Write(
                    "Failed to persist blacklist maintenance: " + ex.Message,
                    ConsoleColor.DarkYellow);
            }
        }
        #endregion

        #region State maintenance
        /// <summary>
        /// Expires hit windows and bans, then applies configured rehabilitation delays.
        /// </summary>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <returns>True when persistent state changed.</returns>
        private static bool MaintainStateLocked(DateTime nowUtc)
        {
            bool persistentChange = PurgeExpiredHitsLocked(nowUtc);
            List<BlackListSubject> removableSubjects = null;

            foreach (KeyValuePair<BlackListSubject, BlackListSubjectEntry> pair in SubjectEntries)
            {
                BlackListSubjectEntry entry = pair.Value;
                if (entry.BanType == BlackListBanType.Temporary &&
                    (!entry.BanExpiresUtc.HasValue || entry.BanExpiresUtc.Value <= nowUtc))
                {
                    ClearActiveBanLocked(entry, entry.BanExpiresUtc ?? nowUtc);
                    persistentChange = true;
                }

                BlackListPolicy policy = ResolvePolicyLocked(pair.Key, null, entry);
                if (ApplyRehabilitationFieldsLocked(entry, policy, nowUtc))
                    persistentChange = true;

                if (!entry.BanType.HasValue && entry.EscalationLevel == 0 && !Hits.ContainsKey(pair.Key))
                {
                    if (removableSubjects == null)
                        removableSubjects = new List<BlackListSubject>();
                    removableSubjects.Add(pair.Key);
                }
            }

            if (removableSubjects != null)
            {
                foreach (BlackListSubject subject in removableSubjects)
                    SubjectEntries.Remove(subject);
            }

            return persistentChange;
        }

        /// <summary>
        /// Applies rehabilitation to one subject and removes an empty runtime entry.
        /// </summary>
        /// <param name="subject">Subject being maintained.</param>
        /// <param name="entry">Subject state.</param>
        /// <param name="policy">Resolved policy.</param>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <returns>True when persistent history was reset.</returns>
        private static bool ApplyRehabilitationLocked(
            BlackListSubject subject,
            BlackListSubjectEntry entry,
            BlackListPolicy policy,
            DateTime nowUtc)
        {
            bool changed = ApplyRehabilitationFieldsLocked(entry, policy, nowUtc);
            if (changed)
            {
                Hits.Remove(subject);
                RemoveEmptyEntryLocked(subject);
            }
            return changed;
        }

        /// <summary>
        /// Resets escalation after the configured incident-free duration.
        /// </summary>
        /// <param name="entry">Subject state.</param>
        /// <param name="policy">Resolved policy.</param>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <returns>True when escalation history was reset.</returns>
        private static bool ApplyRehabilitationFieldsLocked(
            BlackListSubjectEntry entry,
            BlackListPolicy policy,
            DateTime nowUtc)
        {
            if (entry.BanType.HasValue ||
                entry.EscalationLevel == 0 ||
                policy.EscalationResetAfterSeconds <= 0 ||
                !entry.LastIncidentUtc.HasValue ||
                entry.LastIncidentUtc.Value.AddSeconds(policy.EscalationResetAfterSeconds) > nowUtc)
            {
                return false;
            }

            entry.EscalationLevel = 0;
            entry.LastIncidentUtc = null;
            return true;
        }

        /// <summary>
        /// Removes expired in-memory hit windows.
        /// </summary>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <returns>True when persisted hit progress changed.</returns>
        private static bool PurgeExpiredHitsLocked(DateTime nowUtc)
        {
            List<BlackListSubject> expired = null;
            foreach (KeyValuePair<BlackListSubject, BlackListHitCounter> pair in Hits)
            {
                if (pair.Value.ExpiresUtc > nowUtc)
                    continue;
                if (expired == null)
                    expired = new List<BlackListSubject>();
                expired.Add(pair.Key);
            }

            if (expired == null)
                return false;
            foreach (BlackListSubject subject in expired)
            {
                Hits.Remove(subject);
                BlackListSubjectEntry entry;
                if (SubjectEntries.TryGetValue(subject, out entry))
                    ClearStoredHitProgress(entry);
                RemoveEmptyEntryLocked(subject);
            }
            return configuration.BlackListPersistHitProgress;
        }

        /// <summary>
        /// Removes an entry that contains neither a ban nor escalation history nor active hits.
        /// </summary>
        /// <param name="subject">Subject to consider for removal.</param>
        private static void RemoveEmptyEntryLocked(BlackListSubject subject)
        {
            BlackListSubjectEntry entry;
            if (SubjectEntries.TryGetValue(subject, out entry) &&
                !entry.BanType.HasValue &&
                entry.EscalationLevel == 0 &&
                entry.HitCount == 0 &&
                !Hits.ContainsKey(subject))
            {
                SubjectEntries.Remove(subject);
            }
        }

        /// <summary>
        /// Evicts one active hit window when the configured capacity is reached.
        /// </summary>
        private static void EnsureHitCapacityLocked()
        {
            int configuredMaximum = configuration.BlackListMaxTrackedSubjects > 0
                ? configuration.BlackListMaxTrackedSubjects
                : configuration.BlackListMaxTrackedHitAddresses;
            int maximum = Math.Max(1, configuredMaximum);
            if (Hits.Count < maximum)
                return;

            BlackListSubject subjectToRemove = Hits.Keys.FirstOrDefault();
            if (subjectToRemove != null)
            {
                Hits.Remove(subjectToRemove);
                BlackListSubjectEntry entry;
                if (SubjectEntries.TryGetValue(subjectToRemove, out entry))
                    ClearStoredHitProgress(entry);
                RemoveEmptyEntryLocked(subjectToRemove);
            }
        }
        #endregion

        #region Entry helpers
        /// <summary>
        /// Creates a new empty runtime entry for a subject.
        /// </summary>
        /// <param name="subject">Subject represented by the entry.</param>
        /// <param name="policyName">Associated policy name.</param>
        /// <returns>The initialized entry.</returns>
        private static BlackListSubjectEntry CreateEntry(BlackListSubject subject, string policyName)
        {
            return new BlackListSubjectEntry
            {
                SubjectType = subject.Type,
                SubjectIdentifier = subject.Identifier,
                PolicyName = policyName,
                EscalationLevel = 0
            };
        }

        /// <summary>
        /// Applies an active ban to an existing subject entry.
        /// </summary>
        /// <param name="entry">Entry receiving the ban.</param>
        /// <param name="banType">Ban type.</param>
        /// <param name="temporaryBanDuration">Optional temporary duration.</param>
        /// <param name="reason">Optional ban reason.</param>
        /// <param name="source">Component creating the ban.</param>
        /// <param name="nowUtc">Current UTC time.</param>
        private static void ApplyBanLocked(
            BlackListSubjectEntry entry,
            BlackListBanType banType,
            TimeSpan? temporaryBanDuration,
            string reason,
            string source,
            DateTime nowUtc)
        {
            TimeSpan duration = temporaryBanDuration ??
                                TimeSpan.FromSeconds(Math.Max(1, configuration.BlackListTemporaryBanDurationSeconds));
            if (banType == BlackListBanType.Temporary && duration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(temporaryBanDuration));

            entry.BanType = banType;
            entry.BanCreatedUtc = nowUtc;
            entry.BanExpiresUtc = banType == BlackListBanType.Temporary
                ? nowUtc.Add(duration)
                : (DateTime?)null;
            entry.LastIncidentUtc = nowUtc;
            entry.Reason = reason;
            entry.Source = source;
        }

        /// <summary>
        /// Resolves the duration used by a triggered escalation stage.
        /// </summary>
        /// <param name="stage">Triggered stage.</param>
        /// <param name="banTypeOverride">Optional ban type override.</param>
        /// <param name="durationOverride">Optional duration override.</param>
        /// <returns>The temporary duration, or null for a permanent ban.</returns>
        private static TimeSpan? ResolveStageDuration(
            BlackListEscalationStage stage,
            BlackListBanType? banTypeOverride,
            TimeSpan? durationOverride)
        {
            BlackListBanType effectiveType = banTypeOverride ?? stage.BanType;
            if (effectiveType == BlackListBanType.Permanent)
                return null;
            if (durationOverride.HasValue)
                return durationOverride;
            if (stage.BanType == BlackListBanType.Temporary && stage.BanDurationSeconds > 0)
                return TimeSpan.FromSeconds(stage.BanDurationSeconds);
            return null;
        }

        /// <summary>
        /// Clears active ban fields and starts rehabilitation after the ban end.
        /// </summary>
        /// <param name="entry">Entry whose active ban is cleared.</param>
        /// <param name="incidentEndUtc">UTC time from which rehabilitation begins.</param>
        private static void ClearActiveBanLocked(
            BlackListSubjectEntry entry,
            DateTime incidentEndUtc)
        {
            DateTime normalizedEnd = NormalizeUtc(incidentEndUtc);
            if (!entry.LastIncidentUtc.HasValue || entry.LastIncidentUtc.Value < normalizedEnd)
                entry.LastIncidentUtc = normalizedEnd;

            entry.BanType = null;
            entry.BanExpiresUtc = null;
            entry.BanCreatedUtc = null;
            entry.Reason = null;
            entry.Source = null;
        }

        /// <summary>
        /// Returns whether an entry contains an active ban at the supplied time.
        /// </summary>
        /// <param name="entry">Entry to inspect.</param>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <returns>True when the ban is active.</returns>
        private static bool IsActiveBanLocked(BlackListSubjectEntry entry, DateTime nowUtc)
        {
            if (entry == null || !entry.BanType.HasValue)
                return false;
            if (entry.BanType == BlackListBanType.Permanent)
                return true;
            return entry.BanExpiresUtc.HasValue && entry.BanExpiresUtc.Value > nowUtc;
        }

        /// <summary>
        /// Returns whether IP hit tracking is disabled for a non-public address.
        /// </summary>
        /// <param name="subject">Subject receiving hits.</param>
        /// <returns>True when the subject must not accumulate hits.</returns>
        private static bool ShouldIgnoreHits(BlackListSubject subject)
        {
            return subject.Type == BlackListSubject.IPAddressType &&
                   configuration.BlackListIgnoreNonPublicAddressesForHits &&
                   IPAddressUtilities.IsNonPublic(subject.Identifier);
        }

        /// <summary>
        /// Creates a result for a completed hit operation.
        /// </summary>
        /// <param name="subject">Target subject.</param>
        /// <param name="entry">Optional subject state.</param>
        /// <param name="policy">Resolved policy.</param>
        /// <param name="counter">Optional active hit counter.</param>
        /// <param name="banCreated">Whether this operation created a ban.</param>
        /// <param name="appliedStageIndex">Optional stage triggered by this operation.</param>
        /// <returns>The public hit result.</returns>
        private static BlackListHitResult CreateHitResult(
            BlackListSubject subject,
            BlackListSubjectEntry entry,
            BlackListPolicy policy,
            BlackListHitCounter counter,
            bool banCreated,
            int? appliedStageIndex)
        {
            BlackListEscalationStage stage = GetCurrentStage(policy, entry);
            bool isBanned = IsActiveBanLocked(entry, DateTime.UtcNow);
            return new BlackListHitResult
            {
                Subject = subject,
                PolicyName = policy.Name,
                EscalationLevel = entry != null ? entry.EscalationLevel : 0,
                AppliedStageIndex = appliedStageIndex,
                HitCount = counter != null ? counter.Count : 0,
                HitThreshold = stage != null ? stage.HitThreshold : 0,
                HitWindowExpiresUtc = counter != null ? counter.ExpiresUtc : (DateTime?)null,
                IsBanned = isBanned,
                BanCreated = banCreated,
                BanType = isBanned ? entry.BanType : null,
                BanExpiresUtc = isBanned ? entry.BanExpiresUtc : null,
                Reason = isBanned ? entry.Reason : null,
                Source = isBanned ? entry.Source : null
            };
        }

        /// <summary>
        /// Creates a public status from the current runtime state.
        /// </summary>
        /// <param name="subject">Target subject.</param>
        /// <param name="entry">Optional subject state.</param>
        /// <param name="policy">Resolved policy.</param>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <returns>The public blacklist status.</returns>
        private static BlackListStatus CreateStatusLocked(
            BlackListSubject subject,
            BlackListSubjectEntry entry,
            BlackListPolicy policy,
            DateTime nowUtc)
        {
            BlackListHitCounter counter;
            Hits.TryGetValue(subject, out counter);
            BlackListEscalationStage stage = GetCurrentStage(policy, entry);
            bool isBanned = IsActiveBanLocked(entry, nowUtc);
            return new BlackListStatus
            {
                Subject = subject,
                PolicyName = policy.Name,
                EscalationLevel = entry != null ? entry.EscalationLevel : 0,
                HitThreshold = stage != null ? stage.HitThreshold : 0,
                IsBanned = isBanned,
                BanType = isBanned ? entry.BanType : null,
                BanExpiresUtc = isBanned ? entry.BanExpiresUtc : null,
                Reason = isBanned ? entry.Reason : null,
                Source = isBanned ? entry.Source : null,
                HitCount = counter != null ? counter.Count : 0,
                HitWindowExpiresUtc = counter != null ? counter.ExpiresUtc : (DateTime?)null
            };
        }

        /// <summary>
        /// Sends typed ban feedback to a connected client before closing its socket.
        /// </summary>
        /// <param name="client">Client being banned.</param>
        /// <param name="info">Typed ban feedback.</param>
        private static void DisconnectBannedClient(ConnectedClient client, DisconnectInfo info)
        {
            try
            {
                if (client.TcpSocket != null && client.TcpSocket.Connected)
                {
                    client.AddTCPMessageAndWait(
                        ConnectionFeedbackProtocol.CreateDisconnectMessage(info, client.ID),
                        NetSquareServer.DisconnectNoticeTimeoutMs);
                }
            }
            catch (Exception ex)
            {
                Writer.Write(
                    "Fail to send ban feedback to client " + client.ID + " : " + ex.Message,
                    ConsoleColor.DarkYellow);
            }
            finally
            {
                try { client.TcpSocket.Close(); } catch { }
            }
        }

        /// <summary>
        /// Clones a mutable subject entry for rollback or serialization.
        /// </summary>
        /// <param name="entry">Entry to clone.</param>
        /// <returns>An independent copy.</returns>
        private static BlackListSubjectEntry CloneEntry(BlackListSubjectEntry entry)
        {
            return new BlackListSubjectEntry
            {
                SubjectType = entry.SubjectType,
                SubjectIdentifier = entry.SubjectIdentifier,
                PolicyName = entry.PolicyName,
                EscalationLevel = entry.EscalationLevel,
                LastIncidentUtc = entry.LastIncidentUtc,
                HitCount = entry.HitCount,
                HitWindowExpiresUtc = entry.HitWindowExpiresUtc,
                BanType = entry.BanType,
                BanExpiresUtc = entry.BanExpiresUtc,
                BanCreatedUtc = entry.BanCreatedUtc,
                Reason = entry.Reason,
                Source = entry.Source
            };
        }

        /// <summary>
        /// Copies an in-memory hit counter into its persistent subject entry.
        /// </summary>
        /// <param name="entry">Entry receiving the progress.</param>
        /// <param name="counter">Current hit counter.</param>
        private static void StoreHitProgress(
            BlackListSubjectEntry entry,
            BlackListHitCounter counter)
        {
            entry.HitCount = counter.Count;
            entry.HitWindowExpiresUtc = counter.ExpiresUtc;
        }

        /// <summary>
        /// Clears persisted hit progress from a subject entry.
        /// </summary>
        /// <param name="entry">Entry whose progress is cleared.</param>
        private static void ClearStoredHitProgress(BlackListSubjectEntry entry)
        {
            entry.HitCount = 0;
            entry.HitWindowExpiresUtc = null;
        }

        /// <summary>
        /// Restores hit state after a persistence failure.
        /// </summary>
        /// <param name="subject">Subject whose mutation failed.</param>
        /// <param name="hadEntryBeforeHit">Whether an entry existed before the hit.</param>
        /// <param name="entryBeforeHit">Entry snapshot captured before the hit.</param>
        /// <param name="counter">Counter mutated by the failed operation.</param>
        private static void RestoreHitMutationLocked(
            BlackListSubject subject,
            bool hadEntryBeforeHit,
            BlackListSubjectEntry entryBeforeHit,
            BlackListHitCounter counter)
        {
            if (!hadEntryBeforeHit)
            {
                Hits.Remove(subject);
                SubjectEntries.Remove(subject);
                return;
            }

            SubjectEntries[subject] = entryBeforeHit;
            if (entryBeforeHit.HitCount <= 0 || !entryBeforeHit.HitWindowExpiresUtc.HasValue)
            {
                Hits.Remove(subject);
                return;
            }

            counter.Count = entryBeforeHit.HitCount;
            counter.ExpiresUtc = entryBeforeHit.HitWindowExpiresUtc.Value;
            Hits[subject] = counter;
        }

        /// <summary>
        /// Validates that a public generic subject is present.
        /// </summary>
        /// <param name="subject">Subject to validate.</param>
        private static void ValidateSubject(BlackListSubject subject)
        {
            if (subject == null)
                throw new ArgumentNullException(nameof(subject));
        }

        /// <summary>
        /// Creates a normalized subject from persisted values.
        /// </summary>
        /// <param name="type">Persisted subject type.</param>
        /// <param name="identifier">Persisted identifier.</param>
        /// <returns>The normalized subject.</returns>
        private static BlackListSubject CreateNormalizedSubject(string type, string identifier)
        {
            return string.Equals(type, BlackListSubject.IPAddressType, StringComparison.OrdinalIgnoreCase)
                ? BlackListSubject.ForIPAddress(identifier)
                : new BlackListSubject(type, identifier);
        }

        /// <summary>
        /// Normalizes an optional timestamp to UTC.
        /// </summary>
        /// <param name="value">Timestamp to normalize.</param>
        /// <returns>The normalized UTC timestamp.</returns>
        private static DateTime? NormalizeUtc(DateTime? value)
        {
            if (!value.HasValue)
                return null;
            return NormalizeUtc(value.Value);
        }

        /// <summary>
        /// Normalizes a timestamp to UTC.
        /// </summary>
        /// <param name="value">Timestamp to normalize.</param>
        /// <returns>The normalized UTC timestamp.</returns>
        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }
        #endregion
    }
}
