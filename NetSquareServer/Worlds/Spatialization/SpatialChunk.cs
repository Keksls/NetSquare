using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public class SpatialChunk
    {
        public short x;
        public short y;
        public Dictionary<uint, ChunkedClient> Clients;
        public List<StaticEntity> StaticEntities;
        private List<SpatialChunk> Neighbour;

        public SpatialChunk(short x, short y)
        {
            StaticEntities = new List<StaticEntity>();
            Clients = new Dictionary<uint, ChunkedClient>();
            Neighbour = new List<SpatialChunk>(9);
            this.x = x;
            this.y = y;
        }

        public void AddNeighbour(SpatialChunk chunk)
        {
            Neighbour.Add(chunk);
        }

        public void AddClient(ChunkedClient client)
        {
            foreach (SpatialChunk chunk in Neighbour)
                lock (chunk.Clients)
                    chunk.Clients.Add(client.ClientID, client);
        }

        public bool HasClient(uint clientID)
        {
            return Clients.ContainsKey(clientID);
        }

        public void RemoveClient(uint clientID)
        {
            foreach (SpatialChunk chunk in Neighbour)
                lock (chunk.Clients)
                    chunk.Clients.Remove(clientID);
        }
    }
}