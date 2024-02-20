using NetSquareCore;

namespace NetSquareServer.Worlds
{
    public class StaticEntity
    {
        public short Type { get; set; }
        public uint Id { get; set; }
        public Transform Position { get; set; }

        public StaticEntity(short type, uint id, Transform position)
        {
            Type = type;
            Id = id;
            Position = position;
        }
    }
}