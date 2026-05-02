using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

#region Source
namespace NetSquare.Core
{
    /// <summary>
    /// Records synchronization frames for short replay and debugging sessions.
    /// </summary>
    public sealed class NetSquareTraceRecorder
    {
        #region Variables
        /// <summary>
        /// Stores the trace entries.
        /// </summary>
        private readonly List<NetSquareTraceEntry> entries = new List<NetSquareTraceEntry>();
        /// <summary>
        /// Stores the trace lock.
        /// </summary>
        private readonly object entriesLock = new object();
        /// <summary>
        /// Stores the trace clock.
        /// </summary>
        private readonly Stopwatch stopwatch = new Stopwatch();
        /// <summary>
        /// Stores the next trace index.
        /// </summary>
        private int nextIndex;
        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the maximum entries retained by the recorder.
        /// </summary>
        public int MaxEntries { get; set; }

        /// <summary>
        /// Gets whether the recorder is currently active.
        /// </summary>
        public bool IsRecording { get; private set; }
        #endregion

        #region Construction
        /// <summary>
        /// Initializes a new instance of the trace recorder class.
        /// </summary>
        public NetSquareTraceRecorder()
        {
            MaxEntries = 4096;
        }
        #endregion

        #region Recording
        /// <summary>
        /// Starts recording a new trace.
        /// </summary>
        public void Start()
        {
            lock (entriesLock)
            {
                entries.Clear();
                nextIndex = 0;
                stopwatch.Restart();
                IsRecording = true;
            }
        }

        /// <summary>
        /// Stops recording.
        /// </summary>
        public void Stop()
        {
            lock (entriesLock)
            {
                IsRecording = false;
                stopwatch.Stop();
            }
        }

        /// <summary>
        /// Clears all recorded entries.
        /// </summary>
        public void Clear()
        {
            lock (entriesLock)
                entries.Clear();
        }

        /// <summary>
        /// Records synchronization frames for one client.
        /// </summary>
        /// <param name="clientID">Source client id.</param>
        /// <param name="frames">Frames to record.</param>
        public void Record(uint clientID, IEnumerable<INetSquareSynchFrame> frames)
        {
            if (!IsRecording || frames == null)
                return;

            lock (entriesLock)
            {
                foreach (INetSquareSynchFrame frame in frames)
                    AddEntry(clientID, frame);

                TrimEntries();
            }
        }

        /// <summary>
        /// Adds one frame entry to the trace.
        /// </summary>
        /// <param name="clientID">Source client id.</param>
        /// <param name="frame">Frame to record.</param>
        private void AddEntry(uint clientID, INetSquareSynchFrame frame)
        {
            if (frame == null)
                return;

            NetSquareTraceEntry entry = new NetSquareTraceEntry
            {
                Index = nextIndex++,
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                ClientID = clientID,
                FrameType = frame.SynchFrameType,
                SequenceID = frame.SequenceID,
                NetworkTime = frame.Time
            };

            if (frame is NetsquareTransformFrame transformFrame)
            {
                entry.X = transformFrame.x;
                entry.Y = transformFrame.y;
                entry.Z = transformFrame.z;
                entry.RX = transformFrame.rx;
                entry.RY = transformFrame.ry;
                entry.RZ = transformFrame.rz;
                entry.RW = transformFrame.rw;
            }
            else if (frame is NetSquareStateFrame stateFrame)
            {
                entry.State = stateFrame.States;
            }

            entries.Add(entry);
        }

        /// <summary>
        /// Trims entries to the configured retention cap.
        /// </summary>
        private void TrimEntries()
        {
            if (MaxEntries <= 0 || entries.Count <= MaxEntries)
                return;

            entries.RemoveRange(0, entries.Count - MaxEntries);
        }
        #endregion

        #region Replay
        /// <summary>
        /// Replays the recorded trace through a frame callback.
        /// </summary>
        /// <param name="callback">Callback invoked for each replayed frame.</param>
        /// <param name="timeScalePercent">Replay time scale in percent.</param>
        public void Replay(Action<uint, INetSquareSynchFrame> callback, int timeScalePercent = 100)
        {
            if (callback == null)
                return;

            List<NetSquareTraceEntry> snapshot = GetSnapshot();
            double previousMs = 0;
            int scale = Math.Max(1, timeScalePercent);
            for (int i = 0; i < snapshot.Count; i++)
            {
                NetSquareTraceEntry entry = snapshot[i];
                double waitMs = (entry.ElapsedMilliseconds - previousMs) * 100.0 / scale;
                if (waitMs > 0)
                    Thread.Sleep((int)Math.Min(waitMs, int.MaxValue));

                INetSquareSynchFrame frame = entry.CreateFrame();
                if (frame != null)
                    callback(entry.ClientID, frame);

                previousMs = entry.ElapsedMilliseconds;
            }
        }

        /// <summary>
        /// Gets a copy of all recorded entries.
        /// </summary>
        /// <returns>Trace entries.</returns>
        public List<NetSquareTraceEntry> GetSnapshot()
        {
            lock (entriesLock)
                return new List<NetSquareTraceEntry>(entries);
        }
        #endregion

        #region Export
        /// <summary>
        /// Exports the recorded trace as JSON.
        /// </summary>
        /// <returns>JSON trace.</returns>
        public string ToJson()
        {
            List<NetSquareTraceEntry> snapshot = GetSnapshot();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("{");
            builder.Append("  \"entries\": [").AppendLine();
            for (int i = 0; i < snapshot.Count; i++)
            {
                AppendEntryJson(builder, snapshot[i]);
                if (i < snapshot.Count - 1)
                    builder.Append(',');
                builder.AppendLine();
            }
            builder.AppendLine("  ]");
            builder.AppendLine("}");
            return builder.ToString();
        }

        /// <summary>
        /// Appends one entry as JSON.
        /// </summary>
        /// <param name="builder">Builder to append to.</param>
        /// <param name="entry">Entry to append.</param>
        private static void AppendEntryJson(StringBuilder builder, NetSquareTraceEntry entry)
        {
            builder.AppendLine("    {");
            builder.Append("      \"index\": ").Append(entry.Index).AppendLine(",");
            builder.Append("      \"elapsed_ms\": ").Append(entry.ElapsedMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"client_id\": ").Append(entry.ClientID).AppendLine(",");
            builder.Append("      \"frame_type\": ").Append(entry.FrameType).AppendLine(",");
            builder.Append("      \"sequence_id\": ").Append(entry.SequenceID).AppendLine(",");
            builder.Append("      \"network_time\": ").Append(entry.NetworkTime.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"x\": ").Append(entry.X.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"y\": ").Append(entry.Y.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"z\": ").Append(entry.Z.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"rx\": ").Append(entry.RX.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"ry\": ").Append(entry.RY.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"rz\": ").Append(entry.RZ.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"rw\": ").Append(entry.RW.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine(",");
            builder.Append("      \"state\": ").Append(entry.State).AppendLine();
            builder.Append("    }");
        }
        #endregion
    }

    /// <summary>
    /// Represents one recorded synchronization frame.
    /// </summary>
    public sealed class NetSquareTraceEntry
    {
        #region Variables
        /// <summary>
        /// Stores the trace index.
        /// </summary>
        public int Index;
        /// <summary>
        /// Stores elapsed milliseconds since trace start.
        /// </summary>
        public double ElapsedMilliseconds;
        /// <summary>
        /// Stores the source client id.
        /// </summary>
        public uint ClientID;
        /// <summary>
        /// Stores the frame type.
        /// </summary>
        public byte FrameType;
        /// <summary>
        /// Stores the frame sequence id.
        /// </summary>
        public uint SequenceID;
        /// <summary>
        /// Stores the frame network time.
        /// </summary>
        public float NetworkTime;
        /// <summary>
        /// Stores transform x.
        /// </summary>
        public float X;
        /// <summary>
        /// Stores transform y.
        /// </summary>
        public float Y;
        /// <summary>
        /// Stores transform z.
        /// </summary>
        public float Z;
        /// <summary>
        /// Stores rotation x.
        /// </summary>
        public float RX;
        /// <summary>
        /// Stores rotation y.
        /// </summary>
        public float RY;
        /// <summary>
        /// Stores rotation z.
        /// </summary>
        public float RZ;
        /// <summary>
        /// Stores rotation w.
        /// </summary>
        public float RW;
        /// <summary>
        /// Stores state value.
        /// </summary>
        public int State;
        #endregion

        #region Factory
        /// <summary>
        /// Recreates a synchronization frame from this entry.
        /// </summary>
        /// <returns>Recreated synchronization frame.</returns>
        public INetSquareSynchFrame CreateFrame()
        {
            if (FrameType == 0)
                return new NetsquareTransformFrame(X, Y, Z, RX, RY, RZ, RW, NetworkTime, SequenceID);
            if (FrameType == 1)
                return new NetSquareStateFrame(NetworkTime, State, SequenceID);

            return null;
        }
        #endregion
    }
}
#endregion
