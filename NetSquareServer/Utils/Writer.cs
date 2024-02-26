using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NetSquareServer.Utils
{
    public static class Writer
    {
        private static RichTextBox textBox;
        private static bool saveLog = false;
        public static bool DisplayLog { get; private set; }
        public static bool DisplayTitle { get; private set; }
        private static string logPath = Environment.CurrentDirectory + @"\server.log";
        private static string logPrevPath = Environment.CurrentDirectory + @"\server_prev.log";
        private static StreamWriter logFileStream;
        private static ConcurrentQueue<string> LogLines = new ConcurrentQueue<string>();

        static Writer()
        {
            DisplayTitle = true;
        }

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

        public static void StopRecordingLog()
        {
            saveLog = false;
        }

        public static void StartDisplayLog()
        {
            DisplayLog = true;
        }

        public static void StopDisplayLog()
        {
            DisplayLog = false;
        }

        public static void StartDisplayTitle()
        {
            DisplayTitle = true;
        }

        public static void StopDisplayTitle()
        {
            DisplayTitle = false;
        }

        public static void SetOutputAsRichTextBox(RichTextBox tb)
        {
            textBox = tb;
        }

        public static void SetOutputAsConsole()
        {
            textBox = null;
        }

        public static void Title(string text)
        {
            if (DisplayTitle)
                Console.Title = text;
        }

        public static void Write(string text, ConsoleColor color, bool inline = true)
        {
            if (DisplayLog)
            {
                if (textBox == null)
                {
                    Console.ForegroundColor = color;
                    Console.Write(text + (inline ? "\n\r" : ""));
                    Console.ResetColor();
                }
                else
                {
                    textBox.Invoke(new Action(() =>
                    {
                        textBox.SelectionStart = textBox.TextLength;
                        textBox.SelectionLength = 0;

                        textBox.SelectionColor = FromColor(color);
                        textBox.AppendText(text + (inline ? "\n" : ""));
                        textBox.SelectionColor = textBox.ForeColor;
                    }));
                }
            }

            if (saveLog)
            {
                if (inline)
                    AddLineToLog(text + "\n");
                else
                    AddLineToLog(text);
            }
        }

        private static Color FromColor(ConsoleColor c)
        {
            int cInt = (int)c;

            int brightnessCoefficient = ((cInt & 8) > 0) ? 2 : 1;
            int r = ((cInt & 4) > 0) ? 64 * brightnessCoefficient : 0;
            int g = ((cInt & 2) > 0) ? 64 * brightnessCoefficient : 0;
            int b = ((cInt & 1) > 0) ? 64 * brightnessCoefficient : 0;

            return Color.FromArgb(r, g, b);
        }

        public static void Write(string text, bool inline = true)
        {
            Write(text, ConsoleColor.White, inline);
        }

        public static void Write_Database(string text, ConsoleColor color, bool inline = true)
        {
            Database();
            Write(text, color, inline);
        }

        public static void Write_Physical(string text, ConsoleColor color, bool inline = true)
        {
            Physical();
            Write(text, color, inline);
        }

        public static void Write_Spells(string text, ConsoleColor color, bool inline = true)
        {
            Spells();
            Write(text, color, inline);
        }

        public static void Write_Monsters(string text, ConsoleColor color, bool inline = true)
        {
            Monsters();
            Write(text, color, inline);
        }
        public static void Write_Fight(string text, ConsoleColor color, bool inline = true)
        {
            Fight();
            Write(text, color, inline);
        }
        public static void Write_Server(string text, ConsoleColor color, bool inline = true)
        {
            Server();
            Write(text, color, inline);
        }
        public static void Write_PNJ(string text, ConsoleColor color, bool inline = true)
        {
            PNJ();
            Write(text, color, inline);
        }

        public static void Database()
        {
            Write("[Database] ", ConsoleColor.Gray, false);
        }
        public static void Physical()
        {
            Write("[Physical Persistance] ", ConsoleColor.Gray, false);
        }
        public static void Spells()
        {
            Write("[Spells] ", ConsoleColor.Gray, false);
        }
        public static void Monsters()
        {
            Write("[Monsters] ", ConsoleColor.Gray, false);
        }
        public static void Fight()
        {
            Write("[Fight] ", ConsoleColor.Gray, false);
            Console.ForegroundColor = ConsoleColor.Gray;
        }
        public static void Server()
        {
            Write("[Server] ", ConsoleColor.Gray, false);
        }
        public static void PNJ()
        {
            Write("[PNJ] ", ConsoleColor.Gray, false);
        }
    }
}