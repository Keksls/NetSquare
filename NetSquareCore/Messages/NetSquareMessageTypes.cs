namespace NetSquare.Core
{
    public enum MessageType : uint
    {
        Default = 0,
        BroadcastCurrentWorld = 1,
        SynchronizeMessageCurrentWorld = 2,
        SetClientPosition = 3
    }
}