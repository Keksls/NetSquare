using System;
using System.Drawing;
using System.Windows.Forms;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Writes entries to a WinForms text control.
    /// </summary>
    internal sealed class TextBoxWriterOutput : INetSquareWriterOutput
    {
        private readonly TextBoxBase textBox;

        /// <summary>
        /// Initializes a WinForms text output.
        /// </summary>
        internal TextBoxWriterOutput(TextBoxBase textBox)
        {
            // The control lifetime is checked again for every asynchronous append.
            this.textBox = textBox;
        }

        /// <summary>
        /// Queues colored text on the WinForms user-interface thread.
        /// </summary>
        public void Write(string text, ConsoleColor color, bool appendNewLine)
        {
            // BeginInvoke prevents the logging worker from waiting for the user-interface thread.
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
        /// Ignores title updates for embedded text controls.
        /// </summary>
        public void SetTitle(string text)
        {
            // Text controls do not own a window title.
        }

        /// <summary>
        /// Appends text after execution reaches the user-interface thread.
        /// </summary>
        private void Append(string text, ConsoleColor color)
        {
            // RichTextBox supports colors while basic text boxes receive plain text.
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

        /// <summary>
        /// Converts a console color to a WinForms color.
        /// </summary>
        private static Color FromColor(ConsoleColor color)
        {
            // Console color bits are converted without allocating lookup tables.
            int colorValue = (int)color;
            int brightnessCoefficient = (colorValue & 8) > 0 ? 2 : 1;
            int red = (colorValue & 4) > 0 ? 64 * brightnessCoefficient : 0;
            int green = (colorValue & 2) > 0 ? 64 * brightnessCoefficient : 0;
            int blue = (colorValue & 1) > 0 ? 64 * brightnessCoefficient : 0;
            return Color.FromArgb(red, green, blue);
        }
    }
}
