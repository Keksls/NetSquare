namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Identifies the allocation-free storage used by a structured log property.
    /// </summary>
    internal enum NetSquareLogValueKind : byte
    {
        Null,
        String,
        SignedInteger,
        UnsignedInteger,
        FloatingPoint,
        Decimal,
        Boolean,
        Guid,
        DateTime,
        Object
    }
}
