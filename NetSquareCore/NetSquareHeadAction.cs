#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Represents the net square head action value.
    /// </summary>
    public struct NetSquareHeadAction
    {
        /// <summary>
        /// Gets or sets the head id value.
        /// </summary>
        public ushort HeadID { get; private set; }
        /// <summary>
        /// Gets or sets the head name value.
        /// </summary>
        public string HeadName { get; private set; }
        /// <summary>
        /// Gets or sets the head action value.
        /// </summary>
        public NetSquareAction HeadAction { get; private set; }

        /// <summary>
        /// Initializes a new instance of the net square head action class.
        /// </summary>
        public NetSquareHeadAction(ushort ID, string name, NetSquareAction action)
        {
            HeadID = ID;
            HeadName = name;
            HeadAction = action;
        }
    }

    /// <summary>
    /// Defines the net square action callback.
    /// </summary>
    public delegate void NetSquareAction(NetworkMessage message);
}
#endregion
