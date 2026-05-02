using NetSquare.Core;

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Defines the i net square serializable contract.
    /// </summary>
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

/// <summary>
/// Represents the net square serializable extensions component.
/// </summary>
public static class NetSquareSerializableExtensions
{
    /// <summary>
    /// Serialize the object using the serializer associated with message.Serializer
    /// </summary>
    /// <param name="serializable"> The serializable object </param>
    /// <param name="message"> The message containing the serializer </param>
    public static void Serialize(this INetSquareSerializable serializable, NetworkMessage message)
    {
        serializable.Serialize(message.Serializer);
    }

    /// <summary>
    /// Deserialize the object using the serializer associated with message.Serializer
    /// </summary>
    /// <param name="serializable"> The serializable object </param>
    /// <param name="message"> The message containing the serializer </param>
    public static void Deserialize(this INetSquareSerializable serializable, NetworkMessage message)
    {
        serializable.Deserialize(message.Serializer);
    }
}
#endregion
