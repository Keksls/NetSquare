using NetSquare.Core;
using System;
using System.Runtime.InteropServices;

namespace NetSquareCore
{
    public struct NetsquareTransformFrame
    {
        public float x;
        public float y;
        public float z;
        public float rx;
        public float ry;
        public float rz;
        public float rw;
        public byte State;
        public float Time;
        public static NetsquareTransformFrame zero { get { return new NetsquareTransformFrame(); } }
        public static int Size { get { return 33; } }

        /// <summary>
        /// Create a new position
        /// </summary>
        /// <param name="_x"> x position</param>
        /// <param name="_y"> y position</param>
        /// <param name="_z"> z position</param>
        /// <param name="_rx"> x rotation</param>
        /// <param name="_ry"> y rotation</param>
        /// <param name="_rz"> z rotation</param>
        /// <param name="_rw"> w rotation</param>
        /// <param name="_state"> state</param>
        /// <param name="_time"> time</param>
        public NetsquareTransformFrame(float _x = 0, float _y = 0, float _z = 0, float _rx = 0, float _ry = 0, float _rz = 0, float _rw = 0, byte _state = 0, float _time = 0)
        {
            x = _x;
            y = _y;
            z = _z;
            rx = _rx;
            ry = _ry;
            rz = _rz;
            rw = _rw;
            State = _state;
            Time = _time;
        }

        /// <summary>
        /// Copy the position from another position
        /// </summary>
        /// <param name="_x"> x position</param>
        /// <param name="_y"> y position</param>
        /// <param name="_z"> z position</param>
        /// <param name="_rx"> x rotation</param>
        /// <param name="_ry"> y rotation</param>
        /// <param name="_rz"> z rotation</param>
        /// <param name="_rw"> w rotation</param>
        /// <param name="_state"> state</param>
        /// <param name="_time"> time</param>
        public NetsquareTransformFrame(float _x, float _y, float _z, float _rx, float _ry, float _rz, float _rw, Enum _state, float _time)
        {
            x = _x;
            y = _y;
            z = _z;
            rx = _rx;
            ry = _ry;
            rz = _rz;
            rw = _rw;
            Time = _time;
            State = Convert.ToByte(_state);
        }

        /// <summary>
        /// Copy the position from another position
        /// </summary>
        /// <param name="transform"> position to copy</param>
        public NetsquareTransformFrame(NetsquareTransformFrame transform)
        {
            x = transform.x;
            y = transform.y;
            z = transform.z;
            rx = transform.rx;
            ry = transform.ry;
            rz = transform.rz;
            rw = transform.rw;
            State = transform.State;
            Time = transform.Time;
        }

        /// <summary>
        /// Deserialize the position from a network message
        /// </summary>
        /// <param name="message"> message to deserialize the position</param>
        public NetsquareTransformFrame(NetworkMessage message)
        {
            x = message.GetFloat();
            y = message.GetFloat();
            z = message.GetFloat();
            rx = message.GetFloat();
            ry = message.GetFloat();
            rz = message.GetFloat();
            rw = message.GetFloat();
            State = message.GetByte();
            Time = message.GetFloat();
        }

        /// <summary>
        /// Set the position to the given position
        /// </summary>
        /// <param name="_x"> x position</param>
        /// <param name="_y"> y position</param>
        /// <param name="_z"> z position</param>
        /// <param name="_rx"> x rotation</param>
        /// <param name="_ry"> y rotation</param>
        /// <param name="_rz"> z rotation</param>
        /// <param name="_rw"> w rotation</param>
        public void Set(float _x, float _y, float _z, float _rx, float _ry, float _rz, float _rw)
        {
            x = _x;
            y = _y;
            z = _z;
            rx = _rx;
            ry = _ry;
            rz = _rz;
            rw = _rw;
        }

        /// <summary>
        /// Set the position to the given position
        /// </summary>
        /// <param name="state"> state to set</param>
        /// <param name="time"> time to set</param>
        public void Set(byte state, float time)
        {
            State = state;
            Time = time;
        }

        /// <summary>
        /// Set the position to the given position
        /// </summary>
        /// <param name="pos"> position to set</param>
        public void Set(NetsquareTransformFrame pos)
        {
            x = pos.x;
            y = pos.y;
            z = pos.z;
            rx = pos.rx;
            ry = pos.ry;
            rz = pos.rz;
            rw = pos.rw;
            State = pos.State;
            Time = pos.Time;
        }

        /// <summary>
        /// Serialize the position to a network message
        /// </summary>
        /// <param name="message"> message to serialize the position</param>
        public void Serialize(NetworkMessage message)
        {
            message.Set(x).Set(y).Set(z).Set(rx).Set(ry).Set(rz).Set(rw).Set(State).Set(Time);
        }

        /// <summary>
        /// Get the distance between two positions
        /// </summary>
        /// <param name="pos1"> first position</param>
        /// <param name="pos2"> second position</param>
        /// <returns> distance between pos1 and pos2</returns>
        public static float Distance(NetsquareTransformFrame pos1, NetsquareTransformFrame pos2)
        {
            float diff_x = pos1.x - pos2.x;
            float diff_y = pos1.y - pos2.y;
            float diff_z = pos1.z - pos2.z;
            return Sqrt(diff_x * diff_x + diff_y * diff_y + diff_z * diff_z);
        }

        /// <summary>
        /// Linearly interpolates between two positions.
        /// </summary>
        /// <param name="a"> start position</param>
        /// <param name="b"> end position</param>
        /// <param name="t"> value between 0 and 1</param>
        /// <returns> interpolated position</returns>
        public static NetsquareTransformFrame Lerp(NetsquareTransformFrame a, NetsquareTransformFrame b, float t)
        {
            t = Clamp01(t);
            return new NetsquareTransformFrame(
                a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t,
                a.z + (b.z - a.z) * t,
                a.rx + (b.rx - a.rx) * t,
                a.ry + (b.ry - a.ry) * t,
                a.rz + (b.rz - a.rz) * t,
                a.rw + (b.rw - a.rw) * t
            );
        }

        /// <summary>
        /// Clamps value between 0 and 1 and returns value
        /// </summary>
        /// <param name="value">value to clamp</param>
        /// <returns>clamped value</returns>
        private static float Clamp01(float value)
        {
            if (value < 0F)
                return 0F;
            else if (value > 1F)
                return 1F;
            else
                return value;
        }

        /// <summary>
        /// Fast square root
        /// </summary>
        /// <param name="z"> value to get the square root</param>
        /// <returns> square root of z</returns>
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

        /// <summary>
        /// Union to get the float square root
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatIntUnion
        {
            [FieldOffset(0)]
            public float f;
            [FieldOffset(0)]
            public int tmp;
        }

        /// <summary>
        /// Check if the position AND rotation is equal to another position AND rotation
        /// </summary>
        /// <param name="transform"> position to compare</param>
        /// <returns> true if the position AND rotation is equal to the given position AND rotation</returns>
        public bool Equals(NetsquareTransformFrame transform)
        {
            return x == transform.x && y == transform.y && z == transform.z && rx == transform.rx && ry == transform.ry && rz == transform.rz && rw == transform.rw;
        }
    }
}