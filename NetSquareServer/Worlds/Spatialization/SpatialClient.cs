using NetSquare.Core;
using NetSquare.Core.Messages;
using System.Collections.Generic;

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
        /// Initializes a new instance of the spatial client class.
        /// </summary>
        public SpatialClient(SimpleSpatializer spatializer, ConnectedClient client)
        {
            Spatializer = spatializer;
            Client = client;
            Visibles = new HashSet<SpatialClient>();
            VisibleIDs = new HashSet<uint>();
            VisibleStaticEntities = new HashSet<StaticEntity>();
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
            NetsquareTransformFrame currentTransform;
            if (!TryGetTransform(out currentTransform))
                return;

            lock (SyncRoot)
            {
                // leaving clients
                NetworkMessage leavingMessage = new NetworkMessage(NetSquareMessageID.ClientsLeaveWorld);
                bool clientLeaveFOV = false;
                // pack message
                foreach (SpatialClient oldVisible in Visibles)
                {
                    NetsquareTransformFrame oldVisibleTransform;
                    if (!oldVisible.TryGetTransform(out oldVisibleTransform) ||
                        NetsquareTransformFrame.Distance(oldVisibleTransform, currentTransform) > Spatializer.MaxViewDistance + Spatializer.VisibilityHysteresis)
                    {
                        // client just leave FOV
                        clientLeaveFOV = true;
                        leavingMessage.Set(new UInt24(oldVisible.Client.ID));
                    }
                }
                // send packed message to client
                if (clientLeaveFOV)
                    Client.AddTCPMessage(leavingMessage);

                // joining clients
                NetworkMessage JoiningPacked = new NetworkMessage(NetSquareMessageID.ClientsJoinWorld);
                List<NetworkMessage> JoiningClientMessages = new List<NetworkMessage>();
                HashSet<SpatialClient> newVisibles = new HashSet<SpatialClient>();
                HashSet<uint> newVisibleIDs = new HashSet<uint>();
                // iterate on each clients in my spatializer
                foreach (SpatialClient client in Spatializer.GetClientsSnapshot())
                {
                    NetsquareTransformFrame clientTransform;
                    if (client.TryGetTransform(out clientTransform))
                    {
                        float distance = NetsquareTransformFrame.Distance(clientTransform, currentTransform);
                        bool wasVisible = Visibles.Contains(client);
                        bool isVisible = distance <= Spatializer.MaxViewDistance ||
                            (wasVisible && distance <= Spatializer.MaxViewDistance + Spatializer.VisibilityHysteresis);
                        if (!isVisible)
                            continue;

                        // new client in FOV
                        newVisibles.Add(client);
                        newVisibleIDs.Add(client.Client.ID);
                        if (!Visibles.Contains(client))
                        {
                            //create new join message
                            NetworkMessage joiningClientMessage = new NetworkMessage(0, client.Client.ID);
                            // set Transform frame
                            clientTransform.Serialize(joiningClientMessage);
                            // send message so server event for being custom binded
                            Spatializer.World.server.Worlds.Fire_OnSendWorldClients(Spatializer.World.ID,
                                client.Client.ID,
                                joiningClientMessage);
                            // add message to list for packing
                            JoiningClientMessages.Add(joiningClientMessage);
                        }
                    }
                }

                // send packed message
                if (JoiningClientMessages.Count > 0)
                {
                    JoiningPacked.Pack(JoiningClientMessages);
                    Client.AddTCPMessage(JoiningPacked);
                }

                // client has move since last spatialization
                if (!currentTransform.Equals(LastPosition))
                {
                    LastPosition.Set(currentTransform);
                }

                Visibles = newVisibles;
                VisibleIDs = newVisibleIDs;
            }
        }

        /// <summary>
        /// Executes the process visible static entities operation.
        /// </summary>
        public void ProcessVisibleStaticEntities()
        {
            NetsquareTransformFrame currentTransform;
            if (!TryGetTransform(out currentTransform))
                return;

            // leaving entities
            List<StaticEntity> leaving = new List<StaticEntity>();
            List<StaticEntity> newVisibles = new List<StaticEntity>();
            HashSet<StaticEntity> nextVisibleStaticEntities = new HashSet<StaticEntity>();

            lock (SyncRoot)
            {
                // pack message
                foreach (StaticEntity oldVisible in VisibleStaticEntities)
                {
                    if (NetsquareTransformFrame.Distance(oldVisible.Transform, currentTransform) > Spatializer.MaxViewDistance)
                    {
                        // client just leave FOV
                        leaving.Add(oldVisible);
                    }
                    else
                    {
                        nextVisibleStaticEntities.Add(oldVisible);
                    }
                }

                // iterate on each entities in my spatializer
                foreach (StaticEntity entity in Spatializer.GetStaticEntitiesSnapshot())
                {
                    if (NetsquareTransformFrame.Distance(entity.Transform, currentTransform) <= Spatializer.MaxViewDistance &&
                        !nextVisibleStaticEntities.Contains(entity))
                    {
                        nextVisibleStaticEntities.Add(entity);
                        if (!VisibleStaticEntities.Contains(entity))
                            newVisibles.Add(entity);
                    }
                }

                VisibleStaticEntities = nextVisibleStaticEntities;
            }
            // fire event
            if (leaving.Count > 0)
                Spatializer.World.Fire_OnHideStaticEntities(Client.ID, leaving);

            // fire event
            if (newVisibles.Count > 0)
                Spatializer.World.Fire_OnShowStaticEntities(Client.ID, newVisibles);
        }
    }
}
#endregion
