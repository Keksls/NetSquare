using NetSquare.Server.Server;
using NetSquare.Server.Worlds;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ServerMonitor
{
    public partial class Form1 : Form
    {
        List<int> receptionsSpeedValues;
        List<int> sendingSpeedValues;
        List<float> receptionsSizeValues;
        List<float> sendingSizeValues;
        private int maxLenght = 60;
        private int eachTimeInvoke = 1;
        private int invokeIndex = 0;

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

        public void Initialize(int maxlenght, int eachtimeinvoke)
        {
            maxLenght = maxlenght;
            eachTimeInvoke = eachtimeinvoke;
        }

        public void Write(string text)
        {
            if (invokeIndex < eachTimeInvoke)
                return;
            if (InvokeRequired)
            {
                Invoke(new Action<string>((txt) => { richTextBox1.AppendText(txt + Environment.NewLine); }), text);
                return;
            }
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
            if (receptionsSpeedValues == null || sendingSpeedValues == null || receptionsSizeValues == null || sendingSizeValues == null)
            {
                return;
            }

            // Update reception and sending speed
            receptionsSpeedValues.Add(statistics.NbMessagesReceiving);
            sendingSpeedValues.Add(statistics.NbMessagesSending);
            if (receptionsSpeedValues.Count > maxLenght)
            {
                receptionsSpeedValues.RemoveAt(0);
                sendingSpeedValues.RemoveAt(0);
            }

            // Update reception and sending size
            receptionsSizeValues.Add(statistics.Downloading);
            sendingSizeValues.Add(statistics.Uploading);
            if (receptionsSizeValues.Count > maxLenght)
            {
                receptionsSizeValues.RemoveAt(0);
                sendingSizeValues.RemoveAt(0);
            }

            if (invokeIndex < eachTimeInvoke)
            {
                invokeIndex++;
                return;
            }
            else
            {
                invokeIndex = 0;
            }

            if (InvokeRequired)
            {
                Invoke(new Action(() =>
                {
                    // Update UI
                    chart1.Series[0].Points.Clear();
                    chart1.Series[1].Points.Clear();
                    lock (receptionsSpeedValues)
                        for (int i = 0; i < receptionsSpeedValues.Count; i++)
                        {
                            chart1.Series[0].Points.AddY(receptionsSpeedValues[i]);
                        }
                    lock (sendingSpeedValues)
                        for (int i = 0; i < sendingSpeedValues.Count; i++)
                        {
                            chart1.Series[1].Points.AddY(sendingSpeedValues[i]);
                        }

                    // Update UI
                    chart2.Series[0].Points.Clear();
                    chart2.Series[1].Points.Clear();
                    lock (receptionsSizeValues)
                        for (int i = 0; i < receptionsSizeValues.Count; i++)
                        {
                            chart2.Series[0].Points.AddY(receptionsSizeValues[i]);
                        }
                    lock (sendingSizeValues)
                        for (int i = 0; i < sendingSizeValues.Count; i++)
                        {
                            chart2.Series[1].Points.AddY(sendingSizeValues[i]);
                        }
                }));
                return;
            }
        }

        public void UpdateWorldData(NetSquareWorld world)
        {
            if (invokeIndex < eachTimeInvoke)
                return;

            if (InvokeRequired)
            {
                Invoke(new Action<NetSquareWorld>(UpdateWorldData), world);
                return;
            }

            if (world != null)
            {
                if (world.UseSpatializer)
                {
                    lbWs.Text = "World " + world.ID + " uses spatializer.";
                    if (world.Spatializer.SynchFrequency > 0)
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
