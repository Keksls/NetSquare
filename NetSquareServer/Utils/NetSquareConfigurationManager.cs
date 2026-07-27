using System;
using System.IO;
using NetSquare.Core.Configuration;

#region Source
namespace NetSquare.Server
{
    /// <summary>
    /// Exposes the server configuration stored by the shared Core configuration system.
    /// </summary>
    public static class NetSquareConfigurationManager
    {
        #region Variables
        private static readonly object SynchronizationLock = new object();
        private static NetSquareConfiguration configuration;
        private static Type configurationType;
        private static string configurationPath;
        private static Action saveConfiguration;
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the manager with the server configuration type declared by the consuming project.
        /// </summary>
        /// <typeparam name="T">Configuration type derived from <see cref="NetSquareConfiguration"/>.</typeparam>
        /// <param name="filePath">Optional configuration file path. The default is config.json in the current directory.</param>
        public static void Initialize<T>(string filePath = null)
            where T : NetSquareConfiguration, new()
        {
            // Resolve the path before entering the lock so every comparison uses an absolute path.
            string resolvedPath = Path.GetFullPath(
                filePath ?? Path.Combine(Environment.CurrentDirectory, "config.json"));

            lock (SynchronizationLock)
            {
                if (configuration != null)
                {
                    EnsureSameInitialization(typeof(T), resolvedPath);
                    return;
                }

                // Each manager owns a dedicated generic store, so client and server configurations cannot collide.
                NetSquareConfigurationStore<T> store =
                    new NetSquareConfigurationStore<T>(resolvedPath);
                store.Configuration.Validate();
                configuration = store.Configuration;
                configurationType = typeof(T);
                configurationPath = store.FilePath;
                saveConfiguration = store.Save;
                ResolveRuntimePaths(configuration);
            }
        }

        /// <summary>
        /// Verifies that repeated initialization uses the same configuration contract and file.
        /// </summary>
        /// <param name="requestedType">Requested concrete configuration type.</param>
        /// <param name="requestedPath">Requested absolute JSON path.</param>
        private static void EnsureSameInitialization(Type requestedType, string requestedPath)
        {
            // Reinitializing another contract would invalidate references already used by the running server.
            if (configurationType != requestedType ||
                !string.Equals(configurationPath, requestedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "NetSquare server configuration is already initialized with type " +
                    configurationType.FullName + " at " + configurationPath + ".");
            }
        }
        #endregion

        #region Access
        /// <summary>
        /// Gets the initialized configuration as the requested base or concrete configuration type.
        /// </summary>
        /// <typeparam name="TConfiguration">Expected server configuration type.</typeparam>
        /// <returns>The active configuration instance.</returns>
        public static TConfiguration Get<TConfiguration>()
            where TConfiguration : NetSquareConfiguration
        {
            lock (SynchronizationLock)
            {
                EnsureInitialized();

                // Allow NetSquare internals to request the base type while consumers keep strongly typed access.
                TConfiguration typedConfiguration = configuration as TConfiguration;
                if (typedConfiguration == null)
                {
                    throw new InvalidOperationException(
                        "The active NetSquare server configuration has type " +
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
            // Explicit initialization guarantees that the correct project-defined type is deserialized.
            if (configuration == null)
            {
                throw new InvalidOperationException(
                    "NetSquare server configuration is not initialized. Call " +
                    "NetSquareConfigurationManager.Initialize<TConfiguration>() before creating the server.");
            }
        }
        #endregion

        #region Persistence
        /// <summary>
        /// Saves the active server configuration through the shared Core store.
        /// </summary>
        public static void Save()
        {
            lock (SynchronizationLock)
            {
                EnsureInitialized();
                // Never persist a server configuration known to be unusable.
                configuration.Validate();
                // Resolve tokens set by the consuming project before configuration becomes runtime state.
                ResolveRuntimePaths(configuration);
                saveConfiguration();
            }
        }

        /// <summary>
        /// Resolves server-specific path tokens that are meaningful only at runtime.
        /// </summary>
        /// <param name="activeConfiguration">Configuration whose server paths must be resolved.</param>
        private static void ResolveRuntimePaths(NetSquareConfiguration activeConfiguration)
        {
            // Keep the existing current-directory token available to server configuration files.
            if (!string.IsNullOrEmpty(activeConfiguration.BlackListFilePath))
            {
                activeConfiguration.BlackListFilePath = activeConfiguration.BlackListFilePath.Replace(
                    "[current]",
                    Environment.CurrentDirectory);
            }

            if (!string.IsNullOrEmpty(activeConfiguration.TLSCertificatePath))
            {
                activeConfiguration.TLSCertificatePath = activeConfiguration.TLSCertificatePath.Replace(
                    "[current]",
                    Environment.CurrentDirectory);
            }
        }
        #endregion
    }
}
#endregion
