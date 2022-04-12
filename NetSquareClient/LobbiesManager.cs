using NetSquare.Core;
using System;
using System.Collections.Generic;

namespace NetSquareClient
{
    public class LobbiesManager
    {
        public bool IsInLobby { get; private set; }
        public ushort CurrentLobbyID { get; private set; }
        public event Action<uint> OnClientJoinLobby;
        public event Action<uint> OnClientLeaveLobby;
        public HashSet<uint> ClientsInLobby { get; private set; }
        private NetSquare_Client parentClient;

        public LobbiesManager(NetSquare_Client client)
        {
            ClientsInLobby = new HashSet<uint>();
            parentClient = client;
            parentClient.Dispatcher.AddHeadAction(65532, "ClientJoinCurrentLobby", ClientJoinCurrentLobby);
            parentClient.Dispatcher.AddHeadAction(65531, "ClientLeaveCurrentLobby", ClientLeaveCurrentLobby);
        }

        /// <summary>
        /// Try to Join a lobby. Can fail if lobbyID don't exists or client already in lobby.
        /// if success, OnJoinLobby will be invoked, else OnFailJoinLobby will be invoked
        /// </summary>
        /// <param name="lobbyID">ID of the lobby to join</param>
        /// <param name="Callback">Callback raised after server try added. if true, join success</param>
        public void TryJoinLobby(ushort lobbyID, Action<bool> Callback)
        {
            if (IsInLobby)
            {
                Callback?.Invoke(false);
                return;
            }

            parentClient.SendMessage(new NetworkMessage(65535).Set(lobbyID), (response) =>
            {
                if (response.GetBool())
                {
                    IsInLobby = true;
                    CurrentLobbyID = lobbyID;
                    Callback?.Invoke(true);
                }
                else
                    Callback?.Invoke(false);
            });
        }
        
        /// <summary>
        /// Try to leave the current lobby. Can fail if not in lobby.
        /// if success, OnJoinLobby will be invoked, else OnFailJoinLobby will be invoked
        /// </summary>
        /// <param name="Callback">Callback raised after server try leave. if true, leave success. can be null</param>
        public void TryleaveLobby(Action<bool> Callback)
        {
            if (!IsInLobby)
            {
                Callback?.Invoke(false);
                return;
            }

            parentClient.SendMessage(new NetworkMessage(65534), (response) =>
            {
                if (response.GetBool())
                {
                    IsInLobby = false;
                    CurrentLobbyID = 0;
                    Callback?.Invoke(true);
                }
                else
                    Callback?.Invoke(false);
            });
        }

        /// <summary>
        /// Send a networkMessage to any client in the same lobby I am. Must be in a lobby
        /// </summary>
        /// <param name="message">message to send</param>
        public void Broadcast(NetworkMessage message)
        {
            if (!IsInLobby)
                return;
            // set TypeID as 1, because 1 is the broadcast ID
            message.SetType(1);
            parentClient.SendMessage(message);
        }

        private void ClientJoinCurrentLobby(NetworkMessage message)
        {
            uint clientID = message.GetUInt();
            ClientsInLobby.Add(clientID);
            OnClientJoinLobby?.Invoke(clientID);
        }

        private void ClientLeaveCurrentLobby(NetworkMessage message)
        {
            uint clientID = message.GetUInt();
            ClientsInLobby.Remove(clientID);
            OnClientLeaveLobby?.Invoke(clientID);
        }
    }
}