using System;
using System.Collections.Generic;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Represents an immutable structured entry stored inside the Writer ring buffer.
    /// </summary>
    public readonly struct NetSquareLogEntry
    {
        public DateTime TimestampUtc { get; }
        public NetSquareLogLevel Level { get; }
        public WriterCategory Category { get; }
        public string EventName { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public IReadOnlyList<NetSquareLogProperty> Properties { get; }
        public ConsoleColor ConsoleColor { get; }
        public bool AppendNewLine { get; }
        internal NetSquareLogTarget Targets { get; }
        internal WriterMessageBuffer MessageBuffer { get; }
        internal int BufferedMessageLength { get; }
        internal bool IsBufferedMessage => MessageBuffer.IsValid;

        /// <summary>
        /// Initializes an entry that references an existing string message.
        /// </summary>
        internal NetSquareLogEntry(
            DateTime timestampUtc,
            NetSquareLogLevel level,
            WriterCategory category,
            string eventName,
            string message,
            Exception exception,
            IReadOnlyList<NetSquareLogProperty> properties,
            ConsoleColor consoleColor,
            bool appendNewLine,
            NetSquareLogTarget targets)
        {
            // Caller-owned strings require no additional Writer allocation.
            TimestampUtc = timestampUtc;
            Level = level;
            Category = category;
            EventName = eventName;
            Message = message ?? string.Empty;
            Exception = exception;
            Properties = properties;
            ConsoleColor = consoleColor;
            AppendNewLine = appendNewLine;
            Targets = targets;
            MessageBuffer = default(WriterMessageBuffer);
            BufferedMessageLength = 0;
        }

        /// <summary>
        /// Initializes an entry that owns a preallocated buffered message lease.
        /// </summary>
        internal NetSquareLogEntry(
            DateTime timestampUtc,
            NetSquareLogLevel level,
            WriterCategory category,
            WriterMessageBuffer messageBuffer,
            int bufferedMessageLength,
            Exception exception,
            ConsoleColor consoleColor,
            NetSquareLogTarget targets)
        {
            // The logging worker returns the lease after every destination has consumed it.
            TimestampUtc = timestampUtc;
            Level = level;
            Category = category;
            EventName = null;
            Message = null;
            Exception = exception;
            Properties = null;
            ConsoleColor = consoleColor;
            AppendNewLine = true;
            Targets = targets;
            MessageBuffer = messageBuffer;
            BufferedMessageLength = bufferedMessageLength;
        }
    }
}
