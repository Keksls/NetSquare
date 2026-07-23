using System;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Writes entries through host-provided delegates.
    /// </summary>
    internal sealed class DelegateWriterOutput : INetSquareWriterOutput
    {
        private readonly Action<string, ConsoleColor, bool> write;
        private readonly Action<string> setTitle;

        /// <summary>
        /// Initializes a delegate-backed output.
        /// </summary>
        internal DelegateWriterOutput(Action<string, ConsoleColor, bool> write, Action<string> setTitle)
        {
            // Delegates are immutable after registration and safe to call from the worker.
            this.write = write;
            this.setTitle = setTitle;
        }

        /// <summary>
        /// Invokes the host-provided write delegate.
        /// </summary>
        public void Write(string text, ConsoleColor color, bool appendNewLine)
        {
            // The worker isolates any exception raised by host code.
            write?.Invoke(text, color, appendNewLine);
        }

        /// <summary>
        /// Invokes the host-provided title delegate.
        /// </summary>
        public void SetTitle(string text)
        {
            // A missing title delegate intentionally does nothing.
            setTitle?.Invoke(text);
        }
    }
}
