using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public class SpatialClient
    {
        public ConnectedClient Client;
        public NetsquareTransformFrame LastPosition;
        public NetsquareTransformFrame Transform => Spatializer.World.Clients[Client.ID];
        public HashSet<SpatialClient> Visibles;
        public HashSet<uint> VisibleIDs;
        public HashSet<StaticEntity> VisibleStaticEntities;
        public SimpleSpatializer Spatializer;

        public SpatialClient(SimpleSpatializer spatializer, ConnectedClient client)
        {
            Spatializer = spatializer;
            Client = client;
            Visibles = new HashSet<SpatialClient>();
            VisibleIDs = new HashSet<uint>();
            VisibleStaticEntities = new HashSet<StaticEntity>();
        }

        public void ProcessVisibleClients()
        {
            lock (Visibles)
            {
                // leaving clients
                NetworkMessage leavingMessage = new NetworkMessage(NetSquareMessageType.ClientsLeaveWorld);
                bool clientLeaveFOV = false;
                // pack message
                foreach (SpatialClient oldVisible in Visibles)
                    if (NetsquareTransformFrame.Distance(oldVisible.Transform, Transform) > Spatializer.MaxViewDistance)
                    {
                        // client just leave FOV
                        clientLeaveFOV = true;
                        leavingMessage.Set(new UInt24(oldVisible.Client.ID));
                    }
                // send packed message to client
                if (clientLeaveFOV)
                    Client.AddTCPMessage(leavingMessage);

                // joining clients
                NetworkMessage JoiningPacked = new NetworkMessage(NetSquareMessageType.ClientsJoinWorld);
                List<NetworkMessage> JoiningClientMessages = new List<NetworkMessage>();
                HashSet<SpatialClient> newVisibles = new HashSet<SpatialClient>();
                VisibleIDs.Clear();
                // iterate on each clients in my spatializer
                foreach (SpatialClient client in Spatializer.Clients.Values)
                {
                    if (NetsquareTransformFrame.Distance(client.Transform, Transform) <= Spatializer.MaxViewDistance)
                    {
                        // new client in FOV
                        newVisibles.Add(client);
                        VisibleIDs.Add(client.Client.ID);
                        if (!Visibles.Contains(client))
                        {
                            //create new join message
                            NetworkMessage joiningClientMessage = new NetworkMessage(0, client.Client.ID);
                            // set Transform frame
                            client.Transform.Serialize(joiningClientMessage);
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
                if (!Transform.Equals(LastPosition))
                {
                    LastPosition.Set(Transform);
                }

                Visibles = newVisibles;
            }
        }

        public void ProcessVisibleStaticEntities()
        {
            // leaving entities
            List<StaticEntity> leaving = new List<StaticEntity>();
            // pack message
            foreach (StaticEntity oldVisible in VisibleStaticEntities)
                if (NetsquareTransformFrame.Distance(oldVisible.Transform, Transform) > Spatializer.MaxViewDistance)
                {
                    // client just leave FOV
                    leaving.Add(oldVisible);
                }
            // fire event
            if (leaving.Count > 0)
                Spatializer.World.Fire_OnHideStaticEntities(Client.ID, leaving);

            // joining entities
            List<StaticEntity> newVisibles = new List<StaticEntity>();
            // iterate on each entities in my spatializer
            foreach (StaticEntity entity in Spatializer.StaticEntities)
            {
                if (NetsquareTransformFrame.Distance(entity.Transform, Transform) <= Spatializer.MaxViewDistance && !VisibleStaticEntities.Contains(entity))
                {
                    // new client in FOV
                    VisibleStaticEntities.Add(entity);
                    newVisibles.Add(entity);
                }
            }

            // fire event
            if (newVisibles.Count > 0)
                Spatializer.World.Fire_OnShowStaticEntities(Client.ID, newVisibles);
        }
    }
}