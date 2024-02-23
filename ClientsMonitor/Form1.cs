using NetSquareClient;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ClientsMonitor
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
            receptionsSpeedValues = new List<int>();
            sendingSpeedValues = new List<int>();
            receptionsSizeValues = new List<float>();
            sendingSizeValues = new List<float>();
        }

        private void chart2_Click(object sender, EventArgs e)
        {

        }

        private void chart1_Click(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        public void UpdateStatistics(ClientStatistics statistics)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<ClientStatistics>(UpdateStatistics), statistics);
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
    }
}