using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the simple spatializer component.
    /// </summary>
    public class SimpleSpatializer : Spatializer
    {
        /// <summary>
        /// Stores the clients value.
        /// </summary>
        public ConcurrentDictionary<uint, SpatialClient> Clients;
        /// <summary>
        /// Stores the static entities value.
        /// </summary>
        public List<StaticEntity> StaticEntities;
        /// <summary>
        /// Gets or sets the max view distance value.
        /// </summary>
        public float MaxViewDistance { get; private set; }
        /// <summary>
        /// Gets or sets the extra distance a visible client can move before leaving visibility.
        /// </summary>
        public float VisibilityHysteresis { get; set; }
        /// <summary>
        /// Stores the static entities lock value.
        /// </summary>
        private readonly object staticEntitiesLock = new object();
        /// <summary>
        /// Stores the reusable clients snapshot for spatialization ticks.
        /// </summary>
        private readonly List<SpatialClient> spatializationClients = new List<SpatialClient>();
        /// <summary>
        /// Stores the reusable static-entity snapshot for spatialization ticks.
        /// </summary>
        private readonly List<StaticEntity> spatializationStaticEntities = new List<StaticEntity>();
        /// <summary>
        /// Stores the reusable client broad-phase grid for the current spatialization tick.
        /// </summary>
        private readonly Dictionary<long, List<SpatialClient>> clientGrid = new Dictionary<long, List<SpatialClient>>();
        /// <summary>
        /// Stores the reusable static-entity broad-phase grid for the current spatialization tick.
        /// </summary>
        private readonly Dictionary<long, List<StaticEntity>> staticEntityGrid = new Dictionary<long, List<StaticEntity>>();
        /// <summary>
        /// Recycles client cell lists between ticks.
        /// </summary>
        private readonly Stack<List<SpatialClient>> clientCellPool = new Stack<List<SpatialClient>>();
        /// <summary>
        /// Recycles static-entity cell lists between ticks.
        /// </summary>
        private readonly Stack<List<StaticEntity>> staticEntityCellPool = new Stack<List<StaticEntity>>();
        /// <summary>
        /// Stores one reusable candidate list for exact client-distance checks.
        /// </summary>
        private readonly List<SpatialClient> clientCandidates = new List<SpatialClient>();
        /// <summary>
        /// Stores one reusable candidate list for exact static-entity distance checks.
        /// </summary>
        private readonly List<StaticEntity> staticEntityCandidates = new List<StaticEntity>();

        /// <summary>
        /// Instantiate a new simple spatializer based on distance between clients
        /// </summary>
        /// <param name="world"> world to spatialize</param>
        /// <param name="spatializationFreq"> frequency of spatialization loop</param>
        /// <param name="synchFreq"> frequency of synch loop</param>
        /// <param name="maxViewDistance"> maximum view distance of the clients</param>
        public SimpleSpatializer(NetSquareWorld world, float spatializationFreq, float synchFreq, float maxViewDistance, float visibilityHysteresis = 0f) : base(world, spatializationFreq, synchFreq)
        {
            MaxViewDistance = maxViewDistance;
            VisibilityHysteresis = visibilityHysteresis < 0f ? 0f : visibilityHysteresis;
            Clients = new ConcurrentDictionary<uint, SpatialClient>();
            StaticEntities = new List<StaticEntity>();
            Start();
        }

        /// <summary>
        /// Add a client to this spatializer
        /// </summary>
        /// <param name="client">ID of the client to add</param>
        public override void AddClient(ConnectedClient client)
        {
            if (client == null)
                return;

            SpatialClient spatializedClient = new SpatialClient(this, client);
            Clients.TryAdd(client.ID, spatializedClient);
        }

        /// <summary>
        /// Remove a client from the spatializer
        /// </summary>
        /// <param name="clientID">ID of the client to remove</param>
        public override void RemoveClient(uint clientID)
        {
            SpatialClient client;
            Clients.TryRemove(clientID, out client);
            RemoveStoredFrames(clientID);
        }

        /// <summary>
        /// get all visible clients for a given client, according to a maximum view distance
        /// </summary>
        /// <param name="clientID">ID of the client to get visibles</param>
        /// <param name="maxDistance">maximum view distance of  the client</param>
        /// <returns></returns>
        public override HashSet<uint> GetVisibleClients(uint clientID)
        {
            SpatialClient client;
            if (!Clients.TryGetValue(clientID, out client))
                return new HashSet<uint>();

            lock (client.SyncRoot)
                return new HashSet<uint>(client.VisibleIDs);
        }

        /// <summary>
        /// Main spatialization loop
        /// Process visible clients and static entities
        /// </summary>
        protected override void SpatializationLoop()
        {
            // Build stable input snapshots once, then use a uniform grid to avoid all-pairs distance checks.
            spatializationClients.Clear();
            foreach (KeyValuePair<uint, SpatialClient> client in Clients)
                spatializationClients.Add(client.Value);

            spatializationStaticEntities.Clear();
            lock (staticEntitiesLock)
                spatializationStaticEntities.AddRange(StaticEntities);

            float cellSize = MaxViewDistance;
            if (cellSize <= 0f ||
                float.IsNaN(cellSize) ||
                float.IsInfinity(cellSize) ||
                float.IsNaN(VisibilityHysteresis) ||
                float.IsInfinity(VisibilityHysteresis))
            {
                ProcessSpatializationWithoutGrid();
                return;
            }

            double visibilityCellRadius = Math.Ceiling(
                (MaxViewDistance + Math.Max(0f, VisibilityHysteresis)) / cellSize);
            if (visibilityCellRadius > 64d)
            {
                ProcessSpatializationWithoutGrid();
                return;
            }
            int clientCellRadius = Math.Max(1, (int)visibilityCellRadius);

            RecycleGrid(clientGrid, clientCellPool);
            RecycleGrid(staticEntityGrid, staticEntityCellPool);
            for (int index = 0; index < spatializationClients.Count; index++)
            {
                SpatialClient client = spatializationClients[index];
                NetsquareTransformFrame transform;
                long cellKey;
                if (client.TryGetTransform(out transform) && TryGetCellKey(transform, cellSize, out cellKey))
                    GetCell(clientGrid, clientCellPool, cellKey).Add(client);
            }
            for (int index = 0; index < spatializationStaticEntities.Count; index++)
            {
                StaticEntity entity = spatializationStaticEntities[index];
                long cellKey;
                if (TryGetCellKey(entity.Transform, cellSize, out cellKey))
                    GetCell(staticEntityGrid, staticEntityCellPool, cellKey).Add(entity);
            }

            for (int index = 0; index < spatializationClients.Count; index++)
            {
                SpatialClient client = spatializationClients[index];
                NetsquareTransformFrame transform;
                int cellX;
                int cellY;
                if (!client.TryGetTransform(out transform) ||
                    !TryGetCellCoordinates(transform, cellSize, out cellX, out cellY))
                {
                    client.ProcessVisibleClients(spatializationClients);
                    client.ProcessVisibleStaticEntities(spatializationStaticEntities);
                    continue;
                }

                CollectNeighbourCandidates(clientGrid, cellX, cellY, clientCellRadius, clientCandidates);
                CollectNeighbourCandidates(staticEntityGrid, cellX, cellY, 1, staticEntityCandidates);
                client.ProcessVisibleClients(clientCandidates);
                client.ProcessVisibleStaticEntities(staticEntityCandidates);
            }
        }

        /// <summary>
        /// Preserves the exhaustive algorithm for invalid or disabled grid dimensions.
        /// </summary>
        private void ProcessSpatializationWithoutGrid()
        {
            for (int index = 0; index < spatializationClients.Count; index++)
            {
                SpatialClient client = spatializationClients[index];
                client.ProcessVisibleClients(spatializationClients);
                client.ProcessVisibleStaticEntities(spatializationStaticEntities);
            }
        }

        /// <summary>
        /// Recycles all cell lists and clears a broad-phase grid for its next tick.
        /// </summary>
        /// <typeparam name="T">Cell item type.</typeparam>
        /// <param name="grid">Grid to clear.</param>
        /// <param name="pool">Cell-list pool receiving cleared lists.</param>
        private static void RecycleGrid<T>(Dictionary<long, List<T>> grid, Stack<List<T>> pool)
        {
            foreach (List<T> cell in grid.Values)
            {
                cell.Clear();
                pool.Push(cell);
            }
            grid.Clear();
        }

        /// <summary>
        /// Gets or creates one reusable broad-phase cell list.
        /// </summary>
        /// <typeparam name="T">Cell item type.</typeparam>
        /// <param name="grid">Target grid.</param>
        /// <param name="pool">Reusable cell-list pool.</param>
        /// <param name="cellKey">Packed cell coordinate.</param>
        /// <returns>Mutable cell list for the current tick.</returns>
        private static List<T> GetCell<T>(
            Dictionary<long, List<T>> grid,
            Stack<List<T>> pool,
            long cellKey)
        {
            List<T> cell;
            if (grid.TryGetValue(cellKey, out cell))
                return cell;

            cell = pool.Count > 0 ? pool.Pop() : new List<T>();
            grid.Add(cellKey, cell);
            return cell;
        }

        /// <summary>
        /// Collects the nine cells that can intersect a circular visibility radius.
        /// </summary>
        /// <typeparam name="T">Candidate item type.</typeparam>
        /// <param name="grid">Broad-phase grid.</param>
        /// <param name="cellX">Source cell X coordinate.</param>
        /// <param name="cellY">Source cell Y coordinate.</param>
        /// <param name="cellRadius">Number of neighbouring cells required by the visibility radius.</param>
        /// <param name="candidates">Reusable destination list.</param>
        private static void CollectNeighbourCandidates<T>(
            Dictionary<long, List<T>> grid,
            int cellX,
            int cellY,
            int cellRadius,
            List<T> candidates)
        {
            candidates.Clear();
            for (int xOffset = -cellRadius; xOffset <= cellRadius; xOffset++)
            {
                long neighbourX = (long)cellX + xOffset;
                if (neighbourX < int.MinValue || neighbourX > int.MaxValue)
                    continue;

                for (int yOffset = -cellRadius; yOffset <= cellRadius; yOffset++)
                {
                    long neighbourY = (long)cellY + yOffset;
                    if (neighbourY < int.MinValue || neighbourY > int.MaxValue)
                        continue;

                    List<T> cell;
                    if (grid.TryGetValue(GetCellKey((int)neighbourX, (int)neighbourY), out cell))
                        candidates.AddRange(cell);
                }
            }
        }

        /// <summary>
        /// Resolves and packs the grid cell containing a transform.
        /// </summary>
        /// <param name="transform">World transform.</param>
        /// <param name="cellSize">Positive grid cell size.</param>
        /// <param name="cellKey">Packed grid coordinate.</param>
        /// <returns>True when the position can be represented by the grid.</returns>
        private static bool TryGetCellKey(
            NetsquareTransformFrame transform,
            float cellSize,
            out long cellKey)
        {
            int cellX;
            int cellY;
            if (!TryGetCellCoordinates(transform, cellSize, out cellX, out cellY))
            {
                cellKey = 0;
                return false;
            }

            cellKey = GetCellKey(cellX, cellY);
            return true;
        }

        /// <summary>
        /// Resolves integer grid coordinates for one finite transform.
        /// </summary>
        /// <param name="transform">World transform.</param>
        /// <param name="cellSize">Positive grid cell size.</param>
        /// <param name="cellX">Resolved X coordinate.</param>
        /// <param name="cellY">Resolved Z coordinate.</param>
        /// <returns>True when both coordinates fit in signed integers.</returns>
        private static bool TryGetCellCoordinates(
            NetsquareTransformFrame transform,
            float cellSize,
            out int cellX,
            out int cellY)
        {
            cellX = 0;
            cellY = 0;
            if (float.IsNaN(transform.x) ||
                float.IsInfinity(transform.x) ||
                float.IsNaN(transform.z) ||
                float.IsInfinity(transform.z))
                return false;

            double x = Math.Floor(transform.x / cellSize);
            double y = Math.Floor(transform.z / cellSize);
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                return false;

            cellX = (int)x;
            cellY = (int)y;
            return true;
        }

        /// <summary>
        /// Packs two signed grid coordinates into one collision-free dictionary key.
        /// </summary>
        /// <param name="cellX">Grid X coordinate.</param>
        /// <param name="cellY">Grid Y coordinate.</param>
        /// <returns>Packed coordinate key.</returns>
        private static long GetCellKey(int cellX, int cellY)
        {
            return ((long)cellX << 32) | (uint)cellY;
        }
        /// <summary>
        /// Synchronization loop that pack and send visible clients to the clients
        /// </summary>
        protected override unsafe void SynchLoop()
        {
            Dictionary<uint, List<INetSquareSynchFrame>> frameSnapshot = DrainStoredFrames();
            try
            {
                if (frameSnapshot.Count == 0)
                    return;

                foreach (KeyValuePair<uint, SpatialClient> client in Clients)
                {
                    // Visibility arrays change only when membership changes and are safe to enumerate lock-free.
                    uint[] visibleClientIDs = client.Value.GetVisibleIDsSnapshot();
                    if (visibleClientIDs.Length == 0)
                        continue;

                    NetworkMessage synchMessage = new NetworkMessage(NetSquareMessageID.SetSynchFramesPacked);
                    foreach (uint visibleClientID in visibleClientIDs)
                    {
                        List<INetSquareSynchFrame> frames;
                        if (frameSnapshot.TryGetValue(visibleClientID, out frames) && frames.Count > 0)
                            NetSquareSynchFramesUtils.SerializePackedFrames(synchMessage, visibleClientID, frames);
                    }

                    if (synchMessage.HasWriteData)
                        World.server.SendToClient(synchMessage, client.Key);
                }
            }
            finally
            {
                ReturnDrainedFrames(frameSnapshot);
            }
        }
        /// <summary>
        /// Executes the for each operation.
        /// </summary>
        public override void ForEach(Action<uint, IEnumerable<uint>> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            foreach (KeyValuePair<uint, SpatialClient> client in Clients)
                callback(client.Key, new HashSet<uint>(client.Value.GetVisibleIDsSnapshot()));
        }

        /// <summary>
        /// Executes an internal callback with immutable visibility snapshots and no per-client copy.
        /// </summary>
        /// <param name="callback">Internal synchronization callback.</param>
        internal override void ForEachVisibleSnapshot(Action<uint, uint[]> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            foreach (KeyValuePair<uint, SpatialClient> client in Clients)
                callback(client.Key, client.Value.GetVisibleIDsSnapshot());
        }

        /// <summary>
        /// Creates a debug snapshot of this simple spatializer.
        /// </summary>
        /// <returns>Spatializer debug snapshot.</returns>
        public override NetSquareSpatializerSnapshot CreateSnapshot()
        {
            NetSquareSpatializerSnapshot snapshot = base.CreateSnapshot();
            snapshot.MaxViewDistance = MaxViewDistance;
            snapshot.VisibilityHysteresis = VisibilityHysteresis;
            return snapshot;
        }

        /// <summary>
        /// Executes the add static entity operation.
        /// </summary>
        public override void AddStaticEntity(short type, uint id, NetsquareTransformFrame pos)
        {
            lock (staticEntitiesLock)
                StaticEntities.Add(new StaticEntity(type, id, pos));
            StaticEntitiesCount++;
        }

        /// <summary>
        /// Executes the get clients snapshot operation.
        /// </summary>
        internal List<SpatialClient> GetClientsSnapshot()
        {
            List<SpatialClient> snapshot = new List<SpatialClient>(Clients.Count);
            foreach (var pair in Clients)
                snapshot.Add(pair.Value);
            return snapshot;
        }

        /// <summary>
        /// Executes the get static entities snapshot operation.
        /// </summary>
        internal List<StaticEntity> GetStaticEntitiesSnapshot()
        {
            lock (staticEntitiesLock)
                return new List<StaticEntity>(StaticEntities);
        }
    }
}
#endregion
