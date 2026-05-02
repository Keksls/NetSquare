using System;
using System.Windows.Forms;

#region Source
namespace ClientsMonitor
{
    /// <summary>
    /// Represents the program component.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Point d'entrée principal de l'application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
#endregion
