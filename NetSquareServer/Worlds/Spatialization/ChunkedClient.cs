using NetSquare.Core;
using System.Collections.Generic;

namespace NetSquare.Server.Worlds
{
    public class ChunkedClient
    {
        public uint ClientID { get; private set; }
        public short ChunkX { get; private set; }
        public short ChunkY { get; private set; }
        public NetsquareTransformFrame LastPosition { get; set; }
        public HashSet<uint> VisibleIDs;

        public ChunkedClient(uint clientID, short chunkX, short chunkY, NetsquareTransformFrame pos)
        {
            ClientID = clientID;
            ChunkX = chunkX;
            ChunkY = chunkY;
            LastPosition = new NetsquareTransformFrame(pos);
            VisibleIDs = new HashSet<uint>();
        }

        public void SetChunk(short chunkX, short chunkY)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
        }
    }
}