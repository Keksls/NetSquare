using System;
using System.IO;

namespace NetSquare.Core.Configuration
{
    /// <summary>
    /// Loads and saves one strongly typed NetSquare JSON configuration.
    /// </summary>
    /// <typeparam name="TConfiguration">Configuration contract stored in the JSON file.</typeparam>
    public sealed class NetSquareConfigurationStore<TConfiguration>
        where TConfiguration : NetSquareConfiguration, new()
    {
        #region Properties
        /// <summary>
        /// Gets the absolute configuration file path.
        /// </summary>
        public string FilePath { get; private set; }

        /// <summary>
        /// Gets the active strongly typed configuration.
        /// </summary>
        public TConfiguration Configuration { get; private set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Loads an existing configuration or creates a default JSON file.
        /// </summary>
        /// <param name="filePath">Optional path. The default is config.json in the current directory.</param>
        public NetSquareConfigurationStore(string filePath = null)
        {
            // Resolve the path once so all consumers use a stable absolute configuration location.
            FilePath = Path.GetFullPath(
                filePath ?? Path.Combine(Environment.CurrentDirectory, "config.json"));
            Configuration = LoadOrCreate();
        }
        #endregion

        #region Persistence
        /// <summary>
        /// Reloads the configuration from disk.
        /// </summary>
        /// <returns>The reloaded strongly typed configuration.</returns>
        public TConfiguration Reload()
        {
            // Reloading requires an existing file because silently recreating it could hide deployment damage.
            if (!File.Exists(FilePath))
                throw new FileNotFoundException("The NetSquare configuration file was not found.", FilePath);

            Configuration = Deserialize(File.ReadAllText(FilePath));
            return Configuration;
        }

        /// <summary>
        /// Saves the active configuration to its JSON file.
        /// </summary>
        public void Save()
        {
            // Create an explicitly requested parent directory before writing the configuration file.
            string directoryPath = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string json = NetSquareJsonSerializer.Serialize(Configuration, typeof(TConfiguration));
            File.WriteAllText(FilePath, json);
        }

        /// <summary>
        /// Loads the configured file or creates it from the contract defaults.
        /// </summary>
        /// <returns>The active configuration.</returns>
        private TConfiguration LoadOrCreate()
        {
            if (File.Exists(FilePath))
                return Deserialize(File.ReadAllText(FilePath));

            // Persist a complete default file on first use.
            Configuration = new TConfiguration();
            Save();
            return Configuration;
        }

        /// <summary>
        /// Deserializes and validates a non-empty configuration document.
        /// </summary>
        /// <param name="json">Stored JSON document.</param>
        /// <returns>Deserialized configuration.</returns>
        private static TConfiguration Deserialize(string json)
        {
            // Reject empty JSON instead of allowing a null configuration to fail later.
            TConfiguration configuration = NetSquareJsonSerializer.Deserialize<TConfiguration>(json);
            if (configuration == null)
                throw new InvalidDataException("The NetSquare configuration file contains no configuration object.");

            return configuration;
        }
        #endregion
    }
}
