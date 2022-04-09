using NetSquare.Core;
using NetSquareServer;
using NetSquareServer.Utils;
using System;
using System.Windows.Forms;

namespace Server_Test_Winform
{
    public partial class Form1 : Form
    {
        NetSquare_Server server;

        public Form1()
        {
            InitializeComponent();
            // Init Writer
            Writer.SetOutputAsRichTextBox(rtbOut);
            Writer.StartRecordingLog();
            // Init Server
            server = new NetSquare_Server();
            server.OnClientConnected += Server_OnClientConnected;
            server.Dispatcher.SetMainThreadCallback(ExecuteInMainThread);
            server.Dispatcher.AddHeadAction(1, "Ping", ClientPingMe);
        }

        private void Server_OnClientConnected(uint clientID)
        {
            server.SendToClient(new NetworkMessage(0).Set("Hey new client " + clientID + ". Welcome to my NetSquare server"), clientID);
        }

        public void ExecuteInMainThread(NetSquareAction action, NetworkMessage message)
        {
            Invoke(new MethodInvoker(() => action?.Invoke(message)));
        }

        private void btnStartServer_Click(object sender, EventArgs e)
        {
            Writer.Write("Start Server at " + DateTime.Now.ToString());
            server.Start((int)tbPort.Value);
            btnStartServer.Enabled = false;
            tbPort.Enabled = false;
        }

        [NetSquareAction(0)]
        public static void ClientSendText(NetworkMessage message)
        {
            string text = "";
            message.Get(ref text);
            MessageBox.Show(text);
        }

        public void ClientPingMe(NetworkMessage message)
        {
            rtbOut.AppendText("PING !\n");
        }
    }
}
