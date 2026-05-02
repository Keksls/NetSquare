using System.Collections.Generic;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents a snapshot of all world debug data.
    /// </summary>
    public sealed class NetSquareWorldSnapshot
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
        /// Stores the connected client count.
        /// </summary>
        public int ClientCount;
        /// <summary>
        /// Stores the maximum clients allowed in this world.
        /// </summary>
        public ushort MaxClientsInWorld;
        /// <summary>
        /// Stores whether the synchronizer is enabled.
        /// </summary>
        public bool UseSynchronizer;
        /// <summary>
        /// Stores whether the spatializer is enabled.
        /// </summary>
        public bool UseSpatializer;
        /// <summary>
        /// Stores client debug snapshots.
        /// </summary>
        public List<NetSquareWorldClientSnapshot> Clients = new List<NetSquareWorldClientSnapshot>();
        /// <summary>
        /// Stores spatializer debug data.
        /// </summary>
        public NetSquareSpatializerSnapshot Spatializer;
        #endregion
    }

    /// <summary>
    /// Represents a snapshot of one world client.
    /// </summary>
    public sealed class NetSquareWorldClientSnapshot
    {
        #region Variables
        /// <summary>
        /// Stores the client id.
        /// </summary>
        public uint ClientID;
        /// <summary>
        /// Stores the last known x position.
        /// </summary>
        public float X;
        /// <summary>
        /// Stores the last known y position.
        /// </summary>
        public float Y;
        /// <summary>
        /// Stores the last known z position.
        /// </summary>
        public float Z;
        /// <summary>
        /// Stores visible client ids for this client.
        /// </summary>
        public List<uint> VisibleClientIDs = new List<uint>();
        /// <summary>
        /// Stores pending synchronization frame count for this client.
        /// </summary>
        public int PendingFrameCount;
        #endregion
    }

    /// <summary>
    /// Represents a snapshot of spatializer internals.
    /// </summary>
    public class NetSquareSpatializerSnapshot
    {
        #region Variables
        /// <summary>
        /// Stores the spatializer type.
        /// </summary>
        public string Type;
        /// <summary>
        /// Stores the synchronization frequency in milliseconds.
        /// </summary>
        public int SynchFrequency;
        /// <summary>
        /// Stores the spatialization frequency in milliseconds.
        /// </summary>
        public int SpatializationFrequency;
        /// <summary>
        /// Stores static entity count.
        /// </summary>
        public uint StaticEntitiesCount;
        /// <summary>
        /// Stores total pending frame count.
        /// </summary>
        public int PendingFrameCount;
        /// <summary>
        /// Stores the maximum stored frames per client.
        /// </summary>
        public int MaxStoredFramesPerClient;
        /// <summary>
        /// Stores simple spatializer view distance.
        /// </summary>
        public float MaxViewDistance;
        /// <summary>
        /// Stores simple spatializer visibility hysteresis.
        /// </summary>
        public float VisibilityHysteresis;
        /// <summary>
        /// Stores chunked spatializer chunk size.
        /// </summary>
        public float ChunkSize;
        /// <summary>
        /// Stores chunked spatializer hysteresis.
        /// </summary>
        public float ChunkHysteresis;
        /// <summary>
        /// Stores the minimum world x bound.
        /// </summary>
        public float MinX;
        /// <summary>
        /// Stores the minimum world y/z bound.
        /// </summary>
        public float MinY;
        /// <summary>
        /// Stores the maximum world x bound.
        /// </summary>
        public float MaxX;
        /// <summary>
        /// Stores the maximum world y/z bound.
        /// </summary>
        public float MaxY;
        /// <summary>
        /// Stores chunk grid width.
        /// </summary>
        public int ChunkWidth;
        /// <summary>
        /// Stores chunk grid height.
        /// </summary>
        public int ChunkHeight;
        /// <summary>
        /// Stores visible client ids by source client id.
        /// </summary>
        public Dictionary<uint, List<uint>> VisibleClientsByClientID = new Dictionary<uint, List<uint>>();
        /// <summary>
        /// Stores pending frame counts by source client id.
        /// </summary>
        public Dictionary<uint, int> PendingFramesByClientID = new Dictionary<uint, int>();
        /// <summary>
        /// Stores chunk debug snapshots.
        /// </summary>
        public List<NetSquareSpatialChunkSnapshot> Chunks = new List<NetSquareSpatialChunkSnapshot>();
        #endregion
    }

    /// <summary>
    /// Represents a snapshot of one spatial chunk.
    /// </summary>
    public sealed class NetSquareSpatialChunkSnapshot
    {
        #region Variables
        /// <summary>
        /// Stores the chunk x coordinate.
        /// </summary>
        public short X;
        /// <summary>
        /// Stores the chunk y coordinate.
        /// </summary>
        public short Y;
        /// <summary>
        /// Stores the number of clients visible from this chunk cell.
        /// </summary>
        public int ClientCount;
        /// <summary>
        /// Stores the static entity count in this chunk.
        /// </summary>
        public int StaticEntityCount;
        #endregion
    }
}
#endregion
