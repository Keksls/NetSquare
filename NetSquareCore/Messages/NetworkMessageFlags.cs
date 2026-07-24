using System;

namespace NetSquare.Core
{
    /// <summary>
    /// Defines wire-level transformations applied independently to one network message.
    /// </summary>
    [Flags]
    internal enum NetworkMessageFlags : byte
    {
        None = 0,
        Compressed = 1 << 0
    }
}
