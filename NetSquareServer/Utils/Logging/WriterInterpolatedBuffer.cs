using System;
using System.Globalization;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Builds an accepted interpolated message directly inside a preallocated Writer buffer.
    /// </summary>
    internal struct WriterInterpolatedBuffer
    {
        private WriterMessageBuffer buffer;
        private WriterCategory category;
        private NetSquareLogTarget targets;
        private NetSquareLogLevel level;
        private int length;
        private bool truncated;
        private bool enabled;

        internal bool IsEnabled => enabled;

        /// <summary>
        /// Rents a message buffer only when the selected category and level are enabled.
        /// </summary>
        internal WriterInterpolatedBuffer(WriterCategory category, NetSquareLogLevel level, out bool shouldAppend)
        {
            // Writer returns both the effective category and precomputed destinations in one check.
            buffer = default(WriterMessageBuffer);
            this.category = null;
            targets = NetSquareLogTarget.None;
            this.level = level;
            length = 0;
            truncated = false;
            enabled = Writer.TryBeginBufferedMessage(category, level, out this.category, out targets, out buffer);
            shouldAppend = enabled;
        }

        /// <summary>
        /// Appends literal text to the leased message buffer.
        /// </summary>
        internal void AppendLiteral(string value)
        {
            // Literal copies are bounded and never allocate.
            AppendString(value);
        }

        /// <summary>
        /// Appends a string value without allocating.
        /// </summary>
        internal void AppendString(string value)
        {
            // Null strings intentionally append no characters, matching StringBuilder behavior.
            if (!enabled || string.IsNullOrEmpty(value) || truncated)
                return;

            int remaining = buffer.Capacity - length;
            if (value.Length <= remaining)
            {
                value.CopyTo(0, buffer.Characters, buffer.Offset + length, value.Length);
                length += value.Length;
                return;
            }

            if (remaining > 0)
                value.CopyTo(0, buffer.Characters, buffer.Offset + length, remaining);
            length += Math.Max(0, remaining);
            MarkTruncated();
        }

        /// <summary>
        /// Appends one character without allocating.
        /// </summary>
        internal void AppendCharacter(char value)
        {
            // Individual characters use the same fixed-capacity truncation policy.
            if (!enabled || truncated)
                return;
            if (length >= buffer.Capacity)
            {
                MarkTruncated();
                return;
            }

            buffer.Characters[buffer.Offset + length] = value;
            length++;
        }

        /// <summary>
        /// Appends a signed integer without boxing or allocating.
        /// </summary>
        internal void AppendInt64(long value)
        {
            // Digits are emitted backward into a stack-local fixed array equivalent.
            if (!enabled || truncated)
                return;

            bool negative = value < 0;
            ulong magnitude = negative ? unchecked((ulong)(-(value + 1)) + 1UL) : (ulong)value;
            AppendUnsignedCore(magnitude, negative);
        }

        /// <summary>
        /// Appends an unsigned integer without boxing or allocating.
        /// </summary>
        internal void AppendUInt64(ulong value)
        {
            // Unsigned values share the integer digit writer.
            if (!enabled || truncated)
                return;
            AppendUnsignedCore(value, false);
        }

        /// <summary>
        /// Appends a Boolean value without allocating.
        /// </summary>
        internal void AppendBoolean(bool value)
        {
            // Static literals avoid Boolean.ToString allocations.
            AppendString(value ? "True" : "False");
        }

        /// <summary>
        /// Appends a Guid in canonical D format through the allocation-tolerant fallback.
        /// </summary>
        internal void AppendGuid(Guid value)
        {
            // Guid formatting is uncommon and stays off primitive numeric hot paths.
            AppendFallback(value, "D");
        }

        /// <summary>
        /// Appends a value through the allocation-tolerant fallback path.
        /// </summary>
        internal void AppendFallback<T>(T value, string format)
        {
            // Uncommon custom types retain correct formatting without affecting primitive fast paths.
            if (!enabled || truncated || (object)value == null)
                return;

            try
            {
                if (value is IFormattable formattable)
                    AppendString(formattable.ToString(format, CultureInfo.CurrentCulture));
                else
                    AppendString(value.ToString());
            }
            catch
            {
                AppendString("<format-error>");
            }
        }

        /// <summary>
        /// Appends a formatted value with alignment through the allocation-tolerant fallback path.
        /// </summary>
        internal void AppendAlignedFallback<T>(T value, int alignment, string format)
        {
            // Alignment stays outside primitive fast paths and is bounded by the leased capacity.
            if (!enabled || truncated || (object)value == null)
                return;

            string text;
            try
            {
                if (value is IFormattable formattable)
                    text = formattable.ToString(format, CultureInfo.CurrentCulture);
                else
                    text = value.ToString();
            }
            catch
            {
                text = "<format-error>";
            }

            int alignmentWidth = alignment == int.MinValue
                ? int.MaxValue
                : Math.Abs(alignment);
            int padding = Math.Max(0, alignmentWidth - text.Length);
            if (alignment > 0)
                AppendPadding(padding);
            AppendString(text);
            if (alignment < 0)
                AppendPadding(padding);
        }

        /// <summary>
        /// Appends bounded alignment padding without creating an intermediate string.
        /// </summary>
        private void AppendPadding(int count)
        {
            // The fixed message capacity caps even extreme compiler-supplied alignment values.
            int boundedCount = Math.Min(count, buffer.Capacity - length);
            for (int index = 0; index < boundedCount && !truncated; index++)
                AppendCharacter(' ');
            if (count > boundedCount)
                MarkTruncated();
        }
        /// <summary>
        /// Commits the completed message to the Writer ring buffer.
        /// </summary>
        internal void Complete(ConsoleColor color, Exception exception)
        {
            // Ownership of the leased buffer transfers to the worker only after enqueue succeeds.
            if (!enabled)
                return;

            Writer.CompleteBufferedMessage(category, level, targets, buffer, length, truncated, color, exception);
            enabled = false;
            buffer = default(WriterMessageBuffer);
        }

        /// <summary>
        /// Emits decimal digits directly into their final message-buffer positions.
        /// </summary>
        private void AppendUnsignedCore(ulong value, bool negative)
        {
            // Capacity is checked once so truncation cannot corrupt partially formatted digits.
            ulong remainingValue = value;
            int digitCount = 1;
            while (remainingValue >= 10UL)
            {
                remainingValue /= 10UL;
                digitCount++;
            }

            int requiredLength = digitCount + (negative ? 1 : 0);
            if (requiredLength > buffer.Capacity - length)
            {
                MarkTruncated();
                return;
            }

            if (negative)
                buffer.Characters[buffer.Offset + length++] = '-';

            int digitOffset = buffer.Offset + length;
            length += digitCount;
            int position = digitOffset + digitCount;
            do
            {
                ulong quotient = value / 10UL;
                buffer.Characters[--position] = (char)('0' + (value - (quotient * 10UL)));
                value = quotient;
            }
            while (value != 0UL);
        }

        /// <summary>
        /// Appends a character-array segment to the leased buffer.
        /// </summary>
        private void AppendCharacters(char[] source, int sourceOffset, int sourceLength)
        {
            // Array copies keep primitive formatting bounded and allocation-free.
            if (!enabled || sourceLength <= 0 || truncated)
                return;

            int remaining = buffer.Capacity - length;
            int copyLength = Math.Min(remaining, sourceLength);
            if (copyLength > 0)
            {
                Array.Copy(source, sourceOffset, buffer.Characters, buffer.Offset + length, copyLength);
                length += copyLength;
            }

            if (copyLength < sourceLength)
                MarkTruncated();
        }

        /// <summary>
        /// Marks a full message and writes a visible truncation suffix.
        /// </summary>
        private void MarkTruncated()
        {
            // The final three characters communicate deterministic fixed-buffer truncation.
            truncated = true;
            length = buffer.Capacity;
            if (buffer.Capacity < 3)
                return;

            int suffixOffset = buffer.Offset + buffer.Capacity - 3;
            buffer.Characters[suffixOffset] = '.';
            buffer.Characters[suffixOffset + 1] = '.';
            buffer.Characters[suffixOffset + 2] = '.';
        }
    }
}
