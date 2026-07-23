namespace NetSquare.Core.Configuration
{
    /// <summary>
    /// Defines settings shared by NetSquare client and server configurations.
    /// </summary>
    public abstract class NetSquareConfiguration
    {
        /// <summary>
        /// Gets or sets whether the complete TCP connection uses TLS.
        /// </summary>
        public bool UseTLS { get; set; }
        /// <summary>
        /// Gets or sets whether UDP datagrams use sequence and MAC64 authentication.
        /// </summary>
        public bool UseUdpAuthentication { get; set; }

        /// <summary>
        /// Initializes the shared NetSquare configuration defaults.
        /// </summary>
        protected NetSquareConfiguration()
        {
            // TLS stays opt-in until both connection endpoints explicitly enable it.
            UseTLS = false;
            // UDP authentication stays opt-in independently from TCP TLS.
            UseUdpAuthentication = false;
        }
    }
}
