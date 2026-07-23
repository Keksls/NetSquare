using System;
using System.Globalization;
using System.IO;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Writes structured log entries directly to a TextWriter without building intermediate strings.
    /// </summary>
    internal static class WriterTextFormatter
    {
        private const int TimestampLength = 28;

        /// <summary>
        /// Writes one complete structured entry through reusable worker scratch storage.
        /// </summary>
        internal static void WriteEntry(StreamWriter stream, in NetSquareLogEntry entry, char[] scratchBuffer)
        {
            // Header, message, properties, and exception are streamed as independent segments.
            stream.Write('[');
            WriteTimestamp(stream, entry.TimestampUtc, scratchBuffer);
            stream.Write("] [");
            stream.Write(GetLevelName(entry.Level));
            stream.Write("] [");
            stream.Write(entry.Category.Name);
            stream.Write("] ");
            if (!string.IsNullOrEmpty(entry.EventName))
            {
                stream.Write('[');
                stream.Write(entry.EventName);
                stream.Write("] ");
            }

            WriteMessage(stream, entry);
            WriteProperties(stream, entry.Properties, scratchBuffer);
            if (entry.Exception != null)
            {
                stream.WriteLine();
                stream.Write(entry.Exception.ToString());
            }

            if (entry.AppendNewLine)
                stream.WriteLine();
        }

        /// <summary>
        /// Writes an entry message from either its existing string or leased character buffer.
        /// </summary>
        private static void WriteMessage(StreamWriter stream, in NetSquareLogEntry entry)
        {
            // Buffered interpolations bypass string creation entirely.
            if (entry.IsBufferedMessage)
            {
                stream.Write(entry.MessageBuffer.Characters, entry.MessageBuffer.Offset, entry.BufferedMessageLength);
                return;
            }

            stream.Write(entry.Message);
        }

        /// <summary>
        /// Writes every structured property through its typed storage path.
        /// </summary>
        private static void WriteProperties(StreamWriter stream, System.Collections.Generic.IReadOnlyList<NetSquareLogProperty> properties, char[] scratchBuffer)
        {
            // The params-array API remains compatible while values avoid boxing internally.
            if (properties == null)
                return;

            for (int index = 0; index < properties.Count; index++)
            {
                NetSquareLogProperty property = properties[index];
                stream.Write(" | ");
                stream.Write(property.Name);
                stream.Write('=');
                WritePropertyValue(stream, property, scratchBuffer);
            }
        }

        /// <summary>
        /// Writes one property value according to its unboxed value kind.
        /// </summary>
        private static void WritePropertyValue(StreamWriter stream, in NetSquareLogProperty property, char[] scratchBuffer)
        {
            // Primitive integer and Boolean paths create no temporary objects or strings.
            switch (property.ValueKind)
            {
                case NetSquareLogValueKind.String:
                    stream.Write((string)property.ReferenceValue);
                    break;
                case NetSquareLogValueKind.SignedInteger:
                    WriteInt64(stream, property.SignedValue, scratchBuffer);
                    break;
                case NetSquareLogValueKind.UnsignedInteger:
                    WriteUInt64(stream, property.UnsignedValue, scratchBuffer);
                    break;
                case NetSquareLogValueKind.FloatingPoint:
                    stream.Write(property.FloatingValue.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case NetSquareLogValueKind.Decimal:
                    stream.Write(property.DecimalValue.ToString(CultureInfo.InvariantCulture));
                    break;
                case NetSquareLogValueKind.Boolean:
                    stream.Write(property.UnsignedValue != 0 ? "True" : "False");
                    break;
                case NetSquareLogValueKind.Guid:
                    stream.Write(property.GuidValue.ToString("D"));
                    break;
                case NetSquareLogValueKind.DateTime:
                    WriteTimestamp(stream, new DateTime(property.SignedValue, (DateTimeKind)property.UnsignedValue), scratchBuffer);
                    break;
                case NetSquareLogValueKind.Object:
                    stream.Write(Convert.ToString(property.ReferenceValue, CultureInfo.InvariantCulture));
                    break;
                default:
                    stream.Write("null");
                    break;
            }
        }

        /// <summary>
        /// Writes an ISO-8601 UTC timestamp into reusable scratch storage.
        /// </summary>
        private static void WriteTimestamp(StreamWriter stream, DateTime value, char[] scratchBuffer)
        {
            // Fixed-width numeric fields avoid DateTime.ToString allocations.
            DateTime utcValue = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            WriteFourDigits(scratchBuffer, 0, utcValue.Year);
            scratchBuffer[4] = '-';
            WriteTwoDigits(scratchBuffer, 5, utcValue.Month);
            scratchBuffer[7] = '-';
            WriteTwoDigits(scratchBuffer, 8, utcValue.Day);
            scratchBuffer[10] = 'T';
            WriteTwoDigits(scratchBuffer, 11, utcValue.Hour);
            scratchBuffer[13] = ':';
            WriteTwoDigits(scratchBuffer, 14, utcValue.Minute);
            scratchBuffer[16] = ':';
            WriteTwoDigits(scratchBuffer, 17, utcValue.Second);
            scratchBuffer[19] = '.';
            int fraction = (int)(utcValue.Ticks % TimeSpan.TicksPerSecond);
            for (int index = 26; index >= 20; index--)
            {
                scratchBuffer[index] = (char)('0' + (fraction % 10));
                fraction /= 10;
            }
            scratchBuffer[27] = 'Z';
            stream.Write(scratchBuffer, 0, TimestampLength);
        }

        /// <summary>
        /// Writes a signed integer into reusable scratch storage.
        /// </summary>
        private static void WriteInt64(StreamWriter stream, long value, char[] scratchBuffer)
        {
            // Int64.MinValue is converted through an unsigned magnitude without overflow.
            bool negative = value < 0;
            ulong magnitude = negative ? unchecked((ulong)(-(value + 1)) + 1UL) : (ulong)value;
            int start = FormatUInt64(magnitude, scratchBuffer);
            if (negative)
                scratchBuffer[--start] = '-';
            stream.Write(scratchBuffer, start, scratchBuffer.Length - start);
        }

        /// <summary>
        /// Writes an unsigned integer into reusable scratch storage.
        /// </summary>
        private static void WriteUInt64(StreamWriter stream, ulong value, char[] scratchBuffer)
        {
            // Digits are produced backward and then written as one array segment.
            int start = FormatUInt64(value, scratchBuffer);
            stream.Write(scratchBuffer, start, scratchBuffer.Length - start);
        }

        /// <summary>
        /// Formats unsigned integer digits backward into reusable scratch storage.
        /// </summary>
        private static int FormatUInt64(ulong value, char[] scratchBuffer)
        {
            // The caller owns the scratch array exclusively on the Writer worker.
            int position = scratchBuffer.Length;
            do
            {
                ulong quotient = value / 10UL;
                scratchBuffer[--position] = (char)('0' + (value - (quotient * 10UL)));
                value = quotient;
            }
            while (value != 0UL);
            return position;
        }

        /// <summary>
        /// Writes a two-digit positive integer at a fixed buffer position.
        /// </summary>
        private static void WriteTwoDigits(char[] buffer, int offset, int value)
        {
            // Date components are already constrained to two decimal digits.
            buffer[offset] = (char)('0' + (value / 10));
            buffer[offset + 1] = (char)('0' + (value % 10));
        }

        /// <summary>
        /// Writes a four-digit positive integer at a fixed buffer position.
        /// </summary>
        private static void WriteFourDigits(char[] buffer, int offset, int value)
        {
            // Date years are represented as exactly four decimal digits.
            buffer[offset] = (char)('0' + ((value / 1000) % 10));
            buffer[offset + 1] = (char)('0' + ((value / 100) % 10));
            buffer[offset + 2] = (char)('0' + ((value / 10) % 10));
            buffer[offset + 3] = (char)('0' + (value % 10));
        }

        /// <summary>
        /// Gets the cached textual name of a log level.
        /// </summary>
        private static string GetLevelName(NetSquareLogLevel level)
        {
            // Static literals avoid enum formatting and boxing.
            switch (level)
            {
                case NetSquareLogLevel.Information:
                    return "Information";
                case NetSquareLogLevel.Warning:
                    return "Warning";
                case NetSquareLogLevel.Error:
                    return "Error";
                default:
                    return "Message";
            }
        }
    }
}
