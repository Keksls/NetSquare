using System;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Defines a destination used to display Writer console output.
    /// </summary>
    public interface INetSquareWriterOutput
    {
        /// <summary>
        /// Writes text to the configured display output.
        /// </summary>
        void Write(string text, ConsoleColor color, bool appendNewLine);

        /// <summary>
        /// Updates the configured display title.
        /// </summary>
        void SetTitle(string text);
    }
}
