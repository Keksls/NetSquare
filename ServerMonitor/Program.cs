using System;
using System.Windows;

#region Source
namespace ServerMonitor
{
    /// <summary>
    /// Represents the program component.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        /// <summary>
        /// Executes the main operation.
        /// </summary>
        private static void Main()
        {
            Application application = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            application.Run(new Form1());
        }
    }
}
#endregion
