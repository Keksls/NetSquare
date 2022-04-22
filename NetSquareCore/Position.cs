using System.Runtime.InteropServices;

namespace NetSquareCore
{
    public struct Position
    {
        public float x;
        public float y;
        public float z;

        public Position(float _x = 0, float _y = 0, float _z = 0)
        {
            x = _x;
            y = _y;
            z = _z;
        }

        public void Set(float _x, float _y, float _z)
        {
            x = _x;
            y = _y;
            z = _z;
        }

        public void Set(Position pos)
        {
            x = pos.x;
            y = pos.y;
            z = pos.z;
        }

        public static Position zero { get { return new Position(); } }

        public static float Distance(Position pos1, Position pos2)
        {
            float diff_x = pos1.x - pos2.x;
            float diff_y = pos1.y - pos2.y;
            float diff_z = pos1.z - pos2.z;
            return Sqrt(diff_x * diff_x + diff_y * diff_y + diff_z * diff_z);
        }

        public static Position Lerp(Position a, Position b, float t)
        {
            t = Clamp01(t);
            return new Position(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t
            );
        }

        // Clamps value between 0 and 1 and returns value
        private static float Clamp01(float value)
        {
            if (value < 0F)
                return 0F;
            else if (value > 1F)
                return 1F;
            else
                return value;
        }

        private static float Sqrt(float z)
        {
            if (z == 0) return 0;
            FloatIntUnion u;
            u.tmp = 0;
            u.f = z;
            u.tmp -= 1 << 23; /* Subtract 2^m. */
            u.tmp >>= 1; /* Divide by 2. */
            u.tmp += 1 << 29; /* Add ((b + 1) / 2) * 2^m. */
            return u.f;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatIntUnion
        {
            [FieldOffset(0)]
            public float f;
            [FieldOffset(0)]
            public int tmp;
        }
    }
}