using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Identifies a log category and stores its packed precomputed output filters.
    /// </summary>
    public sealed class WriterCategory
    {
        private const int FileMaskShift = 4;
        private int packedLevelMasks;

        public string Name { get; }
        internal string DisplayPrefix { get; }

        /// <summary>
        /// Initializes a writer category with precomputed filters.
        /// </summary>
        internal WriterCategory(string name, NetSquareLogLevel? consoleMinimumLevel, NetSquareLogLevel? fileMinimumLevel)
        {
            // Category instances are created only by Writer so names remain interned and unique.
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DisplayPrefix = "[" + name + "] ";
            ApplyFilters(consoleMinimumLevel, fileMinimumLevel);
        }

        /// <summary>
        /// Replaces the effective console and file filters with one atomic packed value.
        /// </summary>
        internal void ApplyFilters(NetSquareLogLevel? consoleMinimumLevel, NetSquareLogLevel? fileMinimumLevel)
        {
            // One volatile write publishes both destination masks consistently.
            int consoleMask = CreateLevelMask(consoleMinimumLevel);
            int fileMask = CreateLevelMask(fileMinimumLevel) << FileMaskShift;
            Volatile.Write(ref packedLevelMasks, consoleMask | fileMask);
        }

        /// <summary>
        /// Gets the destinations enabled for a severity level.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal NetSquareLogTarget GetTargets(NetSquareLogLevel level, bool consoleEnabled, bool fileEnabled)
        {
            // One volatile read evaluates both console and file filters.
            int levelBit = 1 << (int)level;
            int masks = Volatile.Read(ref packedLevelMasks);
            NetSquareLogTarget targets = NetSquareLogTarget.None;
            if (consoleEnabled && (masks & levelBit) != 0)
                targets |= NetSquareLogTarget.Console;
            if (fileEnabled && (masks & (levelBit << FileMaskShift)) != 0)
                targets |= NetSquareLogTarget.File;
            return targets;
        }

        /// <summary>
        /// Creates a bit mask containing a minimum level and every higher level.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CreateLevelMask(NetSquareLogLevel? minimumLevel)
        {
            // A null threshold explicitly disables the corresponding destination.
            if (!minimumLevel.HasValue)
                return 0;

            int minimumBit = 1 << (int)minimumLevel.Value;
            return ((1 << ((int)NetSquareLogLevel.Error + 1)) - 1) & ~(minimumBit - 1);
        }
    }
}
