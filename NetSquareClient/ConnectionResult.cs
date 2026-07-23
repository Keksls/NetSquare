using NetSquare.Core;
using System;

namespace NetSquare.Client
{
    /// <summary>
    /// Represents the typed outcome of one client connection attempt.
    /// </summary>
    public sealed class ConnectionResult
    {
        public ConnectionResultStatus Status { get; private set; }
        public uint ClientID { get; private set; }
        public ConnectionRejectionInfo RejectionInfo { get; private set; }
        public Exception Exception { get; private set; }
        public bool IsConnected { get { return Status == ConnectionResultStatus.Connected || Status == ConnectionResultStatus.AlreadyConnected; } }
        public bool IsRejected { get { return Status == ConnectionResultStatus.Rejected; } }
        public bool IsCancelled { get { return Status == ConnectionResultStatus.Cancelled; } }

        /// <summary>
        /// Creates a typed connection result.
        /// </summary>
        /// <param name="status">Final connection status.</param>
        /// <param name="clientID">Assigned client ID when connected.</param>
        /// <param name="rejectionInfo">Server rejection information when refused.</param>
        /// <param name="exception">Transport or timeout exception when the attempt failed.</param>
        private ConnectionResult(
            ConnectionResultStatus status,
            uint clientID = 0,
            ConnectionRejectionInfo rejectionInfo = null,
            Exception exception = null)
        {
            Status = status;
            ClientID = clientID;
            RejectionInfo = rejectionInfo;
            Exception = exception;
        }

        /// <summary>
        /// Creates a successful connection result.
        /// </summary>
        /// <param name="clientID">Assigned client ID.</param>
        /// <returns>The successful result.</returns>
        internal static ConnectionResult Connected(uint clientID)
        {
            return new ConnectionResult(ConnectionResultStatus.Connected, clientID);
        }

        /// <summary>
        /// Creates a server-rejected connection result.
        /// </summary>
        /// <param name="info">Typed server rejection.</param>
        /// <returns>The rejected result.</returns>
        internal static ConnectionResult Rejected(ConnectionRejectionInfo info)
        {
            return new ConnectionResult(ConnectionResultStatus.Rejected, rejectionInfo: info);
        }

        /// <summary>
        /// Creates a transport failure result.
        /// </summary>
        /// <param name="exception">Transport exception.</param>
        /// <returns>The failed result.</returns>
        internal static ConnectionResult TransportError(Exception exception)
        {
            return new ConnectionResult(ConnectionResultStatus.TransportError, exception: exception);
        }

        /// <summary>
        /// Creates a timeout result.
        /// </summary>
        /// <param name="timeoutMilliseconds">Configured timeout in milliseconds.</param>
        /// <returns>The timeout result.</returns>
        internal static ConnectionResult TimedOut(int timeoutMilliseconds)
        {
            return new ConnectionResult(
                ConnectionResultStatus.TimedOut,
                exception: new TimeoutException("The NetSquare connection attempt timed out after " + timeoutMilliseconds + " ms."));
        }

        /// <summary>
        /// Creates a cancelled result.
        /// </summary>
        /// <returns>The cancelled result.</returns>
        internal static ConnectionResult Cancelled()
        {
            return new ConnectionResult(ConnectionResultStatus.Cancelled);
        }

        /// <summary>
        /// Creates a result indicating that the client is already connected.
        /// </summary>
        /// <param name="clientID">Current client ID.</param>
        /// <returns>The already-connected result.</returns>
        internal static ConnectionResult AlreadyConnected(uint clientID)
        {
            return new ConnectionResult(ConnectionResultStatus.AlreadyConnected, clientID);
        }

        /// <summary>
        /// Creates a result indicating that another connection attempt is active.
        /// </summary>
        /// <returns>The in-progress result.</returns>
        internal static ConnectionResult ConnectionInProgress()
        {
            return new ConnectionResult(ConnectionResultStatus.ConnectionInProgress);
        }
    }
}
