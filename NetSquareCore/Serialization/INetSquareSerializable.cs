namespace NetSquare.Core
{
    public interface INetSquareSerializable
    {
        /// <summary>
        /// Serialize the object
        /// </summary>
        /// <param name="writer"> The writer to use </param>
        void Serialize(NetSquareSerializer serializer);

        /// <summary>
        /// Deserialize the object
        /// </summary>
        /// <param name="reader"> The reader to use </param>
        void Deserialize(NetSquareSerializer serializer);
    }
}