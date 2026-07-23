using System;
using System.Runtime.CompilerServices;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Builds an interpolated message message only when at least one destination accepts it.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct WriterMessageInterpolatedStringHandler
    {
        private WriterInterpolatedBuffer writerBuffer;

        /// <summary>
        /// Initializes a handler for the default Writer category.
        /// </summary>
        public WriterMessageInterpolatedStringHandler(int literalLength, int formattedCount, out bool shouldAppend)
            : this(literalLength, formattedCount, Writer.DefaultCategory, out shouldAppend)
        {
            // The category-aware constructor owns filtering and buffer rental.
        }

        /// <summary>
        /// Initializes a handler for a selected Writer category.
        /// </summary>
        public WriterMessageInterpolatedStringHandler(int literalLength, int formattedCount, WriterCategory category, out bool shouldAppend)
        {
            // Literal and formatted counts remain compiler metadata because storage is preallocated.
            writerBuffer = new WriterInterpolatedBuffer(category, NetSquareLogLevel.Message, out shouldAppend);
        }

        /// <summary>
        /// Appends literal text when the entry is enabled.
        /// </summary>
        public void AppendLiteral(string value)
        {
            // Literal text is copied directly into the leased fixed-size buffer.
            writerBuffer.AppendLiteral(value);
        }

        /// <summary>
        /// Appends a string without allocating.
        /// </summary>
        public void AppendFormatted(string value)
        {
            // Strings are copied directly and null values are ignored.
            writerBuffer.AppendString(value);
        }

        /// <summary>
        /// Appends one character without allocating.
        /// </summary>
        public void AppendFormatted(char value)
        {
            // Characters are written directly to the next buffer position.
            writerBuffer.AppendCharacter(value);
        }

        /// <summary>
        /// Appends a Boolean without allocating.
        /// </summary>
        public void AppendFormatted(bool value)
        {
            // Boolean values use cached static literals.
            writerBuffer.AppendBoolean(value);
        }

        /// <summary>
        /// Appends a signed byte without boxing or allocating.
        /// </summary>
        public void AppendFormatted(sbyte value)
        {
            // Small signed integers share the Int64 formatter.
            writerBuffer.AppendInt64(value);
        }

        /// <summary>
        /// Appends an unsigned byte without boxing or allocating.
        /// </summary>
        public void AppendFormatted(byte value)
        {
            // Small unsigned integers share the UInt64 formatter.
            writerBuffer.AppendUInt64(value);
        }

        /// <summary>
        /// Appends a signed short without boxing or allocating.
        /// </summary>
        public void AppendFormatted(short value)
        {
            // Signed integers share the Int64 formatter.
            writerBuffer.AppendInt64(value);
        }

        /// <summary>
        /// Appends an unsigned short without boxing or allocating.
        /// </summary>
        public void AppendFormatted(ushort value)
        {
            // Unsigned integers share the UInt64 formatter.
            writerBuffer.AppendUInt64(value);
        }

        /// <summary>
        /// Appends a signed integer without boxing or allocating.
        /// </summary>
        public void AppendFormatted(int value)
        {
            // Signed integers share the Int64 formatter.
            writerBuffer.AppendInt64(value);
        }

        /// <summary>
        /// Appends an unsigned integer without boxing or allocating.
        /// </summary>
        public void AppendFormatted(uint value)
        {
            // Unsigned integers share the UInt64 formatter.
            writerBuffer.AppendUInt64(value);
        }

        /// <summary>
        /// Appends a signed long without boxing or allocating.
        /// </summary>
        public void AppendFormatted(long value)
        {
            // Int64 values are formatted directly into the leased buffer.
            writerBuffer.AppendInt64(value);
        }

        /// <summary>
        /// Appends an unsigned long without boxing or allocating.
        /// </summary>
        public void AppendFormatted(ulong value)
        {
            // UInt64 values are formatted directly into the leased buffer.
            writerBuffer.AppendUInt64(value);
        }

        /// <summary>
        /// Appends a custom value through the allocation-tolerant fallback path.
        /// </summary>
        public void AppendFormatted<T>(T value)
        {
            // Custom types preserve their ToString behavior outside primitive fast paths.
            writerBuffer.AppendFallback(value, null);
        }

        /// <summary>
        /// Appends a custom value using an explicit format.
        /// </summary>
        public void AppendFormatted<T>(T value, string format)
        {
            // Explicit formats use the fallback path because target frameworks expose different span APIs.
            writerBuffer.AppendFallback(value, format);
        }

        /// <summary>
        /// Appends a custom value using alignment.
        /// </summary>
        public void AppendFormatted<T>(T value, int alignment)
        {
            // Alignment is intentionally isolated in the uncommon allocation-tolerant path.
            writerBuffer.AppendAlignedFallback(value, alignment, null);
        }

        /// <summary>
        /// Appends a custom value using alignment and an explicit format.
        /// </summary>
        public void AppendFormatted<T>(T value, int alignment, string format)
        {
            // Alignment and custom formatting share the uncommon fallback path.
            writerBuffer.AppendAlignedFallback(value, alignment, format);
        }

        /// <summary>
        /// Commits the completed message to Writer.
        /// </summary>
        internal void Complete(ConsoleColor color, Exception exception)
        {
            // The core transfers or returns the preallocated buffer exactly once.
            writerBuffer.Complete(color, exception);
        }
    }
}

