namespace NetSquare.Server
{
    /// <summary>
    /// Defines an external IP reputation provider evaluated outside the connection acceptance path.
    /// </summary>
    public interface IIPReputationProvider
    {
        /// <summary>
        /// Gets the provider name used in status and log messages.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Evaluates one canonical public IP address.
        /// </summary>
        /// <param name="ipAddress">Canonical public IP address.</param>
        /// <returns>The provider evaluation result.</returns>
        BlackListReputationResult Check(string ipAddress);
    }
}
