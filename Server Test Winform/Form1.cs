using NetSquare.Core;
using NetSquareServer;
using NetSquareServer.Utils;
using System;
using System.Windows.Forms;

#region Source
namespace Server_Test_Winform
{
    /// <summary>
    /// Represents the form1 component.
    /// </summary>
    public partial class Form1 : Form
    {
        NetSquare_Server server;

        /// <summary>
        /// Initializes a new instance of the form1 class.
        /// </summary>
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

        /// <summary>
        /// Executes the server on client connected operation.
        /// </summary>
        private void Server_OnClientConnected(uint clientID)
        {
            server.SendToClient(new NetworkMessage(0).Set("Hey new client " + clientID + ". Welcome to my NetSquare server"), clientID);
        }

        /// <summary>
        /// Executes the execute in main thread operation.
        /// </summary>
        public void ExecuteInMainThread(NetSquareAction action, NetworkMessage message)
        {
            Invoke(new MethodInvoker(() => action?.Invoke(message)));
        }

        /// <summary>
        /// Executes the btn start server click operation.
        /// </summary>
        private void btnStartServer_Click(object sender, EventArgs e)
        {
            
            Writer.Write("Start Server at " + DateTime.Now.ToString());
            server.Start((int)tbPort.Value);
            btnStartServer.Enabled = false;
            tbPort.Enabled = false;
        }

        [NetSquareAction(0)]
        /// <summary>
        /// Executes the client send text operation.
        /// </summary>
        public static void ClientSendText(NetworkMessage message)
        {
            MessageBox.Show(message.GetString());
        }

        /// <summary>
        /// Executes the client ping me operation.
        /// </summary>
        public void ClientPingMe(NetworkMessage message)
        {
            rtbOut.AppendText("PING !\n");
        }
    }
}
#endregion
