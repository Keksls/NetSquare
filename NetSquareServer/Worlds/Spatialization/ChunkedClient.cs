using NetSquare.Core;
using System;
using System.Collections.Generic;
using System.Threading;

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
        /// Stores the reusable next visible-client ID set.
        /// </summary>
        internal HashSet<uint> NextVisibleIDs;
        /// <summary>
        /// Stores the immutable visible-client snapshot consumed by synchronization workers.
        /// </summary>
        private uint[] visibleIDsSnapshot;
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
            NextVisibleIDs = new HashSet<uint>();
            visibleIDsSnapshot = Array.Empty<uint>();
        }

        /// <summary>
        /// Executes the set chunk operation.
        /// </summary>
        public void SetChunk(short chunkX, short chunkY)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
        }

        /// <summary>
        /// Publishes an immutable visible-client snapshot when chunk visibility changes.
        /// </summary>
        /// <param name="visibleIDs">Latest visible-client identifiers.</param>
        internal void PublishVisibleIDs(HashSet<uint> visibleIDs)
        {
            if (visibleIDs == null || visibleIDs.Count == 0)
            {
                Volatile.Write(ref visibleIDsSnapshot, Array.Empty<uint>());
                return;
            }

            uint[] snapshot = new uint[visibleIDs.Count];
            visibleIDs.CopyTo(snapshot);
            Volatile.Write(ref visibleIDsSnapshot, snapshot);
        }

        /// <summary>
        /// Returns the immutable visible-client snapshot without allocating or holding the spatialization lock.
        /// </summary>
        /// <returns>Visible client identifiers for the latest completed spatialization pass.</returns>
        internal uint[] GetVisibleIDsSnapshot()
        {
            return Volatile.Read(ref visibleIDsSnapshot);
        }
    }
}
#endregion
