namespace NetSquare.Core.Messages
{
    public static class NetSquareMessageSerialization
    {
        public static Wire.Serializer Serializer;
        
        static NetSquareMessageSerialization()
        {
            Wire.SerializerOptions options = new Wire.SerializerOptions(false, false);
            Serializer = new Wire.Serializer(options);
        }
    }
}