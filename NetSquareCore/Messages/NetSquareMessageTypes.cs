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
        SynchronizeMessageCurrentWorld = 3,
        MAX = 3
    }
}
#endregion
