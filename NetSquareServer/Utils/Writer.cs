using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

#region Source
namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Defines the i net square writer output contract.
    /// </summary>
    public interface INetSquareWriterOutput
    {
        void Write(string text, ConsoleColor color, bool appendNewLine);
        void SetTitle(string text);
    }

    /// <summary>
    /// Represents the writer component.
    /// </summary>
    public static class Writer
    {
        /// <summary>
        /// Stores the output lock value.
        /// </summary>
        private static readonly object outputLock = new object();
        /// <summary>
        /// Stores the output value.
        /// </summary>
        private static INetSquareWriterOutput output = new ConsoleWriterOutput();
        /// <summary>
        /// Stores the save log value.
        /// </summary>
        private static bool saveLog = false;
        /// <summary>
        /// Gets or sets the display log value.
        /// </summary>
        public static bool DisplayLog { get; private set; }
        /// <summary>
        /// Gets or sets the display title value.
        /// </summary>
        public static bool DisplayTitle { get; private set; }
        /// <summary>
        /// Stores the log path value.
        /// </summary>
        private static string logPath = Environment.CurrentDirectory + @"\server.log";
        /// <summary>
        /// Stores the log prev path value.
        /// </summary>
        private static string logPrevPath = Environment.CurrentDirectory + @"\server_prev.log";
        /// <summary>
        /// Stores the log file stream value.
        /// </summary>
        private static StreamWriter logFileStream;
        /// <summary>
        /// Stores the log lines value.
        /// </summary>
        private static ConcurrentQueue<string> LogLines = new ConcurrentQueue<string>();

        static Writer()
        {
            DisplayTitle = true;
        }

        /// <summary>
        /// Executes the start recording log operation.
        /// </summary>
        public static void StartRecordingLog()
        {
            saveLog = true;
            if (File.Exists(logPath))
            {
                if (File.Exists(logPrevPath))
                    File.Delete(logPrevPath);
                File.Copy(logPath, logPrevPath, true);
                File.Delete(logPath);
            }

            logFileStream = new StreamWriter(logPath);
            Thread saveLogThread = new Thread(SaveLogThread);
            saveLogThread.Start();
            AddLineToLog("Start recording log at " + DateTime.Now.ToString() + "\n");
        }

        static void SaveLogThread()
        {
            while (saveLog)
            {
                while (LogLines.Count > 0)
                {
                    string line = string.Empty;
                    while (!LogLines.TryDequeue(out line))
                        Thread.Sleep(1);
                    logFileStream.Write(line);
                }
                Thread.Sleep(1);
            }
        }

        static void AddLineToLog(string line)
        {
            LogLines.Enqueue(line);
        }

        /// <summary>
        /// Executes the stop recording log operation.
        /// </summary>
        public static void StopRecordingLog()
        {
            saveLog = false;
        }

        /// <summary>
        /// Executes the start display log operation.
        /// </summary>
        public static void StartDisplayLog()
        {
            DisplayLog = true;
        }

        /// <summary>
        /// Executes the stop display log operation.
        /// </summary>
        public static void StopDisplayLog()
        {
            DisplayLog = false;
        }

        /// <summary>
        /// Executes the start display title operation.
        /// </summary>
        public static void StartDisplayTitle()
        {
            DisplayTitle = true;
        }

        /// <summary>
        /// Executes the stop display title operation.
        /// </summary>
        public static void StopDisplayTitle()
        {
            DisplayTitle = false;
        }

        /// <summary>
        /// Executes the set output as rich text box operation.
        /// </summary>
        public static void SetOutputAsRichTextBox(RichTextBox tb)
        {
            SetOutput(tb == null ? null : new TextBoxWriterOutput(tb));
        }

        /// <summary>
        /// Executes the set output as text box operation.
        /// </summary>
        public static void SetOutputAsTextBox(TextBoxBase tb)
        {
            SetOutput(tb == null ? null : new TextBoxWriterOutput(tb));
        }

        /// <summary>
        /// Executes the set output as null operation.
        /// </summary>
        public static void SetOutputAsNull()
        {
            SetOutput(new NullWriterOutput());
        }

        /// <summary>
        /// Executes the set output operation.
        /// </summary>
        public static void SetOutput(Action<string, ConsoleColor, bool> write, Action<string> setTitle = null)
        {
            SetOutput(write == null ? null : new DelegateWriterOutput(write, setTitle));
        }

        /// <summary>
        /// Executes the set output operation.
        /// </summary>
        public static void SetOutput(INetSquareWriterOutput writerOutput)
        {
            lock (outputLock)
                output = writerOutput ?? new ConsoleWriterOutput();
        }

        /// <summary>
        /// Executes the get output operation.
        /// </summary>
        public static INetSquareWriterOutput GetOutput()
        {
            lock (outputLock)
                return output;
        }

        /// <summary>
        /// Executes the set output as console operation.
        /// </summary>
        public static void SetOutputAsConsole()
        {
            SetOutput(new ConsoleWriterOutput());
        }

        /// <summary>
        /// Executes the title operation.
        /// </summary>
        public static void Title(string text)
        {
            if (DisplayTitle)
                WriteTitle(text);
        }

        /// <summary>
        /// Executes the write operation.
        /// </summary>
        public static void Write(string text, ConsoleColor color, bool inline = true)
        {
            if (DisplayLog)
            {
                INetSquareWriterOutput currentOutput = GetOutput();
                try { currentOutput.Write(text, color, inline); } catch { }
            }

            if (saveLog)
            {
                if (inline)
                    AddLineToLog(text + "\n");
                else
                    AddLineToLog(text);
            }
        }

        /// <summary>
        /// Executes the write title operation.
        /// </summary>
        private static void WriteTitle(string text)
        {
            INetSquareWriterOutput currentOutput = GetOutput();
            try { currentOutput.SetTitle(text); } catch { }
        }

        /// <summary>
        /// Executes the from color operation.
        /// </summary>
        private static Color FromColor(ConsoleColor c)
        {
            int cInt = (int)c;

            int brightnessCoefficient = ((cInt & 8) > 0) ? 2 : 1;
            int r = ((cInt & 4) > 0) ? 64 * brightnessCoefficient : 0;
            int g = ((cInt & 2) > 0) ? 64 * brightnessCoefficient : 0;
            int b = ((cInt & 1) > 0) ? 64 * brightnessCoefficient : 0;

            return Color.FromArgb(r, g, b);
        }

        /// <summary>
        /// Executes the write operation.
        /// </summary>
        public static void Write(string text, bool inline = true)
        {
            Write(text, ConsoleColor.White, inline);
        }

        /// <summary>
        /// Executes the write database operation.
        /// </summary>
        public static void Write_Database(string text, ConsoleColor color, bool inline = true)
        {
            Database();
            Write(text, color, inline);
        }

        /// <summary>
        /// Executes the write physical operation.
        /// </summary>
        public static void Write_Physical(string text, ConsoleColor color, bool inline = true)
        {
            Physical();
            Write(text, color, inline);
        }

        /// <summary>
        /// Executes the write spells operation.
        /// </summary>
        public static void Write_Spells(string text, ConsoleColor color, bool inline = true)
        {
            Spells();
            Write(text, color, inline);
        }

        /// <summary>
        /// Executes the write monsters operation.
        /// </summary>
        public static void Write_Monsters(string text, ConsoleColor color, bool inline = true)
        {
            Monsters();
            Write(text, color, inline);
        }
        /// <summary>
        /// Executes the write fight operation.
        /// </summary>
        public static void Write_Fight(string text, ConsoleColor color, bool inline = true)
        {
            Fight();
            Write(text, color, inline);
        }
        /// <summary>
        /// Executes the write server operation.
        /// </summary>
        public static void Write_Server(string text, ConsoleColor color, bool inline = true)
        {
            Server();
            Write(text, color, inline);
        }
        /// <summary>
        /// Executes the write pnj operation.
        /// </summary>
        public static void Write_PNJ(string text, ConsoleColor color, bool inline = true)
        {
            PNJ();
            Write(text, color, inline);
        }

        /// <summary>
        /// Executes the database operation.
        /// </summary>
        public static void Database()
        {
            Write("[Database] ", ConsoleColor.Gray, false);
        }
        /// <summary>
        /// Executes the physical operation.
        /// </summary>
        public static void Physical()
        {
            Write("[Physical Persistance] ", ConsoleColor.Gray, false);
        }
        /// <summary>
        /// Executes the spells operation.
        /// </summary>
        public static void Spells()
        {
            Write("[Spells] ", ConsoleColor.Gray, false);
        }
        /// <summary>
        /// Executes the monsters operation.
        /// </summary>
        public static void Monsters()
        {
            Write("[Monsters] ", ConsoleColor.Gray, false);
        }
        /// <summary>
        /// Executes the fight operation.
        /// </summary>
        public static void Fight()
        {
            Write("[Fight] ", ConsoleColor.Gray, false);
        }
        /// <summary>
        /// Executes the server operation.
        /// </summary>
        public static void Server()
        {
            Write("[Server] ", ConsoleColor.Gray, false);
        }
        /// <summary>
        /// Executes the pnj operation.
        /// </summary>
        public static void PNJ()
        {
            Write("[PNJ] ", ConsoleColor.Gray, false);
        }

        /// <summary>
        /// Represents the console writer output component.
        /// </summary>
        private sealed class ConsoleWriterOutput : INetSquareWriterOutput
        {
            /// <summary>
            /// Executes the write operation.
            /// </summary>
            public void Write(string text, ConsoleColor color, bool appendNewLine)
            {
                try
                {
                    Console.ForegroundColor = color;
                    if (appendNewLine)
                        Console.WriteLine(text);
                    else
                        Console.Write(text);
                }
                catch
                {
                }
                finally
                {
                    try { Console.ResetColor(); } catch { }
                }
            }

            /// <summary>
            /// Executes the set title operation.
            /// </summary>
            public void SetTitle(string text)
            {
                try { Console.Title = text ?? string.Empty; } catch { }
            }
        }

        /// <summary>
        /// Represents the text box writer output component.
        /// </summary>
        private sealed class TextBoxWriterOutput : INetSquareWriterOutput
        {
            /// <summary>
            /// Stores the text box value.
            /// </summary>
            private readonly TextBoxBase textBox;

            /// <summary>
            /// Executes the text box writer output operation.
            /// </summary>
            public TextBoxWriterOutput(TextBoxBase textBox)
            {
                this.textBox = textBox;
            }

            /// <summary>
            /// Executes the write operation.
            /// </summary>
            public void Write(string text, ConsoleColor color, bool appendNewLine)
            {
                if (textBox == null || textBox.IsDisposed)
                    return;

                string value = text + (appendNewLine ? Environment.NewLine : string.Empty);
                if (textBox.InvokeRequired)
                {
                    try { textBox.BeginInvoke(new Action<string, ConsoleColor>(Append), value, color); } catch { }
                    return;
                }

                Append(value, color);
            }

            /// <summary>
            /// Executes the set title operation.
            /// </summary>
            public void SetTitle(string text)
            {
            }

            /// <summary>
            /// Executes the append operation.
            /// </summary>
            private void Append(string text, ConsoleColor color)
            {
                if (textBox == null || textBox.IsDisposed)
                    return;

                RichTextBox richTextBox = textBox as RichTextBox;
                if (richTextBox != null)
                {
                    richTextBox.SelectionStart = richTextBox.TextLength;
                    richTextBox.SelectionLength = 0;
                    richTextBox.SelectionColor = FromColor(color);
                    richTextBox.AppendText(text);
                    richTextBox.SelectionColor = richTextBox.ForeColor;
                    return;
                }

                textBox.AppendText(text);
            }
        }

        /// <summary>
        /// Represents the delegate writer output component.
        /// </summary>
        private sealed class DelegateWriterOutput : INetSquareWriterOutput
        {
            /// <summary>
            /// Stores the write value.
            /// </summary>
            private readonly Action<string, ConsoleColor, bool> write;
            /// <summary>
            /// Stores the set title value.
            /// </summary>
            private readonly Action<string> setTitle;

            /// <summary>
            /// Executes the delegate writer output operation.
            /// </summary>
            public DelegateWriterOutput(Action<string, ConsoleColor, bool> write, Action<string> setTitle)
            {
                this.write = write;
                this.setTitle = setTitle;
            }

            /// <summary>
            /// Executes the write operation.
            /// </summary>
            public void Write(string text, ConsoleColor color, bool appendNewLine)
            {
                write?.Invoke(text, color, appendNewLine);
            }

            /// <summary>
            /// Executes the set title operation.
            /// </summary>
            public void SetTitle(string text)
            {
                setTitle?.Invoke(text);
            }
        }

        /// <summary>
        /// Represents the null writer output component.
        /// </summary>
        private sealed class NullWriterOutput : INetSquareWriterOutput
        {
            /// <summary>
            /// Executes the write operation.
            /// </summary>
            public void Write(string text, ConsoleColor color, bool appendNewLine)
            {
            }

            /// <summary>
            /// Executes the set title operation.
            /// </summary>
            public void SetTitle(string text)
            {
            }
        }
    }
}
#endregion
