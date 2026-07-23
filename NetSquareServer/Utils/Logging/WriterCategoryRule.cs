namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Stores the output thresholds associated with a category name or hierarchy.
    /// </summary>
    internal sealed class WriterCategoryRule
    {
        internal NetSquareLogLevel? ConsoleMinimumLevel { get; }
        internal NetSquareLogLevel? FileMinimumLevel { get; }

        /// <summary>
        /// Initializes a category output rule.
        /// </summary>
        internal WriterCategoryRule(NetSquareLogLevel? consoleMinimumLevel, NetSquareLogLevel? fileMinimumLevel)
        {
            // Rules are immutable so they can be resolved safely while configuration is locked.
            ConsoleMinimumLevel = consoleMinimumLevel;
            FileMinimumLevel = fileMinimumLevel;
        }
    }
}
