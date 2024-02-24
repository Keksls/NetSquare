using NetSquareServer.Server;
using NetSquareServer.Worlds;
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
        List<float> receptionsSizeValues;
        List<float> sendingSizeValues;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            receptionsSpeedValues = new List<int>();
            sendingSpeedValues = new List<int>();
            receptionsSizeValues = new List<float>();
            sendingSizeValues = new List<float>();
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

            // Update reception and sending size
            receptionsSizeValues.Add(statistics.Downloading);
            sendingSizeValues.Add(statistics.Uploading);
            if (receptionsSizeValues.Count > 60)
            {
                receptionsSizeValues.RemoveAt(0);
                sendingSizeValues.RemoveAt(0);
            }

            // Update UI
            chart2.Series[0].Points.Clear();
            chart2.Series[1].Points.Clear();
            for (int i = 0; i < receptionsSizeValues.Count; i++)
            {
                chart2.Series[0].Points.AddY(receptionsSizeValues[i]);
                chart2.Series[1].Points.AddY(sendingSizeValues[i]);
            }
        }

        public void UpdateWorldData(NetSquareWorld world)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<NetSquareWorld>(UpdateWorldData), world);
                return;
            }

            if (world != null)
            {
                if(world.UseSpatializer)
                {
                    lbWs.Text = "World " + world.ID + " uses spatializer.";
                    if(world.Spatializer.SynchFrequency > 0)
                    {
                        lbWs.Text += " SynchFrequency: " + world.Spatializer.SynchFrequency;
                    }
                }
                else
                {
                    lbWs.Text = "World " + world.ID + " does NOT use spatializer";
                }
            }
        }
    }
}
