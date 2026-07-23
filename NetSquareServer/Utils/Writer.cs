using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Provides fast categorized console and file logging through a bounded asynchronous queue.
    /// </summary>
    public static partial class Writer
    {
        #region Constants
        private const int DefaultQueueCapacity = 8192;
        private const int DefaultMessageBufferSize = 512;
        private const int ReservedHighSeveritySlots = 64;
        private const int DefaultFlushIntervalMilliseconds = 1000;
        private const int ShutdownFlushTimeoutMilliseconds = 5000;
        #endregion

        #region State
        private static readonly object configurationLock = new object();
        private static readonly object workerLock = new object();
        private static readonly object fileLock = new object();
        private static readonly Dictionary<string, WriterCategory> categories = new Dictionary<string, WriterCategory>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, WriterCategoryRule> categoryRules = new Dictionary<string, WriterCategoryRule>(StringComparer.OrdinalIgnoreCase);
        private static readonly AutoResetEvent queueSignal = new AutoResetEvent(false);
        private static readonly ManualResetEventSlim queueIdle = new ManualResetEventSlim(true);
        private static readonly char[] fileFormatBuffer = new char[64];
        private static readonly WriterCategory defaultCategory;

        private static INetSquareWriterOutput output = new ConsoleWriterOutput();
        private static StreamWriter logFileStream;
        private static Thread workerThread;
        private static int displayLog = 1;
        private static int consoleOutputEnabled = 1;
        private static int displayTitle = 1;
        private static int fileEnabled;
        private static int workerStarted;
        private static int shutdownRequested;
        private static int workerScheduled;
        private static int queueCapacity = DefaultQueueCapacity;
        private static int messageBufferSize = DefaultMessageBufferSize;
        private static int flushIntervalMilliseconds = DefaultFlushIntervalMilliseconds;
        private static long droppedLogCount;
        private static long truncatedLogCount;
        private static long unreportedDroppedLogCount;
        private static WriterLogRingBuffer logQueue;
        private static WriterMessageBufferPool messageBufferPool;
        private static NetSquareLogLevel minimumConsoleLevel = NetSquareLogLevel.Message;
        private static NetSquareLogLevel minimumLogLevel = NetSquareLogLevel.Message;
        private static string logPath = Path.Combine(Environment.CurrentDirectory, "server.log");
        #endregion

        #region Properties
        public static bool DisplayLog => Volatile.Read(ref displayLog) != 0;
        public static bool DisplayTitle => Volatile.Read(ref displayTitle) != 0;
        public static WriterCategory DefaultCategory => defaultCategory;
        public static long DroppedLogCount => Interlocked.Read(ref droppedLogCount);
        public static long TruncatedLogCount => Interlocked.Read(ref truncatedLogCount);

        public static int QueueCapacity
        {
            get => Volatile.Read(ref queueCapacity);
            set
            {
                // Queue sizing is fixed once the worker is active to keep the producer path predictable.
                if (value <= ReservedHighSeveritySlots || (value & (value - 1)) != 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "The queue capacity must be a power of two and leave room for high-severity entries.");

                lock (workerLock)
                {
                    if (Volatile.Read(ref workerStarted) != 0)
                        throw new InvalidOperationException("The queue capacity cannot change after Writer has started.");

                    Volatile.Write(ref queueCapacity, value);
                }
            }
        }

        public static int MessageBufferSize
        {
            get => Volatile.Read(ref messageBufferSize);
            set
            {
                // Message buffers are preallocated once and cannot be resized after startup.
                if (value < 64)
                    throw new ArgumentOutOfRangeException(nameof(value));
                lock (workerLock)
                {
                    if (Volatile.Read(ref workerStarted) != 0)
                        throw new InvalidOperationException("The message buffer size cannot change after Writer has started.");
                    Volatile.Write(ref messageBufferSize, value);
                }
            }
        }
        public static int FlushIntervalMilliseconds
        {
            get => Volatile.Read(ref flushIntervalMilliseconds);
            set
            {
                // File flushing is performed by the worker and can be tuned without touching producers.
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                Volatile.Write(ref flushIntervalMilliseconds, value);
            }
        }

        public static NetSquareLogLevel MinimumConsoleLevel
        {
            get
            {
                lock (configurationLock)
                    return minimumConsoleLevel;
            }
            set
            {
                // Updating the default rebuilds category masks outside the logging hot path.
                ValidateLevel(value);
                lock (configurationLock)
                {
                    minimumConsoleLevel = value;
                    ApplyCategoryRules();
                }
            }
        }

        public static NetSquareLogLevel MinimumLogLevel
        {
            get
            {
                lock (configurationLock)
                    return minimumLogLevel;
            }
            set
            {
                // Updating the default rebuilds category masks outside the logging hot path.
                ValidateLevel(value);
                lock (configurationLock)
                {
                    minimumLogLevel = value;
                    ApplyCategoryRules();
                }
            }
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes Writer defaults and registers a final process flush.
        /// </summary>
        static Writer()
        {
            // The default category is created before public NetSquare categories reference Writer.
            defaultCategory = DefineCategory("NetSquare");
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        /// <summary>
        /// Flushes and stops the logging worker when the process exits.
        /// </summary>
        private static void OnProcessExit(object sender, EventArgs eventArgs)
        {
            // Process shutdown is the only automatic blocking path and never runs in normal server loops.
            Shutdown(ShutdownFlushTimeoutMilliseconds);
        }
        #endregion

        #region Category configuration
        /// <summary>
        /// Creates or retrieves a stable category that can be declared by any consuming project.
        /// </summary>
        public static WriterCategory DefineCategory(string name)
        {
            // Category registration happens once and all subsequent log calls use the returned masks directly.
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A category name is required.", nameof(name));

            string normalizedName = name.Trim();
            lock (configurationLock)
            {
                if (categories.TryGetValue(normalizedName, out WriterCategory existingCategory))
                    return existingCategory;

                WriterCategoryRule rule = ResolveCategoryRule(normalizedName);
                WriterCategory category = new WriterCategory(
                    normalizedName,
                    rule == null ? minimumConsoleLevel : rule.ConsoleMinimumLevel,
                    rule == null ? minimumLogLevel : rule.FileMinimumLevel);

                categories.Add(normalizedName, category);
                return category;
            }
        }

        /// <summary>
        /// Configures a category and every descendant category with independent console and file thresholds.
        /// </summary>
        public static void ConfigureCategory(WriterCategory category, NetSquareLogLevel? consoleMinimumLevel, NetSquareLogLevel? logMinimumLevel)
        {
            // Category objects use their stable names so configuration also applies to future descendants.
            if (category == null)
                throw new ArgumentNullException(nameof(category));

            ConfigureCategory(category.Name, consoleMinimumLevel, logMinimumLevel);
        }

        /// <summary>
        /// Configures a named category hierarchy with independent console and file thresholds.
        /// </summary>
        public static void ConfigureCategory(string categoryName, NetSquareLogLevel? consoleMinimumLevel, NetSquareLogLevel? logMinimumLevel)
        {
            // Rules are resolved by longest matching prefix, allowing child categories to override parents.
            if (string.IsNullOrWhiteSpace(categoryName))
                throw new ArgumentException("A category name is required.", nameof(categoryName));

            ValidateOptionalLevel(consoleMinimumLevel);
            ValidateOptionalLevel(logMinimumLevel);

            lock (configurationLock)
            {
                categoryRules[categoryName.Trim()] = new WriterCategoryRule(consoleMinimumLevel, logMinimumLevel);
                ApplyCategoryRules();
            }
        }

        /// <summary>
        /// Removes a category-specific rule and restores its inherited thresholds.
        /// </summary>
        public static bool ResetCategoryConfiguration(string categoryName)
        {
            // Existing categories are recalculated immediately after a rule is removed.
            if (string.IsNullOrWhiteSpace(categoryName))
                return false;

            lock (configurationLock)
            {
                bool removed = categoryRules.Remove(categoryName.Trim());
                if (removed)
                    ApplyCategoryRules();

                return removed;
            }
        }

        /// <summary>
        /// Determines whether at least one built-in destination accepts a category and severity.
        /// </summary>
        public static bool IsEnabled(WriterCategory category, NetSquareLogLevel level)
        {
            // The check is allocation-free and does not access category dictionaries.
            WriterCategory effectiveCategory = category ?? defaultCategory;
            return GetTargets(effectiveCategory, level) != NetSquareLogTarget.None;
        }

        /// <summary>
        /// Recalculates the precomputed masks of every registered category.
        /// </summary>
        private static void ApplyCategoryRules()
        {
            // This method is called only while configurationLock is held.
            foreach (KeyValuePair<string, WriterCategory> pair in categories)
            {
                WriterCategoryRule rule = ResolveCategoryRule(pair.Key);
                pair.Value.ApplyFilters(
                    rule == null ? minimumConsoleLevel : rule.ConsoleMinimumLevel,
                    rule == null ? minimumLogLevel : rule.FileMinimumLevel);
            }
        }

        /// <summary>
        /// Resolves the most specific configured rule for a category hierarchy.
        /// </summary>
        private static WriterCategoryRule ResolveCategoryRule(string categoryName)
        {
            // Longest matching names win over broader parent rules.
            WriterCategoryRule selectedRule = null;
            int selectedLength = -1;

            foreach (KeyValuePair<string, WriterCategoryRule> pair in categoryRules)
            {
                if (pair.Key.Length <= selectedLength || !IsCategoryMatch(categoryName, pair.Key))
                    continue;

                selectedRule = pair.Value;
                selectedLength = pair.Key.Length;
            }

            return selectedRule;
        }

        /// <summary>
        /// Determines whether a category belongs to a configured hierarchy.
        /// </summary>
        private static bool IsCategoryMatch(string categoryName, string configuredName)
        {
            // A dot boundary prevents similarly prefixed category names from matching accidentally.
            return categoryName.Equals(configuredName, StringComparison.OrdinalIgnoreCase)
                || (categoryName.Length > configuredName.Length
                    && categoryName.StartsWith(configuredName, StringComparison.OrdinalIgnoreCase)
                    && categoryName[configuredName.Length] == '.');
        }

        /// <summary>
        /// Validates a concrete log severity.
        /// </summary>
        private static void ValidateLevel(NetSquareLogLevel level)
        {
            // Invalid enum values would produce incorrect bit shifts in the hot path.
            if (level < NetSquareLogLevel.Message || level > NetSquareLogLevel.Error)
                throw new ArgumentOutOfRangeException(nameof(level));
        }

        /// <summary>
        /// Validates an optional log severity used by destination filters.
        /// </summary>
        private static void ValidateOptionalLevel(NetSquareLogLevel? level)
        {
            // Null disables a destination and therefore does not require validation.
            if (level.HasValue)
                ValidateLevel(level.Value);
        }
        #endregion

        #region File recording
        /// <summary>
        /// Starts asynchronous recording in server.log and rotates the previous file.
        /// </summary>
        public static void StartRecordingLog()
        {
            // The parameterless overload preserves the original Writer API.
            StartRecordingLog(Path.Combine(Environment.CurrentDirectory, "server.log"));
        }

        /// <summary>
        /// Starts asynchronous recording in a specified file and rotates its previous version.
        /// </summary>
        public static void StartRecordingLog(string filePath)
        {
            // File setup occurs at configuration time; server threads only enqueue entries afterward.
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A log file path is required.", nameof(filePath));

            string fullPath = Path.GetFullPath(filePath);
            string directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath))
                Directory.CreateDirectory(directoryPath);

            string extension = Path.GetExtension(fullPath);
            string previousPath = Path.Combine(directoryPath ?? string.Empty, Path.GetFileNameWithoutExtension(fullPath) + "_prev" + extension);

            lock (fileLock)
            {
                if (logFileStream != null)
                    throw new InvalidOperationException("Writer is already recording a log file.");

                if (File.Exists(previousPath))
                    File.Delete(previousPath);

                if (File.Exists(fullPath))
                    File.Move(fullPath, previousPath);

                logPath = fullPath;
                logFileStream = new StreamWriter(new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536));
                Volatile.Write(ref fileEnabled, 1);
            }

            EnsureWorkerStarted();
            queueSignal.Set();
            Info(NetSquareLogCategories.Logging, "LogStarted", "Started recording log file", new NetSquareLogProperty("Path", logPath));
        }

        /// <summary>
        /// Stops accepting file entries, drains the queue, and closes the current log file.
        /// </summary>
        public static void StopRecordingLog()
        {
            // File shutdown can wait, but producer threads remain completely independent from it.
            if (Interlocked.Exchange(ref fileEnabled, 0) == 0)
                return;

            Flush(ShutdownFlushTimeoutMilliseconds);
            lock (fileLock)
            {
                logFileStream?.Flush();
                logFileStream?.Dispose();
                logFileStream = null;
            }
        }

        /// <summary>
        /// Waits for queued entries and flushes the active file without stopping Writer.
        /// </summary>
        public static bool Flush(int timeoutMilliseconds = ShutdownFlushTimeoutMilliseconds)
        {
            // Waiting is explicit and intended for shutdown or administrative code only.
            if (timeoutMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));

            bool drained = queueIdle.Wait(timeoutMilliseconds);
            lock (fileLock)
                logFileStream?.Flush();

            return drained;
        }

        /// <summary>
        /// Drains pending entries and terminates the background worker.
        /// </summary>
        public static bool Shutdown(int timeoutMilliseconds = ShutdownFlushTimeoutMilliseconds)
        {
            // Shutdown is idempotent and prevents any new entries from being enqueued.
            if (Interlocked.Exchange(ref shutdownRequested, 1) != 0)
                return queueIdle.IsSet;

            bool drained = Flush(timeoutMilliseconds);
            queueSignal.Set();

            Thread currentWorker = Volatile.Read(ref workerThread);
            if (currentWorker != null && currentWorker != Thread.CurrentThread)
                currentWorker.Join(timeoutMilliseconds);

            lock (fileLock)
            {
                logFileStream?.Flush();
                logFileStream?.Dispose();
                logFileStream = null;
                Volatile.Write(ref fileEnabled, 0);
            }

            return drained;
        }
        #endregion

        #region Display configuration
        /// <summary>
        /// Enables console output for accepted categories and levels.
        /// </summary>
        public static void StartDisplayLog()
        {
            // A volatile flag keeps enabling output independent from the producer queue.
            Volatile.Write(ref displayLog, 1);
        }

        /// <summary>
        /// Disables console output without disabling file recording.
        /// </summary>
        public static void StopDisplayLog()
        {
            // Disabling output makes rejected calls return before allocating an entry.
            Volatile.Write(ref displayLog, 0);
        }

        /// <summary>
        /// Enables console title updates.
        /// </summary>
        public static void StartDisplayTitle()
        {
            // Title updates are independent from log entry filtering.
            Volatile.Write(ref displayTitle, 1);
        }

        /// <summary>
        /// Disables console title updates.
        /// </summary>
        public static void StopDisplayTitle()
        {
            // Title updates are independent from log entry filtering.
            Volatile.Write(ref displayTitle, 0);
        }

        /// <summary>
        /// Uses a RichTextBox as the current console-style output.
        /// </summary>
        public static void SetOutputAsRichTextBox(RichTextBox textBox)
        {
            // UI marshaling remains encapsulated in the output implementation.
            SetOutput(textBox == null ? null : new TextBoxWriterOutput(textBox));
        }

        /// <summary>
        /// Uses a TextBoxBase as the current console-style output.
        /// </summary>
        public static void SetOutputAsTextBox(TextBoxBase textBox)
        {
            // UI marshaling remains encapsulated in the output implementation.
            SetOutput(textBox == null ? null : new TextBoxWriterOutput(textBox));
        }

        /// <summary>
        /// Discards console-style output while preserving file recording.
        /// </summary>
        public static void SetOutputAsNull()
        {
            // The null output avoids conditionals in consumers that temporarily hide logs.
            SetOutput(new NullWriterOutput());
        }

        /// <summary>
        /// Uses delegates as the current console-style output.
        /// </summary>
        public static void SetOutput(Action<string, ConsoleColor, bool> write, Action<string> setTitle = null)
        {
            // Delegates allow host projects to integrate Writer without implementing an interface.
            SetOutput(write == null ? null : new DelegateWriterOutput(write, setTitle));
        }

        /// <summary>
        /// Replaces the current console-style output atomically.
        /// </summary>
        public static void SetOutput(INetSquareWriterOutput writerOutput)
        {
            // Atomic replacement removes the output lock from the worker path.
            INetSquareWriterOutput effectiveOutput = writerOutput ?? new ConsoleWriterOutput();
            Interlocked.Exchange(ref output, effectiveOutput);
            Volatile.Write(ref consoleOutputEnabled, effectiveOutput is NullWriterOutput ? 0 : 1);
        }

        /// <summary>
        /// Gets the current console-style output.
        /// </summary>
        public static INetSquareWriterOutput GetOutput()
        {
            // Volatile reading ensures diagnostics can restore the most recent output safely.
            return Volatile.Read(ref output);
        }

        /// <summary>
        /// Restores standard console output.
        /// </summary>
        public static void SetOutputAsConsole()
        {
            // A new stateless output avoids retaining any previous host delegate or control.
            SetOutput(new ConsoleWriterOutput());
        }

        /// <summary>
        /// Updates the current output title when title display is enabled.
        /// </summary>
        public static void Title(string text)
        {
            // Titles are infrequent and are sent directly to avoid waiting behind queued logs.
            if (!DisplayTitle)
                return;

            try { GetOutput().SetTitle(text); } catch { }
        }
        #endregion

        #region Public logging API
        /// <summary>
        /// Writes a level-zero message using a selected console color.
        /// </summary>
        public static void Write(string text, ConsoleColor color, bool inline = true)
        {
            // The original Writer signature remains the shortest path for console-oriented messages.
            Emit(defaultCategory, NetSquareLogLevel.Message, null, text, null, null, color, inline);
        }

        /// <summary>
        /// Writes a level-zero message using the default console color.
        /// </summary>
        public static void Write(string text, bool inline = true)
        {
            // The overload delegates to the same asynchronous pipeline as every severity method.
            Write(text, ConsoleColor.White, inline);
        }

        /// <summary>
        /// Writes a categorized level-zero message.
        /// </summary>
        public static void Write(WriterCategory category, string text, ConsoleColor color = ConsoleColor.White, bool inline = true)
        {
            // Custom projects can use their declared category without creating a logger object.
            Emit(category, NetSquareLogLevel.Message, null, text, null, null, color, inline);
        }

        /// <summary>
        /// Writes an informational message in the default category.
        /// </summary>
        public static void Info(string message)
        {
            // Informational messages use a consistent color and severity.
            Emit(defaultCategory, NetSquareLogLevel.Information, null, message, null, null, ConsoleColor.Cyan, true);
        }

        /// <summary>
        /// Writes an informational message in a custom category.
        /// </summary>
        public static void Info(WriterCategory category, string message)
        {
            // The category masks are checked before an entry is created.
            Emit(category, NetSquareLogLevel.Information, null, message, null, null, ConsoleColor.Cyan, true);
        }

        /// <summary>
        /// Writes a structured informational event in a custom category.
        /// </summary>
        public static void Info(WriterCategory category, string eventName, string message, params NetSquareLogProperty[] properties)
        {
            // Structured properties are formatted by the worker rather than the calling thread.
            Emit(category, NetSquareLogLevel.Information, eventName, message, null, properties, ConsoleColor.Cyan, true);
        }

        /// <summary>
        /// Writes a warning message in the default category.
        /// </summary>
        public static void Warning(string message)
        {
            // Warning messages use a consistent color and severity.
            Emit(defaultCategory, NetSquareLogLevel.Warning, null, message, null, null, ConsoleColor.Yellow, true);
        }

        /// <summary>
        /// Writes a warning message in a custom category.
        /// </summary>
        public static void Warning(WriterCategory category, string message)
        {
            // The category masks are checked before an entry is created.
            Emit(category, NetSquareLogLevel.Warning, null, message, null, null, ConsoleColor.Yellow, true);
        }

        /// <summary>
        /// Writes a structured warning event in a custom category.
        /// </summary>
        public static void Warning(WriterCategory category, string eventName, string message, params NetSquareLogProperty[] properties)
        {
            // Structured properties are formatted by the worker rather than the calling thread.
            Emit(category, NetSquareLogLevel.Warning, eventName, message, null, properties, ConsoleColor.Yellow, true);
        }

        /// <summary>
        /// Writes an error message in the default category.
        /// </summary>
        public static void Error(string message, Exception exception = null)
        {
            // Exceptions remain unformatted until the worker processes the entry.
            Emit(defaultCategory, NetSquareLogLevel.Error, null, message, exception, null, ConsoleColor.Red, true);
        }

        /// <summary>
        /// Writes an error message in a custom category.
        /// </summary>
        public static void Error(WriterCategory category, string message, Exception exception = null)
        {
            // The category masks are checked before an entry is created.
            Emit(category, NetSquareLogLevel.Error, null, message, exception, null, ConsoleColor.Red, true);
        }

        /// <summary>
        /// Writes a structured error event in a custom category.
        /// </summary>
        public static void Error(WriterCategory category, string eventName, string message, Exception exception, params NetSquareLogProperty[] properties)
        {
            // Exception and property formatting are deferred to the background worker.
            Emit(category, NetSquareLogLevel.Error, eventName, message, exception, properties, ConsoleColor.Red, true);
        }

        /// <summary>
        /// Writes a message with an explicitly selected level and metadata.
        /// </summary>
        public static void Log(WriterCategory category, NetSquareLogLevel level, string eventName, string message, Exception exception = null, params NetSquareLogProperty[] properties)
        {
            // The generic entry point supports custom tooling without weakening the convenience methods.
            ValidateLevel(level);
            Emit(category, level, eventName, message, exception, properties, GetDefaultColor(level), true);
        }
        #endregion

        #region Legacy category helpers
        /// <summary>
        /// Writes a database category message.
        /// </summary>
        public static void Write_Database(string text, ConsoleColor color, bool inline = true)
        {
            // The legacy helper now emits a real filterable category.
            Write(NetSquareLogCategories.Database, text, color, inline);
        }

        /// <summary>
        /// Writes a physical persistence category message.
        /// </summary>
        public static void Write_Physical(string text, ConsoleColor color, bool inline = true)
        {
            // The legacy helper now emits a real filterable category.
            Write(NetSquareLogCategories.PhysicalPersistence, text, color, inline);
        }

        /// <summary>
        /// Writes a spells category message.
        /// </summary>
        public static void Write_Spells(string text, ConsoleColor color, bool inline = true)
        {
            // The legacy helper now emits a real filterable category.
            Write(NetSquareLogCategories.Spells, text, color, inline);
        }

        /// <summary>
        /// Writes a monsters category message.
        /// </summary>
        public static void Write_Monsters(string text, ConsoleColor color, bool inline = true)
        {
            // The legacy helper now emits a real filterable category.
            Write(NetSquareLogCategories.Monsters, text, color, inline);
        }

        /// <summary>
        /// Writes a fight category message.
        /// </summary>
        public static void Write_Fight(string text, ConsoleColor color, bool inline = true)
        {
            // The legacy helper now emits a real filterable category.
            Write(NetSquareLogCategories.Fight, text, color, inline);
        }

        /// <summary>
        /// Writes a server category message.
        /// </summary>
        public static void Write_Server(string text, ConsoleColor color, bool inline = true)
        {
            // The legacy helper now emits a real filterable category.
            Write(NetSquareLogCategories.Server, text, color, inline);
        }

        /// <summary>
        /// Writes a PNJ category message.
        /// </summary>
        public static void Write_PNJ(string text, ConsoleColor color, bool inline = true)
        {
            // The legacy helper now emits a real filterable category.
            Write(NetSquareLogCategories.Pnj, text, color, inline);
        }

        /// <summary>
        /// Writes the legacy database prefix without a new line.
        /// </summary>
        public static void Database()
        {
            // Prefix-only helpers remain available for source compatibility.
            Write("[Database] ", ConsoleColor.Gray, false);
        }

        /// <summary>
        /// Writes the legacy physical persistence prefix without a new line.
        /// </summary>
        public static void Physical()
        {
            // Prefix-only helpers remain available for source compatibility.
            Write("[Physical Persistance] ", ConsoleColor.Gray, false);
        }

        /// <summary>
        /// Writes the legacy spells prefix without a new line.
        /// </summary>
        public static void Spells()
        {
            // Prefix-only helpers remain available for source compatibility.
            Write("[Spells] ", ConsoleColor.Gray, false);
        }

        /// <summary>
        /// Writes the legacy monsters prefix without a new line.
        /// </summary>
        public static void Monsters()
        {
            // Prefix-only helpers remain available for source compatibility.
            Write("[Monsters] ", ConsoleColor.Gray, false);
        }

        /// <summary>
        /// Writes the legacy fight prefix without a new line.
        /// </summary>
        public static void Fight()
        {
            // Prefix-only helpers remain available for source compatibility.
            Write("[Fight] ", ConsoleColor.Gray, false);
        }

        /// <summary>
        /// Writes the legacy server prefix without a new line.
        /// </summary>
        public static void Server()
        {
            // Prefix-only helpers remain available for source compatibility.
            Write("[Server] ", ConsoleColor.Gray, false);
        }

        /// <summary>
        /// Writes the legacy PNJ prefix without a new line.
        /// </summary>
        public static void PNJ()
        {
            // Prefix-only helpers remain available for source compatibility.
            Write("[PNJ] ", ConsoleColor.Gray, false);
        }
        #endregion

        #region Queue and worker
        /// <summary>
        /// Creates and enqueues a string-backed entry only when at least one destination accepts it.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void Emit(WriterCategory category, NetSquareLogLevel level, string eventName, string message, Exception exception, IReadOnlyList<NetSquareLogProperty> properties, ConsoleColor color, bool appendNewLine)
        {
            // Filtering happens before timestamps, entries, or worker synchronization are touched.
            if (Volatile.Read(ref shutdownRequested) != 0)
                return;

            WriterCategory effectiveCategory = category ?? defaultCategory;
            NetSquareLogTarget targets = GetTargets(effectiveCategory, level);
            if (targets == NetSquareLogTarget.None)
                return;

            NetSquareLogEntry entry = new NetSquareLogEntry(DateTime.UtcNow, level, effectiveCategory, eventName, message, exception, properties, color, appendNewLine, targets);
            TryEnqueue(in entry);
        }

        /// <summary>
        /// Filters an interpolated entry and rents its preallocated message buffer.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool TryBeginBufferedMessage(
            WriterCategory category,
            NetSquareLogLevel level,
            out WriterCategory effectiveCategory,
            out NetSquareLogTarget targets,
            out WriterMessageBuffer buffer)
        {
            // The handler captures destinations once and avoids a second filter during completion.
            effectiveCategory = category ?? defaultCategory;
            targets = NetSquareLogTarget.None;
            buffer = default(WriterMessageBuffer);
            if (Volatile.Read(ref shutdownRequested) != 0)
                return false;

            targets = GetTargets(effectiveCategory, level);
            if (targets == NetSquareLogTarget.None)
                return false;

            EnsureWorkerStarted();
            int capacity = logQueue.Capacity;
            int lowSeverityLimit = capacity - Math.Min(ReservedHighSeveritySlots, capacity / 4);
            if (level < NetSquareLogLevel.Warning && logQueue.Count >= lowSeverityLimit)
            {
                RecordDroppedEntry();
                return false;
            }

            if (messageBufferPool.TryRent(out buffer))
                return true;

            RecordDroppedEntry();
            return false;
        }

        /// <summary>
        /// Commits a completed interpolated buffer to the preallocated ring.
        /// </summary>
        internal static void CompleteBufferedMessage(
            WriterCategory category,
            NetSquareLogLevel level,
            NetSquareLogTarget targets,
            WriterMessageBuffer buffer,
            int length,
            bool truncated,
            ConsoleColor color,
            Exception exception)
        {
            // Buffer ownership transfers to the entry and is returned on rejection or consumption.
            if (truncated)
                Interlocked.Increment(ref truncatedLogCount);

            NetSquareLogEntry entry = new NetSquareLogEntry(DateTime.UtcNow, level, category, buffer, length, exception, color, targets);
            TryEnqueue(in entry);
        }

        /// <summary>
        /// Gets accepted destinations through packed category and global state masks.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static NetSquareLogTarget GetTargets(WriterCategory category, NetSquareLogLevel level)
        {
            // Global destination flags are reduced to two volatile reads outside category configuration.
            bool consoleEnabled = DisplayLog && Volatile.Read(ref consoleOutputEnabled) != 0;
            bool currentFileEnabled = Volatile.Read(ref fileEnabled) != 0;
            return category.GetTargets(level, consoleEnabled, currentFileEnabled);
        }

        /// <summary>
        /// Attempts to enqueue an entry without blocking or allocating.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool TryEnqueue(in NetSquareLogEntry entry)
        {
            // Lower severities cannot consume the soft reserve kept for warnings and errors.
            EnsureWorkerStarted();
            int capacity = logQueue.Capacity;
            int lowSeverityLimit = capacity - Math.Min(ReservedHighSeveritySlots, capacity / 4);
            if (entry.Level < NetSquareLogLevel.Warning && logQueue.Count >= lowSeverityLimit)
            {
                ReleaseEntryBuffer(in entry);
                RecordDroppedEntry();
                return false;
            }

            queueIdle.Reset();
            if (!logQueue.TryEnqueue(in entry))
            {
                ReleaseEntryBuffer(in entry);
                RecordDroppedEntry();
                if (logQueue.Count == 0)
                    queueIdle.Set();
                return false;
            }

            // Reconcile the idle flag after publication to close the producer/consumer empty transition race.
            queueIdle.Reset();
            if (logQueue.Count == 0)
                queueIdle.Set();

            ScheduleWorker();
            return true;
        }

        /// <summary>
        /// Starts the preallocated queue, message pool, and single logging worker exactly once.
        /// </summary>
        private static void EnsureWorkerStarted()
        {
            // Only the first accepted entry pays infrastructure allocation and thread creation costs.
            if (Volatile.Read(ref workerStarted) != 0)
                return;

            lock (workerLock)
            {
                if (workerStarted != 0)
                    return;

                int configuredCapacity = Volatile.Read(ref queueCapacity);
                logQueue = new WriterLogRingBuffer(configuredCapacity);
                messageBufferPool = new WriterMessageBufferPool(configuredCapacity, Volatile.Read(ref messageBufferSize));
                workerThread = new Thread(ProcessEntries)
                {
                    IsBackground = true,
                    Name = "NetSquare Writer"
                };
                Volatile.Write(ref workerStarted, 1);
                workerThread.Start();
            }
        }

        /// <summary>
        /// Signals the worker only when no wake-up is already pending.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ScheduleWorker()
        {
            // Empty-to-non-empty transitions avoid one kernel signal per accepted entry.
            if (Interlocked.Exchange(ref workerScheduled, 1) == 0)
                queueSignal.Set();
        }

        /// <summary>
        /// Drains ring entries and performs all console and file input/output.
        /// </summary>
        private static void ProcessEntries()
        {
            // This worker is the only normal execution path that writes log data.
            DateTime nextFileFlushUtc = DateTime.UtcNow.AddMilliseconds(Volatile.Read(ref flushIntervalMilliseconds));
            while (Volatile.Read(ref shutdownRequested) == 0 || logQueue.Count > 0)
            {
                int waitMilliseconds = Volatile.Read(ref fileEnabled) != 0
                    ? Math.Max(1, (int)(nextFileFlushUtc - DateTime.UtcNow).TotalMilliseconds)
                    : Timeout.Infinite;
                queueSignal.WaitOne(waitMilliseconds);

                bool continueDraining;
                do
                {
                    while (logQueue.TryDequeue(out NetSquareLogEntry entry))
                    {
                        try { ProcessEntry(in entry); } catch { }
                        if (logQueue.Count == 0)
                            queueIdle.Set();
                    }

                    Volatile.Write(ref workerScheduled, 0);
                    continueDraining = logQueue.Count > 0 && Interlocked.Exchange(ref workerScheduled, 1) == 0;
                }
                while (continueDraining);

                ReportDroppedEntries();
                if (DateTime.UtcNow >= nextFileFlushUtc)
                {
                    FlushFileFromWorker();
                    nextFileFlushUtc = DateTime.UtcNow.AddMilliseconds(Volatile.Read(ref flushIntervalMilliseconds));
                }
            }

            FlushFileFromWorker();
            queueIdle.Set();
        }

        /// <summary>
        /// Sends an entry to its destinations and returns any leased message buffer.
        /// </summary>
        private static void ProcessEntry(in NetSquareLogEntry entry)
        {
            // Buffer return is guaranteed even when a destination throws.
            try
            {
                if ((entry.Targets & NetSquareLogTarget.Console) != 0)
                    WriteConsoleEntry(in entry);
                if ((entry.Targets & NetSquareLogTarget.File) != 0)
                    WriteFileEntry(in entry);
            }
            finally
            {
                ReleaseEntryBuffer(in entry);
            }
        }

        /// <summary>
        /// Displays a categorized entry through the current console-style output.
        /// </summary>
        private static void WriteConsoleEntry(in NetSquareLogEntry entry)
        {
            // Built-in outputs consume buffered messages directly; custom legacy outputs receive a fallback string.
            INetSquareWriterOutput currentOutput = GetOutput();
            try
            {
                if (!ReferenceEquals(entry.Category, defaultCategory))
                    currentOutput.Write(entry.Category.DisplayPrefix, ConsoleColor.Gray, false);

                if (entry.IsBufferedMessage)
                {
                    INetSquareBufferedWriterOutput bufferedOutput = currentOutput as INetSquareBufferedWriterOutput;
                    if (bufferedOutput != null)
                        bufferedOutput.Write(entry.MessageBuffer.Characters, entry.MessageBuffer.Offset, entry.BufferedMessageLength, entry.ConsoleColor, entry.AppendNewLine);
                    else
                        currentOutput.Write(new string(entry.MessageBuffer.Characters, entry.MessageBuffer.Offset, entry.BufferedMessageLength), entry.ConsoleColor, entry.AppendNewLine);
                }
                else
                {
                    currentOutput.Write(entry.Message, entry.ConsoleColor, entry.AppendNewLine);
                }

                if (entry.Exception != null)
                    currentOutput.Write(entry.Exception.ToString(), ConsoleColor.Red, true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Streams an entry directly to the active file without constructing a formatted string.
        /// </summary>
        private static void WriteFileEntry(in NetSquareLogEntry entry)
        {
            // A reusable worker scratch buffer handles timestamps and integer properties.
            lock (fileLock)
            {
                if (logFileStream != null)
                    WriterTextFormatter.WriteEntry(logFileStream, in entry, fileFormatBuffer);
            }
        }

        /// <summary>
        /// Returns an entry-owned message buffer to the preallocated pool.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ReleaseEntryBuffer(in NetSquareLogEntry entry)
        {
            // String-backed entries have no lease and leave this method immediately.
            if (entry.IsBufferedMessage)
            {
                WriterMessageBuffer messageBuffer = entry.MessageBuffer;
                messageBufferPool.Return(in messageBuffer);
            }
        }

        /// <summary>
        /// Records one non-blocking queue or buffer-pool rejection.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void RecordDroppedEntry()
        {
            // Atomic counters preserve diagnostics without recursively producing another log.
            Interlocked.Increment(ref droppedLogCount);
            Interlocked.Increment(ref unreportedDroppedLogCount);
        }

        /// <summary>
        /// Reports queue overflow without recursively enqueueing another entry.
        /// </summary>
        private static void ReportDroppedEntries()
        {
            // The report is produced by the worker and therefore cannot add producer pressure.
            long droppedCount = Interlocked.Exchange(ref unreportedDroppedLogCount, 0);
            if (droppedCount <= 0)
                return;

            NetSquareLogTarget targets = GetTargets(NetSquareLogCategories.Logging, NetSquareLogLevel.Warning);
            if (targets == NetSquareLogTarget.None)
                return;

            NetSquareLogEntry entry = new NetSquareLogEntry(
                DateTime.UtcNow,
                NetSquareLogLevel.Warning,
                NetSquareLogCategories.Logging,
                "LogEntriesDropped",
                droppedCount.ToString(CultureInfo.InvariantCulture) + " log entries were dropped because the queue or message pool was full.",
                null,
                null,
                ConsoleColor.Yellow,
                true,
                targets);
            ProcessEntry(in entry);
        }

        /// <summary>
        /// Flushes the active stream from the logging worker.
        /// </summary>
        private static void FlushFileFromWorker()
        {
            // Periodic batched flushing avoids a disk flush for every entry.
            lock (fileLock)
                logFileStream?.Flush();
        }

        /// <summary>
        /// Selects the default console color associated with a severity.
        /// </summary>
        private static ConsoleColor GetDefaultColor(NetSquareLogLevel level)
        {
            // Color is presentation-only and never determines severity or filtering.
            switch (level)
            {
                case NetSquareLogLevel.Information:
                    return ConsoleColor.Cyan;
                case NetSquareLogLevel.Warning:
                    return ConsoleColor.Yellow;
                case NetSquareLogLevel.Error:
                    return ConsoleColor.Red;
                default:
                    return ConsoleColor.White;
            }
        }
        #endregion
    }
}