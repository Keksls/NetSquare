using System;
using System.IO;
using NetSquare.Core.Configuration;

namespace NetSquare.Client
{
    /// <summary>
    /// Exposes the client configuration stored by the shared Core configuration system.
    /// </summary>
    public static class NetSquareClientConfigurationManager
    {
        #region Variables
        private static readonly object SynchronizationLock = new object();
        private static NetSquareClientConfiguration configuration;
        private static Type configurationType;
        private static string configurationPath;
        private static Action saveConfiguration;
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the manager with the client configuration type declared by the consuming project.
        /// </summary>
        /// <typeparam name="T">Configuration type derived from <see cref="NetSquareClientConfiguration"/>.</typeparam>
        /// <param name="filePath">Optional path. The default is client.config.json in the current directory.</param>
        public static void Initialize<T>(string filePath = null)
            where T : NetSquareClientConfiguration, new()
        {
            // Use a client-specific default name so a hosted server and client never share one JSON by accident.
            string resolvedPath = Path.GetFullPath(
                filePath ?? Path.Combine(Environment.CurrentDirectory, "client.config.json"));

            lock (SynchronizationLock)
            {
                if (configuration != null)
                {
                    EnsureSameInitialization(typeof(T), resolvedPath);
                    return;
                }

                // The generic Core store owns all JSON persistence while this manager owns client isolation.
                NetSquareConfigurationStore<T> store =
                    new NetSquareConfigurationStore<T>(resolvedPath);
                configuration = store.Configuration;
                configuration.Validate();
                configurationType = typeof(T);
                configurationPath = store.FilePath;
                saveConfiguration = store.Save;
            }
        }

        /// <summary>
        /// Verifies that repeated initialization uses the same client contract and file.
        /// </summary>
        /// <param name="requestedType">Requested concrete configuration type.</param>
        /// <param name="requestedPath">Requested absolute JSON path.</param>
        private static void EnsureSameInitialization(Type requestedType, string requestedPath)
        {
            // A running client configuration manager cannot safely change its contract or backing file.
            if (configurationType != requestedType ||
                !string.Equals(configurationPath, requestedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "NetSquare client configuration is already initialized with type " +
                    configurationType.FullName + " at " + configurationPath + ".");
            }
        }
        #endregion

        #region Access
        /// <summary>
        /// Gets the initialized client configuration as its base or concrete type.
        /// </summary>
        /// <typeparam name="TConfiguration">Expected client configuration type.</typeparam>
        /// <returns>The active client configuration.</returns>
        public static TConfiguration Get<TConfiguration>()
            where TConfiguration : NetSquareClientConfiguration
        {
            lock (SynchronizationLock)
            {
                EnsureInitialized();

                // Preserve strongly typed access for project-specific client settings.
                TConfiguration typedConfiguration = configuration as TConfiguration;
                if (typedConfiguration == null)
                {
                    throw new InvalidOperationException(
                        "The active NetSquare client configuration has type " +
                        configurationType.FullName + " and cannot be accessed as " +
                        typeof(TConfiguration).FullName + ".");
                }

                return typedConfiguration;
            }
        }

        /// <summary>
        /// Throws when the manager is used before explicit initialization.
        /// </summary>
        private static void EnsureInitialized()
        {
            // Explicit initialization selects the concrete project configuration contract.
            if (configuration == null)
            {
                throw new InvalidOperationException(
                    "NetSquare client configuration is not initialized. Call " +
                    "NetSquareClientConfigurationManager.Initialize<TConfiguration>() first.");
            }
        }
        #endregion

        #region Persistence
        /// <summary>
        /// Validates and saves the active client configuration.
        /// </summary>
        public static void Save()
        {
            lock (SynchronizationLock)
            {
                EnsureInitialized();
                // Never persist a client configuration known to be unusable.
                configuration.Validate();
                saveConfiguration();
            }
        }
        #endregion
    }
}
