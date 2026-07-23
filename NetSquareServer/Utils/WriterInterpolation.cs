using System;
using System.Runtime.CompilerServices;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Provides preallocated interpolation overloads for Writer.
    /// </summary>
    public static partial class Writer
    {
        /// <summary>
        /// Writes an interpolated level-zero message in the default category.
        /// </summary>
        public static void Write(ref WriterMessageInterpolatedStringHandler message)
        {
            // The handler already owns the accepted destinations and leased message buffer.
            message.Complete(ConsoleColor.White, null);
        }

        /// <summary>
        /// Writes an interpolated level-zero message in a custom category.
        /// </summary>
        public static void Write(WriterCategory category, [InterpolatedStringHandlerArgument("category")] ref WriterMessageInterpolatedStringHandler message)
        {
            // The category was consumed by the handler constructor before interpolation began.
            message.Complete(ConsoleColor.White, null);
        }

        /// <summary>
        /// Writes an interpolated informational message in the default category.
        /// </summary>
        public static void Info(ref WriterInfoInterpolatedStringHandler message)
        {
            // The handler commits directly without repeating category filtering.
            message.Complete(ConsoleColor.Cyan, null);
        }

        /// <summary>
        /// Writes an interpolated informational message in a custom category.
        /// </summary>
        public static void Info(WriterCategory category, [InterpolatedStringHandlerArgument("category")] ref WriterInfoInterpolatedStringHandler message)
        {
            // The category was consumed by the handler constructor before interpolation began.
            message.Complete(ConsoleColor.Cyan, null);
        }

        /// <summary>
        /// Writes an interpolated warning message in the default category.
        /// </summary>
        public static void Warning(ref WriterWarningInterpolatedStringHandler message)
        {
            // The handler commits directly without repeating category filtering.
            message.Complete(ConsoleColor.Yellow, null);
        }

        /// <summary>
        /// Writes an interpolated warning message in a custom category.
        /// </summary>
        public static void Warning(WriterCategory category, [InterpolatedStringHandlerArgument("category")] ref WriterWarningInterpolatedStringHandler message)
        {
            // The category was consumed by the handler constructor before interpolation began.
            message.Complete(ConsoleColor.Yellow, null);
        }

        /// <summary>
        /// Writes an interpolated error message in the default category.
        /// </summary>
        public static void Error(ref WriterErrorInterpolatedStringHandler message)
        {
            // The handler commits directly without repeating category filtering.
            message.Complete(ConsoleColor.Red, null);
        }

        /// <summary>
        /// Writes an interpolated error message and exception in the default category.
        /// </summary>
        public static void Error(Exception exception, ref WriterErrorInterpolatedStringHandler message)
        {
            // Exception rendering remains deferred to the logging worker.
            message.Complete(ConsoleColor.Red, exception);
        }

        /// <summary>
        /// Writes an interpolated error message in a custom category.
        /// </summary>
        public static void Error(WriterCategory category, [InterpolatedStringHandlerArgument("category")] ref WriterErrorInterpolatedStringHandler message)
        {
            // The category was consumed by the handler constructor before interpolation began.
            message.Complete(ConsoleColor.Red, null);
        }

        /// <summary>
        /// Writes an interpolated error message and exception in a custom category.
        /// </summary>
        public static void Error(WriterCategory category, Exception exception, [InterpolatedStringHandlerArgument("category")] ref WriterErrorInterpolatedStringHandler message)
        {
            // Exception rendering remains deferred to the logging worker.
            message.Complete(ConsoleColor.Red, exception);
        }
    }
}
