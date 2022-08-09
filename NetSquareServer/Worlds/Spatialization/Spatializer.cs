using NetSquare.Core;
using NetSquareCore;
using System;
using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public abstract class Spatializer
    {
        public NetSquareWorld World { get; private set; }
        public uint StaticEntitiesCount { get; internal set; }

        public Spatializer(NetSquareWorld world)
        {
            World = world;
        }

        public static ChunkedSpatializer GetChunkedSpatializer(NetSquareWorld world, float chunkSize, float xStart, float yStart, float xEnd, float yEnd)
        {
            return new ChunkedSpatializer(world, chunkSize, xStart, yStart, xEnd, yEnd);
        }

        public static SimpleSpatializer GetSimpleSpatializer(NetSquareWorld world, float maxViewDistance)
        {
            return new SimpleSpatializer(world, maxViewDistance);
        }

        /// <summary>
        /// Add a client to this spatializer
        /// </summary>
        /// <param name="client">ID of the client to add</param>
        public abstract void AddClient(ConnectedClient client);

        /// <summary>
        /// add a client to this spatializer and set his position
        /// </summary>
        /// <param name="clientID">ID of the client to add</param>
        /// <param name="pos">spawn position</param>
        public abstract void AddClient(ConnectedClient client, Position pos);

        /// <summary>
        /// Remove a client from the spatializer
        /// </summary>
        /// <param name="clientID">ID of the client to remove</param>
        public abstract void RemoveClient(uint clientID);

        /// <summary>
        /// set a client position
        /// </summary>
        /// <param name="clientID">id of the client that just moved</param>
        /// <param name="pos">position</param>
        public abstract void SetClientPosition(uint clientID, Position pos);

        /// <summary>
        /// Get a client position
        /// </summary>
        /// <param name="clientID">id of the clienty to get position</param>
        /// <returns>position of the  client</returns>
        public abstract Position GetClientPosition(uint clientID);

        /// <summary>
        /// get all visible clients for a given client, according to a maximum view distance
        /// </summary>
        /// <param name="clientID">ID of the client to get visibles</param>
        /// <param name="maxDistance">maximum view distance of  the client</param>
        /// <returns></returns>
        public abstract HashSet<uint> GetVisibleClients(uint clientID);

        /// <summary>
        /// Start spatializer loop that handle clients Spawn and unspawn
        /// </summary>
        /// <param name="frequency">frequency of loop processing</param>
        public abstract void StartSpatializer(float frequency);

        /// <summary>
        /// Stop spatializer Spawn / Unspawn loop
        /// </summary>
        public abstract void StopSpatializer();

        public abstract void ForEach(Action<uint, IEnumerable<uint>> callback);

        public abstract void AddStaticEntity(short type, uint id, Position pos);
    }

    public enum SpatializerType
    {
        SimpleSpatializer = 0,
        ChunkedSpatializer = 1
    }
}