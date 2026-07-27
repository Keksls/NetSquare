using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using NetSquare.Core;

#region Source
namespace NetSquare.Server
{
    /// <summary>
    /// Represents the net square configuration component.
    /// </summary>
    public class NetSquareConfiguration : NetSquare.Core.Configuration.NetSquareConfiguration
    {
        /// <summary>
        /// the port to start server on
        /// </summary>
        public int Port { get; set; }
        /// <summary>
        /// Path to the PFX or PKCS#12 certificate containing the server private key.
        /// </summary>
        public string TLSCertificatePath { get; set; }
        /// <summary>
        /// Password protecting the TLS certificate file.
        /// </summary>
        public string TLSCertificatePassword { get; set; }
        /// <summary>
        /// If TRUE, the server consol will be lock to unselectable
        /// </summary>
        public bool LockConsole { get; set; }
        /// <summary>
        /// Path to the persisted generic blacklist subject state
        /// </summary>
        public string BlackListFilePath { get; set; }

        #region Blacklist
        public int BlackListHitThreshold { get; set; }
        public int BlackListHitWindowSeconds { get; set; }
        public BlackListBanType BlackListDefaultBanType { get; set; }
        public int BlackListTemporaryBanDurationSeconds { get; set; }
        public bool BlackListPersistTemporaryBans { get; set; }
        public bool BlackListPersistHitProgress { get; set; }
        public bool BlackListIgnoreNonPublicAddressesForHits { get; set; }
        public int BlackListMaxTrackedHitAddresses { get; set; }
        public string BlackListDefaultPolicyName { get; set; }
        public int BlackListMaxTrackedSubjects { get; set; }
        public List<BlackListPolicy> BlackListPolicies { get; set; }
        public int BlackListReputationCacheMinutes { get; set; }
        public int BlackListReputationFailureCacheSeconds { get; set; }
        public int BlackListMaxReputationCacheEntries { get; set; }
        public int BlackListMaxPendingReputationChecks { get; set; }
        public int BlackListExternalRequestTimeoutMilliseconds { get; set; }
        public bool AbuseIPDBEnabled { get; set; }
        public string AbuseIPDBApiKey { get; set; }
        public int AbuseIPDBConfidenceThreshold { get; set; }
        public int AbuseIPDBMaxAgeInDays { get; set; }
        public int AbuseIPDBMaximumDailyChecks { get; set; }
        public bool BlockListDeEnabled { get; set; }
        public int BlockListDeMinimumAttacks { get; set; }
        public int BlockListDeMinimumReports { get; set; }
        public bool SpamhausDropEnabled { get; set; }
        public int SpamhausDropRefreshHours { get; set; }
        public bool DShieldEnabled { get; set; }
        public int DShieldRefreshHours { get; set; }
        #endregion

        #region Heartbeat
        /// <summary>
        /// Gets or sets whether connected clients must send TCP heartbeats.
        /// </summary>
        public bool HeartbeatEnabled { get; set; }
        /// <summary>
        /// Gets or sets the interval communicated to clients, in milliseconds.
        /// </summary>
        public int HeartbeatIntervalMilliseconds { get; set; }
        /// <summary>
        /// Gets or sets the maximum accepted TCP silence, in milliseconds.
        /// </summary>
        public int HeartbeatTimeoutMilliseconds { get; set; }
        #endregion

        /// <summary>
        /// Number of threads for message action handling
        /// </summary>
        public int NbQueueThreads { get; set; }
        /// <summary>
        /// Maximum number of received messages retained by each processing worker.
        /// </summary>
        public int MessageQueueCapacity { get; set; }
        /// <summary>
        /// Maximum graceful worker shutdown duration in milliseconds.
        /// </summary>
        public int WorkerStopTimeoutMilliseconds { get; set; }
        /// <summary>
        /// Receiving buffer max size
        /// </summary>
        public int ReceivingBufferSize { get; set; }
        /// <summary>
        /// Number of threads for TcpListners message sending
        /// </summary>
        public int NbSendingThreads { get; set; }
        /// <summary>
        /// Frequency of var synchronization
        /// </summary>
        public int SynchronizingFrequency { get; set; }
        /// <summary>
        /// Frequency of loop time in Hz
        /// </summary>
        public float UpdateFrequencyHz { get; set; }

        /// <summary>
        /// Initializes a new instance of the net square configuration class.
        /// </summary>
        public NetSquareConfiguration()
        {
            Port = 5555;
            TLSCertificatePath = string.Empty;
            TLSCertificatePassword = string.Empty;
            NbSendingThreads = 1;
            NbQueueThreads = 1;
            ReceivingBufferSize = 1024;
            MessageQueueCapacity = 8192;
            WorkerStopTimeoutMilliseconds = 5000;
            LockConsole = false;
            BlackListFilePath = Environment.CurrentDirectory + @"\BlackListedIP.json";
            SetBlackListDefaults();
            SetHeartbeatDefaults();
            UpdateFrequencyHz = 10;
        }

        /// <summary>
        /// Applies defaults before JSON properties are deserialized.
        /// </summary>
        /// <param name="context">Serialization context.</param>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // Existing config files omit new properties, so seed defaults before applying their stored values.
            SetBlackListDefaults();
            SetHeartbeatDefaults();
        }

        /// <summary>
        /// Applies default values for the blacklist and reputation system.
        /// </summary>
        private void SetBlackListDefaults()
        {
            BlackListHitThreshold = 10;
            BlackListHitWindowSeconds = 600;
            BlackListDefaultBanType = BlackListBanType.Temporary;
            BlackListTemporaryBanDurationSeconds = 3600;
            BlackListPersistTemporaryBans = true;
            BlackListPersistHitProgress = true;
            BlackListIgnoreNonPublicAddressesForHits = true;
            BlackListMaxTrackedHitAddresses = 10000;
            BlackListReputationCacheMinutes = 60;
            BlackListReputationFailureCacheSeconds = 60;
            BlackListMaxReputationCacheEntries = 10000;
            BlackListMaxPendingReputationChecks = 64;
            BlackListExternalRequestTimeoutMilliseconds = 3000;
            AbuseIPDBEnabled = false;
            AbuseIPDBApiKey = string.Empty;
            AbuseIPDBConfidenceThreshold = 75;
            AbuseIPDBMaxAgeInDays = 90;
            AbuseIPDBMaximumDailyChecks = 1000;
            BlockListDeEnabled = false;
            BlockListDeMinimumAttacks = 1;
            BlockListDeMinimumReports = 1;
            SpamhausDropEnabled = false;
            SpamhausDropRefreshHours = 24;
            DShieldEnabled = false;
            DShieldRefreshHours = 1;
            BlackListDefaultPolicyName = "default";
            BlackListMaxTrackedSubjects = 10000;
            BlackListPolicies = new List<BlackListPolicy>();
        }

        /// <summary>
        /// Applies the server-owned heartbeat policy defaults.
        /// </summary>
        private void SetHeartbeatDefaults()
        {
            // Keep the default liveness timeout at three times the heartbeat cadence.
            HeartbeatEnabled = true;
            HeartbeatIntervalMilliseconds = 10000;
            HeartbeatTimeoutMilliseconds = 30000;
        }

        /// <summary>
        /// Validates server settings that must be safe before accepting clients.
        /// </summary>
        public void Validate()
        {
            // Disabled heartbeats do not use their timing values.
            if (!HeartbeatEnabled)
                return;
            if (HeartbeatIntervalMilliseconds < NetSquareHandshakeProtocol.MinimumHeartbeatIntervalMilliseconds)
            {
                throw new InvalidOperationException(
                    "HeartbeatIntervalMilliseconds must be at least " +
                    NetSquareHandshakeProtocol.MinimumHeartbeatIntervalMilliseconds + ".");
            }
            if (HeartbeatTimeoutMilliseconds <= HeartbeatIntervalMilliseconds)
            {
                throw new InvalidOperationException(
                    "HeartbeatTimeoutMilliseconds must be greater than HeartbeatIntervalMilliseconds.");
            }
        }

        /// <summary>
        /// Executes the to string operation.
        /// </summary>
        public override string ToString()
        {
            // Display every readable scalar property, including properties declared by consuming projects.
            StringBuilder sb = new StringBuilder();
            foreach (PropertyInfo property in GetType().GetProperties())
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                    continue;

                sb.Append(" - ");
                sb.Append(property.Name);
                sb.Append(" : ");

                if (IsSensitiveProperty(property.Name))
                {
                    // Never expose credentials when the complete configuration is written to the server log.
                    object secretValue = property.GetValue(this);
                    sb.AppendLine(secretValue == null || string.IsNullOrEmpty(secretValue.ToString())
                        ? "<empty>"
                        : "<redacted>");
                    continue;
                }

                if (!IsScalarType(property.PropertyType))
                {
                    // Do not invoke ToString on complex objects because their graph may reference this configuration.
                    sb.Append('<');
                    sb.Append(property.PropertyType.Name);
                    sb.AppendLine(">");
                    continue;
                }

                object value = property.GetValue(this);
                sb.AppendLine(value == null
                    ? "null"
                    : Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns whether a property type can be formatted without traversing an object graph.
        /// </summary>
        private static bool IsScalarType(Type type)
        {
            // Nullable scalar values use the same formatting rules as their underlying type.
            Type scalarType = Nullable.GetUnderlyingType(type) ?? type;
            return scalarType.IsPrimitive ||
                   scalarType.IsEnum ||
                   scalarType == typeof(string) ||
                   scalarType == typeof(decimal) ||
                   scalarType == typeof(DateTime) ||
                   scalarType == typeof(DateTimeOffset) ||
                   scalarType == typeof(TimeSpan) ||
                   scalarType == typeof(Guid);
        }

        /// <summary>
        /// Returns whether a configuration property contains a credential.
        /// </summary>
        /// <param name="propertyName">Configuration property name.</param>
        /// <returns>True when the value must be redacted from logs.</returns>
        private static bool IsSensitiveProperty(string propertyName)
        {
            // Cover conventional credential suffixes used by project-defined configuration classes too.
            return propertyName.IndexOf("ApiKey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   propertyName.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   propertyName.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   propertyName.IndexOf("Token", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endregion
