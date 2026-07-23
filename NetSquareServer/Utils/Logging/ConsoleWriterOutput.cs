using System;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Writes entries to the process console.
    /// </summary>
    internal sealed class ConsoleWriterOutput : INetSquareBufferedWriterOutput
    {
        /// <summary>
        /// Writes colored text to the process console.
        /// </summary>
        public void Write(string text, ConsoleColor color, bool appendNewLine)
        {
            // Console input/output is called exclusively by the Writer worker.
            try
            {
                Console.ForegroundColor = color;
                if (appendNewLine)
                    Console.WriteLine(text);
                else
                    Console.Write(text);
            }
            catch
            {
            }
            finally
            {
                try { Console.ResetColor(); } catch { }
            }
        }

        /// <summary>
        /// Writes a colored character-array segment without constructing a string.
        /// </summary>
        public void Write(char[] characters, int offset, int length, ConsoleColor color, bool appendNewLine)
        {
            // TextWriter exposes compatible buffer overloads on every targeted framework.
            try
            {
                Console.ForegroundColor = color;
                if (appendNewLine)
                    Console.Out.WriteLine(characters, offset, length);
                else
                    Console.Out.Write(characters, offset, length);
            }
            catch
            {
            }
            finally
            {
                try { Console.ResetColor(); } catch { }
            }
        }

        /// <summary>
        /// Updates the process console title.
        /// </summary>
        public void SetTitle(string text)
        {
            // Unsupported hosts are ignored to preserve Writer's helper semantics.
            try { Console.Title = text ?? string.Empty; } catch { }
        }
    }
}
