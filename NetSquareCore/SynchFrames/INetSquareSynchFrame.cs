#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Defines the i net square synch frame contract.
    /// </summary>
    public interface INetSquareSynchFrame
    {
        float Time { get; set; }
        uint SequenceID { get; set; }
        byte SynchFrameType { get; set; }
        int Size { get; }

        unsafe void Serialize(ref byte* ptr);

        unsafe void Deserialize(ref byte* ptr);

        unsafe void Serialize(NetworkMessage message);

        unsafe void Deserialize(NetworkMessage message);
    }
}
#endregion
