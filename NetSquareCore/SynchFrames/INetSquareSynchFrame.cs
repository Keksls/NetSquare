namespace NetSquare.Core
{
    public interface INetSquareSynchFrame
    {
        float Time { get; set; }
        byte SynchFrameType { get; set; }
        int Size { get; }

        unsafe void Serialize(ref byte* ptr);

        unsafe void Deserialize(ref byte* ptr);

        unsafe void Serialize(NetworkMessage message);

        unsafe void Deserialize(NetworkMessage message);
    }
}