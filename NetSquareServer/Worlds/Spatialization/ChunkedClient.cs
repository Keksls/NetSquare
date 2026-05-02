using NetSquare.Core;
using System.Collections.Generic;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the chunked client component.
    /// </summary>
    public class ChunkedClient
    {
        /// <summary>
        /// Gets or sets the client id value.
        /// </summary>
        public uint ClientID { get; internal set; }
        /// <summary>
        /// Gets or sets the chunk x value.
        /// </summary>
        public short ChunkX { get; private set; }
        /// <summary>
        /// Gets or sets the chunk y value.
        /// </summary>
        public short ChunkY { get; private set; }
        /// <summary>
        /// Gets or sets the last position value.
        /// </summary>
        public NetsquareTransformFrame LastPosition { get; set; }
        /// <summary>
        /// Stores the visible i ds value.
        /// </summary>
        public HashSet<uint> VisibleIDs;
        /// <summary>
        /// Stores the sync root value.
        /// </summary>
        internal readonly object SyncRoot = new object();

        /// <summary>
        /// Initializes a new instance of the chunked client class.
        /// </summary>
        public ChunkedClient(uint clientID, short chunkX, short chunkY, NetsquareTransformFrame pos)
        {
            ClientID = clientID;
            ChunkX = chunkX;
            ChunkY = chunkY;
            LastPosition = new NetsquareTransformFrame(pos);
            VisibleIDs = new HashSet<uint>();
        }

        /// <summary>
        /// Executes the set chunk operation.
        /// </summary>
        public void SetChunk(short chunkX, short chunkY)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
        }
    }
}
#endregion
