using NetSquare.Core;
using NetSquare.Core.Messages;
using NetSquareCore;
using System.Collections.Generic;

namespace NetSquareServer.Worlds
{
    public class SpatialClient
    {
        public ConnectedClient Client;
        public Position Position;
        public bool[] visibleClients;
        public HashSet<SpatialClient> Visibles;
        public HashSet<uint> VisibleIDs;
        public Spatializer Spatializer;

        public SpatialClient(Spatializer spatializer, ConnectedClient client, Position position)
        {
            Spatializer = spatializer;
            Client = client;
            Position = position;
            Visibles = new HashSet<SpatialClient>();
            VisibleIDs = new HashSet<uint>();
        }

        public void ProcessVisible()
        {
            // leaving clients
            NetworkMessage leavingMessage = new NetworkMessage(NetSquareMessageType.ClientsLeaveWorld);
            bool clientLeaveFOV = false;
            // pack message
            foreach (SpatialClient oldVisible in Visibles)
                if (Position.Distance(oldVisible.Position, Position) > Spatializer.MaxViewDistance)
                {
                    // client just leave FOV
                    clientLeaveFOV = true;
                    leavingMessage.Set(oldVisible.Client.ID);
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
                if (Position.Distance(client.Position, Position) <= Spatializer.MaxViewDistance)
                {
                    // new client in FOV
                    newVisibles.Add(client);
                    VisibleIDs.Add(client.Client.ID.UInt32);
                    if (!Visibles.Contains(client))
                    {
                        //create new join message
                        NetworkMessage joiningClientMessage = new NetworkMessage(0, client.Client.ID);
                        // send message so server event for being custom binded
                        Spatializer.World.server.Worlds.Fire_OnSendWorldClients(Spatializer.World.ID,
                            client.Client.ID.UInt32,
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

            Visibles = newVisibles;
        }
    }
}