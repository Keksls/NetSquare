using System;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Defines the built-in destinations selected for a log entry.
    /// </summary>
    [Flags]
    internal enum NetSquareLogTarget
    {
        None = 0,
        Console = 1,
        File = 2
    }
}
