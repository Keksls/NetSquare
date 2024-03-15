namespace NetSquare.Core
{
    public enum NetSquareMessageType : byte
    {
        Default = 0,
        Reply = 1,
        BroadcastCurrentWorld = 2,
        SynchronizeMessageCurrentWorld = 3,
        MAX = 3
    }
}