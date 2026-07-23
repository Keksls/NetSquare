using NetSquare.Server;

#region Source
namespace Server_Test
{
    /// <summary>
    /// Extends the NetSquare settings with configuration owned by the consuming server project.
    /// </summary>
    public sealed class ServerTestConfiguration : NetSquareConfiguration
    {
        public string ProjectName { get; set; }

        /// <summary>
        /// Initializes the consuming project's default configuration values.
        /// </summary>
        public ServerTestConfiguration()
        {
            // Keep project-owned defaults next to the project-owned configuration contract.
            ProjectName = "Server Test";
        }
    }
}
#endregion
