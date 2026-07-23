using System;
using NetSquare.Core;
using NetSquare.Core.Configuration;

namespace NetSquare.Client
{
    /// <summary>
    /// Defines the JSON-configurable connection and runtime settings of a NetSquare client.
    /// </summary>
    public class NetSquareClientConfiguration : NetSquareConfiguration
    {
        #region Connection
        /// <summary>
        /// Gets or sets the server host name or IP address.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// Gets or sets the server port.
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Gets or sets the requested NetSquare transport.
        /// </summary>
        public NetSquareProtocoleType ProtocoleType { get; set; }

        /// <summary>
        /// Gets or sets the connection timeout in milliseconds.
        /// </summary>
        public int ConnectionTimeoutMilliseconds { get; set; }
        /// <summary>
        /// Gets or sets the maximum number of received messages waiting for dispatch.
        /// </summary>
        public int MaxQueuedInboundMessages { get; set; }

        /// <summary>
        /// Gets or sets the graceful message worker shutdown timeout in milliseconds.
        /// </summary>
        public int MessageWorkerStopTimeoutMilliseconds { get; set; }

        #endregion

        #region TLS
        /// <summary>
        /// Gets or sets the optional DNS name validated against the server certificate.
        /// </summary>
        public string TLSServerName { get; set; }
        #endregion

        #region Heartbeat
        /// <summary>
        /// Gets or sets whether the TCP heartbeat is enabled.
        /// </summary>
        public bool HeartbeatEnabled { get; set; }

        /// <summary>
        /// Gets or sets the heartbeat interval in milliseconds.
        /// </summary>
        public int HeartbeatIntervalMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the heartbeat reply timeout in milliseconds.
        /// </summary>
        public int HeartbeatTimeoutMilliseconds { get; set; }
        #endregion

        #region Time synchronization
        /// <summary>
        /// Gets or sets whether server time offset changes are smoothed.
        /// </summary>
        public bool SmoothServerTimeOffset { get; set; }

        /// <summary>
        /// Gets or sets the server time offset smoothing speed.
        /// </summary>
        public float ServerTimeOffsetSmoothingSpeed { get; set; }

        /// <summary>
        /// Gets or sets the timeout of one time synchronization request.
        /// </summary>
        public int TimeSynchronizationRequestTimeoutMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the maximum attempts of one synchronization, or zero for automatic selection.
        /// </summary>
        public int TimeSynchronizationMaxAttempts { get; set; }
        #endregion

        #region World synchronization
        /// <summary>
        /// Gets or sets the transport used for world synchronization frames.
        /// </summary>
        public NetSquareSyncTransport SynchronizationTransport { get; set; }

        /// <summary>
        /// Gets or sets the maximum queued synchronization frames before older frames are dropped.
        /// </summary>
        public int MaxStoredSynchronizationFrames { get; set; }

        /// <summary>
        /// Gets or sets whether world synchronization frames are sent automatically.
        /// </summary>
        public bool AutoSendSynchronizationFrames { get; set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a client configuration with the existing NetSquare client defaults.
        /// </summary>
        public NetSquareClientConfiguration()
        {
            MaxQueuedInboundMessages = 8192;
            MessageWorkerStopTimeoutMilliseconds = 5000;
            // Keep JSON-created clients behaviorally aligned with manually constructed clients.
            Host = "127.0.0.1";
            Port = 5555;
            ProtocoleType = NetSquareProtocoleType.TCP_AND_UDP;
            ConnectionTimeoutMilliseconds = 30000;
            TLSServerName = string.Empty;
            HeartbeatEnabled = true;
            HeartbeatIntervalMilliseconds = 10000;
            HeartbeatTimeoutMilliseconds = 30000;
            SmoothServerTimeOffset = true;
            ServerTimeOffsetSmoothingSpeed = 8f;
            TimeSynchronizationRequestTimeoutMilliseconds = 1500;
            TimeSynchronizationMaxAttempts = 0;
            SynchronizationTransport = NetSquareSyncTransport.UnreliableUdp;
            MaxStoredSynchronizationFrames = 256;
            AutoSendSynchronizationFrames = true;
        }
        #endregion

        #region Validation
        /// <summary>
        /// Validates values that would otherwise fail later during connection or synchronization.
        /// </summary>
        public void Validate()
        {
            // Reject invalid deployment settings before starting network activity.
            if (string.IsNullOrWhiteSpace(Host))
                throw new InvalidOperationException("The NetSquare client Host setting is required.");
            if (Port < 1 || Port > 65535)
                throw new InvalidOperationException("The NetSquare client Port setting must be between 1 and 65535.");
            if (!Enum.IsDefined(typeof(NetSquareProtocoleType), ProtocoleType))
                throw new InvalidOperationException("The NetSquare client ProtocoleType setting is invalid.");
            if (!Enum.IsDefined(typeof(NetSquareSyncTransport), SynchronizationTransport))
                throw new InvalidOperationException("The NetSquare client SynchronizationTransport setting is invalid.");
            if (SynchronizationTransport == NetSquareSyncTransport.UnreliableUdp &&
                ProtocoleType != NetSquareProtocoleType.TCP_AND_UDP)
            {
                throw new InvalidOperationException(
                    "Unreliable UDP synchronization requires the TCP_AND_UDP protocol.");
            }

            if (ConnectionTimeoutMilliseconds <= 0)
                throw new InvalidOperationException("ConnectionTimeoutMilliseconds must be greater than zero.");
            if (HeartbeatEnabled &&
                (HeartbeatIntervalMilliseconds <= 0 || HeartbeatTimeoutMilliseconds <= 0))
            {
                throw new InvalidOperationException(
                    "Enabled heartbeat intervals and timeouts must be greater than zero.");
            }
            if (MaxQueuedInboundMessages <= 0)
                throw new InvalidOperationException("MaxQueuedInboundMessages must be greater than zero.");
            if (MessageWorkerStopTimeoutMilliseconds <= 0)
            {
                throw new InvalidOperationException(
                    "MessageWorkerStopTimeoutMilliseconds must be greater than zero.");
            }

            if (ServerTimeOffsetSmoothingSpeed < 0)
                throw new InvalidOperationException("ServerTimeOffsetSmoothingSpeed cannot be negative.");
            if (TimeSynchronizationRequestTimeoutMilliseconds <= 0)
            {
                throw new InvalidOperationException(
                    "TimeSynchronizationRequestTimeoutMilliseconds must be greater than zero.");
            }
            if (TimeSynchronizationMaxAttempts < 0)
                throw new InvalidOperationException("TimeSynchronizationMaxAttempts cannot be negative.");
            if (MaxStoredSynchronizationFrames <= 0)
                throw new InvalidOperationException("MaxStoredSynchronizationFrames must be greater than zero.");
        }
        #endregion
    }
}
