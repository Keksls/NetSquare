﻿using NetSquare.Core;

namespace NetSquare.Server.Worlds
{
    public struct SpatialBounds
    {
        public float MinX { get; private set; }
        public float MinY { get; private set; }
        public float MaxX { get; private set; }
        public float MaxY { get; private set; }

        public SpatialBounds(float minx, float miny, float maxx, float maxy)
        {
            MinX = minx;
            MinY = miny;
            MaxX = maxx;
            MaxY = maxy;
        }

        public bool IsInBounds(float x, float y)
        {
            return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
        }

        public bool IsInBounds(NetsquareTransformFrame position)
        {
            return position.x >= MinX && position.x <= MaxX && position.z >= MinY && position.z <= MaxY;
        }
    }
}