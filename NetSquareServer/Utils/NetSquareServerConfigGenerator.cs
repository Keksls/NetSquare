using System.Collections.Generic;
using System.IO;
using NetSquare.Core;
using NetSquare.Server.Worlds;

#region Source
namespace NetSquare.Server
{
    /// <summary>
    /// Generates clean starter JSON configuration files for NetSquare servers.
    /// </summary>
    public static class NetSquareServerConfigGenerator
    {
        #region Public Methods
        /// <summary>
        /// Creates a default generated server configuration model.
        /// </summary>
        /// <returns>Generated configuration model.</returns>
        public static NetSquareGeneratedServerConfig CreateDefault()
        {
            NetSquareGeneratedServerConfig config = new NetSquareGeneratedServerConfig();
            config.Server = NetSquareConfigurationManager.Configuration ?? new NetSquareConfiguration();
            config.Protocol = NetSquareProtocoleType.TCP_AND_UDP.ToString();
            config.Worlds.Add(new NetSquareGeneratedWorldConfig
            {
                ID = 1,
                Name = "Main",
                MaxClients = 128,
                StartSynchronizer = true,
                SynchronizerFrequencyHz = 20,
                Spatializer = new NetSquareGeneratedSpatializerConfig
                {
                    Type = SpatializerType.ChunkedSpatializer.ToString(),
                    SpatializationFrequencyHz = 10,
                    SynchronizationFrequencyHz = 20,
                    ChunkSize = 10f,
                    MinX = -100f,
                    MinY = -100f,
                    MaxX = 100f,
                    MaxY = 100f,
                    ChunkHysteresis = 1f
                }
            });
            return config;
        }

        /// <summary>
        /// Generates a default server configuration as formatted JSON.
        /// </summary>
        /// <returns>Generated JSON.</returns>
        public static string GenerateDefaultJson()
        {
            return NetSquareJson.Serialize(CreateDefault());
        }

        /// <summary>
        /// Writes a default server configuration to disk.
        /// </summary>
        /// <param name="path">Destination path.</param>
        public static void WriteDefault(string path)
        {
            File.WriteAllText(path, GenerateDefaultJson());
        }
        #endregion

        #region Formatting
        /// <summary>
        /// Formats compact JSON with indentation.
        /// </summary>
        /// <param name="json">Compact JSON.</param>
        /// <returns>Formatted JSON.</returns>
        private static string FormatJson(string json)
        {
            int indentation = 0;
            bool inString = false;
            System.Text.StringBuilder builder = new System.Text.StringBuilder(json.Length * 2);
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                    inString = !inString;

                if (!inString && (c == '{' || c == '['))
                {
                    builder.Append(c);
                    builder.AppendLine();
                    indentation++;
                    AppendIndent(builder, indentation);
                }
                else if (!inString && (c == '}' || c == ']'))
                {
                    builder.AppendLine();
                    indentation--;
                    AppendIndent(builder, indentation);
                    builder.Append(c);
                }
                else if (!inString && c == ',')
                {
                    builder.Append(c);
                    builder.AppendLine();
                    AppendIndent(builder, indentation);
                }
                else if (!inString && c == ':')
                {
                    builder.Append(": ");
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Appends indentation spaces.
        /// </summary>
        /// <param name="builder">Builder to append to.</param>
        /// <param name="indentation">Indentation level.</param>
        private static void AppendIndent(System.Text.StringBuilder builder, int indentation)
        {
            for (int i = 0; i < indentation; i++)
                builder.Append("  ");
        }
        #endregion
    }

    /// <summary>
    /// Represents a generated server configuration.
    /// </summary>
    public sealed class NetSquareGeneratedServerConfig
    {
        #region Variables
        /// <summary>
        /// Stores base server settings.
        /// </summary>
        public NetSquareConfiguration Server = new NetSquareConfiguration();
        /// <summary>
        /// Stores the protocol name.
        /// </summary>
        public string Protocol;
        /// <summary>
        /// Stores generated world configurations.
        /// </summary>
        public List<NetSquareGeneratedWorldConfig> Worlds = new List<NetSquareGeneratedWorldConfig>();
        #endregion
    }

    /// <summary>
    /// Represents one generated world configuration.
    /// </summary>
    public sealed class NetSquareGeneratedWorldConfig
    {
        #region Variables
        /// <summary>
        /// Stores the world id.
        /// </summary>
        public ushort ID;
        /// <summary>
        /// Stores the world name.
        /// </summary>
        public string Name;
        /// <summary>
        /// Stores the maximum client count.
        /// </summary>
        public ushort MaxClients;
        /// <summary>
        /// Stores whether the synchronizer should be started.
        /// </summary>
        public bool StartSynchronizer;
        /// <summary>
        /// Stores synchronizer frequency in hertz.
        /// </summary>
        public int SynchronizerFrequencyHz;
        /// <summary>
        /// Stores spatializer settings.
        /// </summary>
        public NetSquareGeneratedSpatializerConfig Spatializer;
        #endregion
    }

    /// <summary>
    /// Represents one generated spatializer configuration.
    /// </summary>
    public sealed class NetSquareGeneratedSpatializerConfig
    {
        #region Variables
        /// <summary>
        /// Stores the spatializer type.
        /// </summary>
        public string Type;
        /// <summary>
        /// Stores spatialization frequency in hertz.
        /// </summary>
        public float SpatializationFrequencyHz;
        /// <summary>
        /// Stores synchronization frequency in hertz.
        /// </summary>
        public float SynchronizationFrequencyHz;
        /// <summary>
        /// Stores simple spatializer maximum view distance.
        /// </summary>
        public float MaxViewDistance;
        /// <summary>
        /// Stores simple spatializer visibility hysteresis.
        /// </summary>
        public float VisibilityHysteresis;
        /// <summary>
        /// Stores chunk size.
        /// </summary>
        public float ChunkSize;
        /// <summary>
        /// Stores minimum x bound.
        /// </summary>
        public float MinX;
        /// <summary>
        /// Stores minimum y/z bound.
        /// </summary>
        public float MinY;
        /// <summary>
        /// Stores maximum x bound.
        /// </summary>
        public float MaxX;
        /// <summary>
        /// Stores maximum y/z bound.
        /// </summary>
        public float MaxY;
        /// <summary>
        /// Stores chunk hysteresis.
        /// </summary>
        public float ChunkHysteresis;
        #endregion
    }
}
#endregion
