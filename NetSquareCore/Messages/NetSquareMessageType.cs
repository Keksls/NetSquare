namespace NetSquare.Core.Messages
{
    public enum NetSquareMessageType : ushort
    {
        ClientJoinWorld = 65535,
        ClientLeaveWorld = 65534,
        ClientSetTransform = 65533,
        ClientsJoinWorld = 65532,
        ClientsLeaveWorld = 65531,
        ClientSynchronizeTime = 65530
    }
}