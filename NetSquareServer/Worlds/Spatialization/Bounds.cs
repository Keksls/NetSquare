using NetSquare.Core;

#region Source
namespace NetSquare.Server.Worlds
{
    /// <summary>
    /// Represents the spatial bounds value.
    /// </summary>
    public struct SpatialBounds
    {
        /// <summary>
        /// Gets or sets the min x value.
        /// </summary>
        public float MinX { get; private set; }
        /// <summary>
        /// Gets or sets the min y value.
        /// </summary>
        public float MinY { get; private set; }
        /// <summary>
        /// Gets or sets the max x value.
        /// </summary>
        public float MaxX { get; private set; }
        /// <summary>
        /// Gets or sets the max y value.
        /// </summary>
        public float MaxY { get; private set; }

        /// <summary>
        /// Executes the spatial bounds operation.
        /// </summary>
        public SpatialBounds(float minx, float miny, float maxx, float maxy)
        {
            MinX = minx;
            MinY = miny;
            MaxX = maxx;
            MaxY = maxy;
        }

        /// <summary>
        /// Executes the is in bounds operation.
        /// </summary>
        public bool IsInBounds(float x, float y)
        {
            return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
        }

        /// <summary>
        /// Executes the is in bounds operation.
        /// </summary>
        public bool IsInBounds(NetsquareTransformFrame position)
        {
            return position.x >= MinX && position.x <= MaxX && position.z >= MinY && position.z <= MaxY;
        }
    }
}
#endregion
