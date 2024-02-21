using NetSquareCore;

namespace NetSquareServer.Worlds
{
    public class StaticEntity
    {
        public short Type { get; set; }
        public uint Id { get; set; }
        public NetsquareTransformFrame Transform { get; set; }

        public StaticEntity(short type, uint id, NetsquareTransformFrame transform)
        {
            Type = type;
            Id = id;
            Transform = transform;
        }
    }
}