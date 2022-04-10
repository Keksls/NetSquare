﻿using NetSquare.Core;
using System;

namespace NetSquareClient
{
    public class LobbiesManager
    {
        public bool IsInLobby { get; private set; }
        public ushort CurrentLobbyID { get; private set; }
        public event Action<uint> OnClientJoinLobby;
        public event Action<uint> OnClientLeaveLobby;
        private NetSquare_Client parentClient;

        public LobbiesManager(NetSquare_Client client)
        {
            parentClient = client;
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
        /// Send a networkMessage to any client in the same lobby I am. Must be in a lobby
        /// </summary>
        /// <param name="message">message to send</param>
        public void Broadcast(NetworkMessage message)
        {
            if (!IsInLobby)
                return;

            // save head into message for construct head after server receive it
            message.Set(message.Head);
            // set head as broadcast message in lobby ID
            message.Head = 65534;
            parentClient.SendMessage(message);
        }

        private void ClientJoinCurrentLobby(NetworkMessage message)
        {

        }
    }
}