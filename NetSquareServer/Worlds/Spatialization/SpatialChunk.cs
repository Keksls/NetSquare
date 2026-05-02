using System.Collections.Concurrent;
using System.Collections.Generic;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the spatial chunk component.
    /// </summary>
    public class SpatialChunk
    {
        /// <summary>
        /// Stores the x value.
        /// </summary>
        public short x;
        /// <summary>
        /// Stores the y value.
        /// </summary>
        public short y;
        /// <summary>
        /// Stores the clients value.
        /// </summary>
        public ConcurrentDictionary<uint, ChunkedClient> Clients;
        /// <summary>
        /// Stores the static entities value.
        /// </summary>
        public List<StaticEntity> StaticEntities;
        /// <summary>
        /// Stores the neighbour value.
        /// </summary>
        private List<SpatialChunk> Neighbour;
        /// <summary>
        /// Stores the static entities lock value.
        /// </summary>
        private readonly object staticEntitiesLock = new object();

        /// <summary>
        /// Initializes a new instance of the spatial chunk class.
        /// </summary>
        public SpatialChunk(short x, short y)
        {
            StaticEntities = new List<StaticEntity>();
            Clients = new ConcurrentDictionary<uint, ChunkedClient>();
            Neighbour = new List<SpatialChunk>(9);
            this.x = x;
            this.y = y;
        }

        /// <summary>
        /// Executes the add neighbour operation.
        /// </summary>
        public void AddNeighbour(SpatialChunk chunk)
        {
            Neighbour.Add(chunk);
        }

        /// <summary>
        /// Executes the add client operation.
        /// </summary>
        public void AddClient(ChunkedClient client)
        {
            foreach (SpatialChunk chunk in Neighbour)
                chunk.Clients[client.ClientID] = client;
        }

        /// <summary>
        /// Executes the has client operation.
        /// </summary>
        public bool HasClient(uint clientID)
        {
            return Clients.ContainsKey(clientID);
        }

        /// <summary>
        /// Executes the remove client operation.
        /// </summary>
        public void RemoveClient(uint clientID)
        {
            foreach (SpatialChunk chunk in Neighbour)
            {
                ChunkedClient removed;
                chunk.Clients.TryRemove(clientID, out removed);
            }
        }

        /// <summary>
        /// Executes the add static entity operation.
        /// </summary>
        public void AddStaticEntity(StaticEntity entity)
        {
            lock (staticEntitiesLock)
                StaticEntities.Add(entity);
        }

        /// <summary>
        /// Executes the get static entities snapshot operation.
        /// </summary>
        public List<StaticEntity> GetStaticEntitiesSnapshot()
        {
            lock (staticEntitiesLock)
                return new List<StaticEntity>(StaticEntities);
        }
    }
}
#endregion
