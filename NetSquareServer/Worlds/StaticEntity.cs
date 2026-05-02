using NetSquare.Core;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the static entity component.
    /// </summary>
    public class StaticEntity
    {
        /// <summary>
        /// Gets or sets the type value.
        /// </summary>
        public short Type { get; set; }
        /// <summary>
        /// Gets or sets the id value.
        /// </summary>
        public uint Id { get; set; }
        /// <summary>
        /// Gets or sets the transform value.
        /// </summary>
        public NetsquareTransformFrame Transform { get; set; }

        /// <summary>
        /// Initializes a new instance of the static entity class.
        /// </summary>
        public StaticEntity(short type, uint id, NetsquareTransformFrame transform)
        {
            Type = type;
            Id = id;
            Transform = transform;
        }
    }
}
#endregion
