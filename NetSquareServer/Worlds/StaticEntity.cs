using NetSquareCore;

namespace NetSquareServer.Worlds
{
    public class StaticEntity
    {
        public short Type { get; set; }
        public uint Id { get; set; }
        public Position Position { get; set; }

        public StaticEntity(short type, uint id, Position position)
        {
            Type = type;
            Id = id;
            Position = position;
        }
    }
}