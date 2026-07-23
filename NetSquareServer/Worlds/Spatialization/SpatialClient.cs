using NetSquare.Core;
using NetSquare.Core.Messages;
using System;
using System.Collections.Generic;
using System.Threading;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the spatial client component.
    /// </summary>
    public class SpatialClient
    {
        /// <summary>
        /// Stores the client value.
        /// </summary>
        public ConnectedClient Client;
        /// <summary>
        /// Stores the last position value.
        /// </summary>
        public NetsquareTransformFrame LastPosition;
        /// <summary>
        /// Stores the transform value.
        /// </summary>
        public NetsquareTransformFrame Transform
        {
            get
            {
                NetsquareTransformFrame transform;
                return TryGetTransform(out transform) ? transform : LastPosition;
            }
        }
        /// <summary>
        /// Stores the visibles value.
        /// </summary>
        public HashSet<SpatialClient> Visibles;
        /// <summary>
        /// Stores the visible i ds value.
        /// </summary>
        public HashSet<uint> VisibleIDs;
        /// <summary>
        /// Stores the visible static entities value.
        /// </summary>
        public HashSet<StaticEntity> VisibleStaticEntities;
        /// <summary>
        /// Stores the spatializer value.
        /// </summary>
        public SimpleSpatializer Spatializer;
        /// <summary>
        /// Stores the sync root value.
        /// </summary>
        internal readonly object SyncRoot = new object();
        /// <summary>
        /// Stores the reusable next visible-client set.
        /// </summary>
        private HashSet<SpatialClient> nextVisibles;
        /// <summary>
        /// Stores the reusable next visible-client ID set.
        /// </summary>
        private HashSet<uint> nextVisibleIDs;
        /// <summary>
        /// Stores the immutable visible-client snapshot consumed by synchronization workers.
        /// </summary>
        private uint[] visibleIDsSnapshot;
        /// <summary>
        /// Stores the reusable next visible-static-entity set.
        /// </summary>
        private HashSet<StaticEntity> nextVisibleStaticEntities;

        /// <summary>
        /// Initializes a new instance of the spatial client class.
        /// </summary>
        public SpatialClient(SimpleSpatializer spatializer, ConnectedClient client)
        {
            Spatializer = spatializer;
            Client = client;
            Visibles = new HashSet<SpatialClient>();
            VisibleIDs = new HashSet<uint>();
            VisibleStaticEntities = new HashSet<StaticEntity>();
            nextVisibles = new HashSet<SpatialClient>();
            nextVisibleIDs = new HashSet<uint>();
            visibleIDsSnapshot = Array.Empty<uint>();
            nextVisibleStaticEntities = new HashSet<StaticEntity>();
            NetsquareTransformFrame transform;
            if (TryGetTransform(out transform))
                LastPosition = new NetsquareTransformFrame(transform);
        }

        /// <summary>
        /// Executes the try get transform operation.
        /// </summary>
        public bool TryGetTransform(out NetsquareTransformFrame transform)
        {
            return Spatializer.World.Clients.TryGetValue(Client.ID, out transform);
        }

        /// <summary>
        /// Executes the process visible clients operation.
        /// </summary>
        public void ProcessVisibleClients()
        {
            ProcessVisibleClients(Spatializer.GetClientsSnapshot());
        }

        /// <summary>
        /// Updates visible clients from a snapshot shared by the complete spatialization tick.
        /// </summary>
        /// <param name="clientsSnapshot">Stable clients snapshot for the current tick.</param>
        internal void ProcessVisibleClients(IReadOnlyList<SpatialClient> clientsSnapshot)
        {
            NetsquareTransformFrame currentTransform;
            if (!TryGetTransform(out currentTransform))
                return;

            NetworkMessage leavingMessage = null;
            List<NetworkMessage> joiningClientMessages = null;
            bool visibilityChanged = false;
            lock (SyncRoot)
            {
                HashSet<SpatialClient> previousVisibles = Visibles;
                nextVisibles.Clear();
                nextVisibleIDs.Clear();

                float enterDistanceSquared = Spatializer.MaxViewDistance * Spatializer.MaxViewDistance;
                float exitDistance = Spatializer.MaxViewDistance + Spatializer.VisibilityHysteresis;
                float exitDistanceSquared = exitDistance * exitDistance;

                foreach (SpatialClient previousVisible in previousVisibles)
                {
                    NetsquareTransformFrame previousTransform;
                    if (!previousVisible.TryGetTransform(out previousTransform) ||
                        NetsquareTransformFrame.DistanceSquared(previousTransform, currentTransform) > exitDistanceSquared)
                    {
                        if (leavingMessage == null)
                            leavingMessage = new NetworkMessage(NetSquareMessageID.ClientsLeaveWorld);
                        leavingMessage.Set(previousVisible.Client.ID);
                        visibilityChanged = true;
                    }
                }

                for (int index = 0; index < clientsSnapshot.Count; index++)
                {
                    SpatialClient candidate = clientsSnapshot[index];
                    NetsquareTransformFrame candidateTransform;
                    if (!candidate.TryGetTransform(out candidateTransform))
                        continue;

                    bool wasVisible = previousVisibles.Contains(candidate);
                    float maximumDistanceSquared = wasVisible ? exitDistanceSquared : enterDistanceSquared;
                    if (NetsquareTransformFrame.DistanceSquared(candidateTransform, currentTransform) > maximumDistanceSquared)
                        continue;

                    nextVisibles.Add(candidate);
                    nextVisibleIDs.Add(candidate.Client.ID);
                    if (wasVisible)
                        continue;

                    visibilityChanged = true;
                    NetworkMessage joiningClientMessage = new NetworkMessage(0, candidate.Client.ID);
                    candidateTransform.Serialize(joiningClientMessage);
                    if (joiningClientMessages == null)
                        joiningClientMessages = new List<NetworkMessage>();
                    joiningClientMessages.Add(joiningClientMessage);
                }

                if (!currentTransform.Equals(LastPosition))
                    LastPosition.Set(currentTransform);

                Visibles = nextVisibles;
                nextVisibles = previousVisibles;
                HashSet<uint> previousVisibleIDs = VisibleIDs;
                VisibleIDs = nextVisibleIDs;
                nextVisibleIDs = previousVisibleIDs;
                if (visibilityChanged)
                    Volatile.Write(ref visibleIDsSnapshot, CopyVisibleIDs(VisibleIDs));
            }

            if (leavingMessage != null)
                Client.AddTCPMessage(leavingMessage);

            if (joiningClientMessages == null)
                return;

            foreach (NetworkMessage joiningClientMessage in joiningClientMessages)
            {
                Spatializer.World.server.Worlds.Fire_OnSendWorldClients(
                    Spatializer.World.ID,
                    joiningClientMessage.ClientID,
                    joiningClientMessage);
            }

            NetworkMessage joiningPacked = new NetworkMessage(NetSquareMessageID.ClientsJoinWorld);
            joiningPacked.Pack(joiningClientMessages);
            Client.AddTCPMessage(joiningPacked);
        }
        /// <summary>
        /// Returns the immutable visible-client snapshot without allocating or holding the spatialization lock.
        /// </summary>
        /// <returns>Visible client identifiers for the latest completed spatialization pass.</returns>
        internal uint[] GetVisibleIDsSnapshot()
        {
            return Volatile.Read(ref visibleIDsSnapshot);
        }

        /// <summary>
        /// Copies a visible-client set into an immutable synchronization snapshot.
        /// </summary>
        /// <param name="visibleIDs">Visible client identifiers to copy.</param>
        /// <returns>Immutable visible-client identifier array.</returns>
        private static uint[] CopyVisibleIDs(HashSet<uint> visibleIDs)
        {
            if (visibleIDs == null || visibleIDs.Count == 0)
                return Array.Empty<uint>();

            uint[] snapshot = new uint[visibleIDs.Count];
            visibleIDs.CopyTo(snapshot);
            return snapshot;
        }
        /// <summary>
        /// Executes the process visible static entities operation.
        /// </summary>
        public void ProcessVisibleStaticEntities()
        {
            ProcessVisibleStaticEntities(Spatializer.GetStaticEntitiesSnapshot());
        }

        /// <summary>
        /// Updates visible static entities from a snapshot shared by the complete spatialization tick.
        /// </summary>
        /// <param name="staticEntitiesSnapshot">Stable static-entity snapshot for the current tick.</param>
        internal void ProcessVisibleStaticEntities(IReadOnlyList<StaticEntity> staticEntitiesSnapshot)
        {
            NetsquareTransformFrame currentTransform;
            if (!TryGetTransform(out currentTransform))
                return;

            List<StaticEntity> leaving = null;
            List<StaticEntity> joining = null;
            lock (SyncRoot)
            {
                HashSet<StaticEntity> previousVisibleStaticEntities = VisibleStaticEntities;
                nextVisibleStaticEntities.Clear();
                float maximumDistanceSquared = Spatializer.MaxViewDistance * Spatializer.MaxViewDistance;

                foreach (StaticEntity previousVisible in previousVisibleStaticEntities)
                {
                    if (NetsquareTransformFrame.DistanceSquared(previousVisible.Transform, currentTransform) > maximumDistanceSquared)
                    {
                        if (leaving == null)
                            leaving = new List<StaticEntity>();
                        leaving.Add(previousVisible);
                    }
                    else
                    {
                        nextVisibleStaticEntities.Add(previousVisible);
                    }
                }

                for (int index = 0; index < staticEntitiesSnapshot.Count; index++)
                {
                    StaticEntity entity = staticEntitiesSnapshot[index];
                    if (NetsquareTransformFrame.DistanceSquared(entity.Transform, currentTransform) > maximumDistanceSquared ||
                        !nextVisibleStaticEntities.Add(entity))
                        continue;

                    if (!previousVisibleStaticEntities.Contains(entity))
                    {
                        if (joining == null)
                            joining = new List<StaticEntity>();
                        joining.Add(entity);
                    }
                }

                VisibleStaticEntities = nextVisibleStaticEntities;
                nextVisibleStaticEntities = previousVisibleStaticEntities;
            }

            if (leaving != null)
                Spatializer.World.Fire_OnHideStaticEntities(Client.ID, leaving);
            if (joining != null)
                Spatializer.World.Fire_OnShowStaticEntities(Client.ID, joining);
        }
    }
}
#endregion
