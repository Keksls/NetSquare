using NetSquare.Server.Utils;
using System;

namespace NetSquareDiagnostics
{
    /// <summary>
    /// Consumes Writer benchmark output without adding formatting or input/output costs.
    /// </summary>
    internal sealed class WriterBenchmarkOutput : INetSquareBufferedWriterOutput
    {
        internal static readonly WriterBenchmarkOutput Instance = new WriterBenchmarkOutput();

        /// <summary>
        /// Prevents external instances because the output is stateless.
        /// </summary>
        private WriterBenchmarkOutput()
        {
            // A singleton keeps benchmark setup outside measured allocations.
        }

        /// <summary>
        /// Consumes a compatibility string message without performing work.
        /// </summary>
        public void Write(string text, ConsoleColor color, bool appendNewLine)
        {
            // The benchmark measures Writer transport rather than a terminal device.
        }

        /// <summary>
        /// Consumes a buffered message without constructing a string.
        /// </summary>
        public void Write(char[] buffer, int offset, int length, ConsoleColor color, bool appendNewLine)
        {
            // Direct buffer support preserves the allocation-free worker path.
        }

        /// <summary>
        /// Ignores title updates during benchmarks.
        /// </summary>
        public void SetTitle(string text)
        {
            // Titles are unrelated to logging throughput.
        }
    }
}
