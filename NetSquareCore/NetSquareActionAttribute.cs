using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Use this attribute to link method to a particular HeadID or a networkMessage received by NetSquare
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class NetSquareActionAttribute : Attribute
    {
        /// <summary>
        /// ID of the network message, must be unique
        /// </summary>
        public ushort HeadID;

        /// <summary>
        /// Use this attribute to link method to a particular HeadID or a networkMessage received by NetSquare
        /// </summary>
        /// <param name="ID">ID of the NetworkMessage. Must be unique</param>
        public NetSquareActionAttribute(ushort ID)
        {
            HeadID = ID;
        }
    }
}