namespace NetSquare.Core.Messages
{
    public enum NetSquareMessageType : ushort
    {
        ClientJoinWorld = 65535,
        ClientLeaveWorld = 65534,
        ClientsJoinWorld = 65533,
        ClientsLeaveWorld = 65532,
        ClientSynchronizeTime = 65531,

        /// <summary>
        ///    Client sends a transform
        /// </summary>
        SetSynchFrame = 65530,
        /// <summary>
        ///    Clienty sends some transform frames
        /// </summary>
        SetSynchFrames = 65529,
        /// <summary>
        ///    Server sends some transforms frames, for x clients, packed into a single message
        /// </summary>
        SetSynchFramesPacked = 65528
    }
}