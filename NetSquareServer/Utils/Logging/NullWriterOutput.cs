using System;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Discards console-style entries.
    /// </summary>
    internal sealed class NullWriterOutput : INetSquareBufferedWriterOutput
    {
        /// <summary>
        /// Discards a text entry.
        /// </summary>
        public void Write(string text, ConsoleColor color, bool appendNewLine)
        {
            // The null output intentionally performs no work.
        }

        /// <summary>
        /// Discards a buffered text entry.
        /// </summary>
        public void Write(char[] characters, int offset, int length, ConsoleColor color, bool appendNewLine)
        {
            // The null output intentionally performs no work.
        }

        /// <summary>
        /// Discards a title update.
        /// </summary>
        public void SetTitle(string text)
        {
            // The null output intentionally performs no work.
        }
    }
}
