using NetSquareCore;
using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public class ChunkedClient
    {
        public uint ClientID { get; private set; }
        public short ChunkX { get; private set; }
        public short ChunkY { get; private set; }
        public Transform Position { get; private set; }
        public Transform LastPosition { get; set; }
        public HashSet<uint> VisibleIDs;

        public ChunkedClient(uint clientID, short chunkX, short chunkY, Transform pos)
        {
            ClientID = clientID;
            ChunkX = chunkX;
            ChunkY = chunkY;
            Position = pos;
            LastPosition = new Transform(pos);
            VisibleIDs = new HashSet<uint>();
        }

        public void SetPostition(Transform position)
        {
            Position = position;
        }

        public void SetChunk(short chunkX, short chunkY)
        {
            ChunkX = chunkX;
            ChunkY= chunkY;
        }
    }
}