using System;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Extends a Writer output with direct character-buffer support.
    /// </summary>
    internal interface INetSquareBufferedWriterOutput : INetSquareWriterOutput
    {
        /// <summary>
        /// Writes a character-array segment without constructing a string.
        /// </summary>
        void Write(char[] characters, int offset, int length, ConsoleColor color, bool appendNewLine);
    }
}
