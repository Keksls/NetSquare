#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Defines the available net square message type values.
    /// </summary>
    public enum NetSquareMessageType : byte
    {
        Default = 0,
        Reply = 1,
        BroadcastCurrentWorld = 2,
        BroadcastCurrentWorldUnreliable = 3,
        SynchronizeMessageCurrentWorld = 4,
        MAX = 4
    }
}
#endregion
