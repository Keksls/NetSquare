using NetSquareServer.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServerMonitor
{
    public partial class Form1 : Form
    {
        List<int> receptionsSpeedValues;
        List<int> sendingSpeedValues;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            receptionsSpeedValues = new List<int>();
            sendingSpeedValues = new List<int>();
        }

        public void Write(string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(Write), text);
                return;
            }
            richTextBox1.AppendText(text + Environment.NewLine);
        }

        public void Clear()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(Clear));
                return;
            }
            richTextBox1.Clear();
        }

        public void UpdateStatistics(ServerStatistics statistics)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<ServerStatistics>(UpdateStatistics), statistics);
                return;
            }

            // Update reception and sending speed
            receptionsSpeedValues.Add(statistics.NbMessagesReceiving);
            sendingSpeedValues.Add(statistics.NbMessagesSending);
            if (receptionsSpeedValues.Count > 60)
            {
                receptionsSpeedValues.RemoveAt(0);
                sendingSpeedValues.RemoveAt(0);
            }

            // Update UI
            chart1.Series[0].Points.Clear();
            chart1.Series[1].Points.Clear();
            for (int i = 0; i < receptionsSpeedValues.Count; i++)
            {
                chart1.Series[0].Points.AddY(receptionsSpeedValues[i]);
                chart1.Series[1].Points.AddY(sendingSpeedValues[i]);
            }
        }
    }
}
