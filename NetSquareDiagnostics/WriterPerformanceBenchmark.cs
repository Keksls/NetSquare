using NetSquare.Server.Utils;
using System;
using System.Diagnostics;

namespace NetSquareDiagnostics
{
    /// <summary>
    /// Measures Writer producer allocations and asynchronous transport throughput in isolation.
    /// </summary>
    internal static class WriterPerformanceBenchmark
    {
        private const int DefaultIterations = 100000;
        private const int WarmupIterations = 2000;

        /// <summary>
        /// Runs filtered and accepted interpolated-message benchmarks in a dedicated process mode.
        /// </summary>
        internal static int Run(int iterations)
        {
            // The benchmark configures Writer before its worker starts so queue memory is deterministic.
            iterations = Math.Max(1, iterations <= 0 ? DefaultIterations : iterations);
            Writer.QueueCapacity = 16384;
            Writer.MessageBufferSize = 256;
            Writer.MinimumConsoleLevel = NetSquareLogLevel.Information;
            Writer.MinimumLogLevel = NetSquareLogLevel.Information;

            WriterCategory category = Writer.DefineCategory("Diagnostics.Writer");
            Console.WriteLine("Writer performance benchmark (" + iterations + " iterations)");
            BenchmarkFiltered(category, iterations);
            BenchmarkAccepted(category, iterations);
            Writer.Shutdown();
            return 0;
        }

        /// <summary>
        /// Measures the disabled interpolation path where no buffer or queue entry is created.
        /// </summary>
        private static void BenchmarkFiltered(WriterCategory category, int iterations)
        {
            // Null output disables the only destination before the interpolated expressions execute.
            Writer.SetOutputAsNull();
            for (int index = 0; index < WarmupIterations; index++)
                Writer.Info(category, $"Filtered player {index}");

            Stopwatch stopwatch = new Stopwatch();
            CollectGarbage();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            stopwatch.Start();
            for (int index = 0; index < iterations; index++)
                Writer.Info(category, $"Filtered player {index}");
            stopwatch.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            PrintResult("filtered", iterations, stopwatch.Elapsed, allocatedBytes, 0);
        }

        /// <summary>
        /// Measures producer cost and end-to-end draining for accepted buffered interpolations.
        /// </summary>
        private static void BenchmarkAccepted(WriterCategory category, int iterations)
        {
            // A buffered null sink removes console formatting while keeping the real worker and queue active.
            Writer.SetOutput(WriterBenchmarkOutput.Instance);
            for (int index = 0; index < WarmupIterations; index++)
                Writer.Info(category, $"Accepted player {index} at tick {index}");
            Writer.Flush();

            Stopwatch producerStopwatch = new Stopwatch();
            CollectGarbage();
            long droppedBefore = Writer.DroppedLogCount;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            producerStopwatch.Start();
            for (int index = 0; index < iterations; index++)
                Writer.Info(category, $"Accepted player {index} at tick {index}");
            producerStopwatch.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Stopwatch drainStopwatch = Stopwatch.StartNew();
            Writer.Flush();
            drainStopwatch.Stop();
            long dropped = Writer.DroppedLogCount - droppedBefore;

            PrintResult("accepted producer", iterations, producerStopwatch.Elapsed, allocatedBytes, dropped);
            Console.WriteLine("  drain: " + drainStopwatch.Elapsed.TotalMilliseconds.ToString("F3") + " ms");
        }

        /// <summary>
        /// Forces collection before an allocation measurement outside the timed region.
        /// </summary>
        private static void CollectGarbage()
        {
            // Completing pending finalizers stabilizes repeated local measurements.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Prints one compact benchmark result with throughput and producer allocations.
        /// </summary>
        private static void PrintResult(string name, int iterations, TimeSpan elapsed, long allocatedBytes, long dropped)
        {
            // Allocation per call makes regressions visible independently from iteration count.
            double operationsPerSecond = elapsed.TotalSeconds <= 0 ? 0 : iterations / elapsed.TotalSeconds;
            double bytesPerCall = iterations <= 0 ? 0 : (double)allocatedBytes / iterations;
            Console.WriteLine(
                "  " + name + ": " + operationsPerSecond.ToString("N0") + " calls/s, " +
                allocatedBytes + " producer bytes (" + bytesPerCall.ToString("F4") + "/call), dropped=" + dropped);
        }
    }
}
