using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using NetSquare.Server.Utils;

#region Source
namespace NetSquareDiagnostics
{
    /// <summary>
    /// Represents the diagnostics window component.
    /// </summary>
    internal sealed class DiagnosticsWindow : Form
    {
        /// <summary>
        /// Stores the tests button value.
        /// </summary>
        private readonly Button testsButton;
        /// <summary>
        /// Stores the benchmarks button value.
        /// </summary>
        private readonly Button benchmarksButton;
        /// <summary>
        /// Stores the average button value.
        /// </summary>
        private readonly Button averageButton;
        /// <summary>
        /// Stores the full load button value.
        /// </summary>
        private readonly Button fullLoadButton;
        /// <summary>
        /// Stores the clear button value.
        /// </summary>
        private readonly Button clearButton;
        /// <summary>
        /// Stores the output text box value.
        /// </summary>
        private readonly RichTextBox outputTextBox;
        /// <summary>
        /// Stores the status label value.
        /// </summary>
        private readonly Label statusLabel;
        /// <summary>
        /// Stores the is running value.
        /// </summary>
        private bool isRunning;

        /// <summary>
        /// Initializes a new instance of the diagnostics window class.
        /// </summary>
        public DiagnosticsWindow()
        {
            Text = "NetSquare Diagnostics";
            Width = 1040;
            Height = 700;
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(30, 32, 36);

            Panel topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(12, 10, 12, 10),
                BackColor = Color.FromArgb(38, 41, 46)
            };

            testsButton = CreateButton("Tests", 0);
            benchmarksButton = CreateButton("Bench 1x", 112);
            averageButton = CreateButton("Bench x5", 248);
            fullLoadButton = CreateButton("Full x3", 384);
            clearButton = CreateButton("Clear", 520);
            clearButton.Width = 96;

            statusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Right,
                Width = 260,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(210, 214, 220),
                Text = "Ready"
            };

            topPanel.Controls.Add(testsButton);
            topPanel.Controls.Add(benchmarksButton);
            topPanel.Controls.Add(averageButton);
            topPanel.Controls.Add(fullLoadButton);
            topPanel.Controls.Add(clearButton);
            topPanel.Controls.Add(statusLabel);

            outputTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap = false,
                BackColor = Color.FromArgb(18, 20, 24),
                ForeColor = Color.FromArgb(235, 238, 242),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10f),
                Margin = new Padding(0)
            };

            Controls.Add(outputTextBox);
            Controls.Add(topPanel);

            testsButton.Click += async delegate { await RunDiagnosticsAsync("Tests", "--tests-only"); };
            benchmarksButton.Click += async delegate { await RunDiagnosticsAsync("Benchmarks", "--bench-only"); };
            averageButton.Click += async delegate { await RunDiagnosticsAsync("Benchmarks x5", "--bench-only", "--runs", "5"); };
            fullLoadButton.Click += async delegate { await RunDiagnosticsAsync("Full x3", "--bench-only", "--full-load", "--runs", "3"); };
            clearButton.Click += delegate { outputTextBox.Clear(); };
        }

        /// <summary>
        /// Executes the create button operation.
        /// </summary>
        private static Button CreateButton(string text, int left)
        {
            return new Button
            {
                Text = text,
                Left = 12 + left,
                Top = 10,
                Width = 128,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(64, 91, 139),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
        }

        /// <summary>
        /// Executes the run diagnostics async operation.
        /// </summary>
        private async Task RunDiagnosticsAsync(string title, params string[] args)
        {
            if (isRunning)
                return;

            isRunning = true;
            SetButtonsEnabled(false);
            statusLabel.Text = title + " running...";
            AppendLine("");
            AppendLine("=== " + title + " ===");

            TextWriter previousOut = Console.Out;
            TextWriter previousError = Console.Error;
            INetSquareWriterOutput previousWriterOutput = Writer.GetOutput();
            bool previousDisplayLog = Writer.DisplayLog;
            bool previousDisplayTitle = Writer.DisplayTitle;
            using (ControlTextWriter writer = new ControlTextWriter(outputTextBox))
            {
                Console.SetOut(writer);
                Console.SetError(writer);
                Writer.SetOutputAsRichTextBox(outputTextBox);
                Writer.StartDisplayLog();
                Writer.StartDisplayTitle();
                int exitCode = 1;
                try
                {
                    exitCode = await Task.Run(delegate { return Program.RunDiagnostics(args); });
                }
                finally
                {
                    Console.SetOut(previousOut);
                    Console.SetError(previousError);
                    Writer.SetOutput(previousWriterOutput);
                    if (previousDisplayLog)
                        Writer.StartDisplayLog();
                    else
                        Writer.StopDisplayLog();
                    if (previousDisplayTitle)
                        Writer.StartDisplayTitle();
                    else
                        Writer.StopDisplayTitle();
                }

                AppendLine("");
                AppendLine(title + " finished with exit code " + exitCode + ".");
                statusLabel.Text = exitCode == 0 ? title + " passed" : title + " failed";
            }

            SetButtonsEnabled(true);
            isRunning = false;
        }

        /// <summary>
        /// Executes the set buttons enabled operation.
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            testsButton.Enabled = enabled;
            benchmarksButton.Enabled = enabled;
            averageButton.Enabled = enabled;
            fullLoadButton.Enabled = enabled;
            clearButton.Enabled = enabled;
        }

        /// <summary>
        /// Executes the append line operation.
        /// </summary>
        private void AppendLine(string text)
        {
            outputTextBox.AppendText(text + Environment.NewLine);
        }

        /// <summary>
        /// Represents the control text writer component.
        /// </summary>
        private sealed class ControlTextWriter : TextWriter
        {
            /// <summary>
            /// Stores the output value.
            /// </summary>
            private readonly TextBoxBase output;

            /// <summary>
            /// Executes the control text writer operation.
            /// </summary>
            public ControlTextWriter(TextBoxBase output)
            {
                this.output = output;
            }

            /// <summary>
            /// Stores the encoding value.
            /// </summary>
            public override Encoding Encoding
            {
                get { return Encoding.UTF8; }
            }

            /// <summary>
            /// Executes the write operation.
            /// </summary>
            public override void Write(char value)
            {
                Append(value.ToString());
            }

            /// <summary>
            /// Executes the write operation.
            /// </summary>
            public override void Write(string value)
            {
                Append(value);
            }

            /// <summary>
            /// Executes the append operation.
            /// </summary>
            private void Append(string value)
            {
                if (string.IsNullOrEmpty(value) || output.IsDisposed)
                    return;

                if (output.InvokeRequired)
                {
                    try { output.BeginInvoke(new Action<string>(Append), value); } catch { }
                    return;
                }

                output.AppendText(value);
            }
        }
    }
}
#endregion
