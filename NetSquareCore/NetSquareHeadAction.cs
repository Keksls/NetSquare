namespace NetSquare.Core
{
    public struct NetSquareHeadAction
    {
        public ushort HeadID { get; private set; }
        public string HeadName { get; private set; }
        public NetSquareAction HeadAction { get; private set; }

        public NetSquareHeadAction(ushort ID, string name, NetSquareAction action)
        {
            HeadID = ID;
            HeadName = name;
            HeadAction = action;
        }
    }

    public delegate void NetSquareAction(NetworkMessage message);
}
