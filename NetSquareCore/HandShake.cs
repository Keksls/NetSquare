using System;

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Represents the hand shake component.
    /// </summary>
    public static class HandShake
    {
        /// <summary>
        /// Stores the random value.
        /// </summary>
        public static Random Random;

        static HandShake()
        {
            Random = new Random();
        }

        /// <summary>
        /// Executes the get random hand shake operation.
        /// </summary>
        public static byte[] GetRandomHandShake(out int rnd1, out int rnd2, out int key)
        {
            rnd1 = Random.Next(int.MinValue, int.MaxValue);
            rnd2 = Random.Next(99, 999999);
            key = GetKey(rnd1, rnd2);
            byte[] array1 = BitConverter.GetBytes(rnd1);
            byte[] array2 = BitConverter.GetBytes(rnd2);
            return new byte[8] { array1[0], array1[1], array1[2], array1[3], array2[0], array2[1], array2[2], array2[3] };
        }

        /// <summary>
        /// Executes the get key operation.
        /// </summary>
        public static int GetKey(int rnd1, int rnd2)
        {
            if (rnd1 < 0)
                rnd1 += rnd2;
            else
                rnd1 -= rnd2;
            byte[] array = BitConverter.GetBytes(rnd1);
            array[0] = (byte)(array[0] + (rnd2 * array[0]) % 255);
            array[1] = (byte)((array[1] * rnd2) % 255);
            array[2] = (byte)(((array[0] + array[1]) / rnd2) % 255);
            array[3] = (byte)(((array[3] + array[1] + array[2]) * (rnd2 / 2)) % 255);

            return BitConverter.ToInt32(array, 0);
        }
    }
}
#endregion
