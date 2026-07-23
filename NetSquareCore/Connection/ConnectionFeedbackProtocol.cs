using NetSquare.Core.Messages;
using System.IO;
using System;
using System.Net.Sockets;
using System.Text;

namespace NetSquare.Core
{
    /// <summary>
    /// Serializes connection rejections during the handshake and typed disconnection notices after it.
    /// </summary>
    public static class ConnectionFeedbackProtocol
    {
        #region Protocol constants
        private static readonly byte[] ConnectionRejectionMagic = Encoding.ASCII.GetBytes("NSQREJ01");
        private const byte ProtocolVersion = 1;
        private const int RejectionHeaderLength = 22;
        private const int MaximumMessageByteLength = 16384;
        #endregion

        #region Connection rejection
        /// <summary>
        /// Returns whether the next bytes on a socket identify a connection rejection frame.
        /// </summary>
        /// <param name="socket">Socket being validated.</param>
        /// <returns>True when a complete rejection marker is pending.</returns>
        public static bool IsConnectionRejectionPending(Socket socket)
        {
            if (socket == null || socket.Available < ConnectionRejectionMagic.Length)
                return false;

            byte[] prefix = new byte[ConnectionRejectionMagic.Length];
            int received = socket.Receive(prefix, 0, prefix.Length, SocketFlags.Peek);
            if (received != prefix.Length)
                return false;

            return HasMagic(prefix);
        }

        /// <summary>
        /// Returns whether bytes already read from a stream identify a connection rejection frame.
        /// </summary>
        /// <param name="buffer">Frame prefix read from the transport.</param>
        /// <returns>True when the rejection marker matches.</returns>
        public static bool IsConnectionRejectionMarker(byte[] buffer)
        {
            return HasMagic(buffer);
        }

        /// <summary>
        /// Sends a typed connection rejection synchronously before the server closes the socket.
        /// </summary>
        /// <param name="socket">Socket to notify.</param>
        /// <param name="info">Rejection information.</param>
        public static void SendConnectionRejection(Socket socket, ConnectionRejectionInfo info)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            byte[] messageBytes = Encoding.UTF8.GetBytes(info.Message ?? string.Empty);
            if (messageBytes.Length > MaximumMessageByteLength)
                throw new ArgumentException("The connection rejection message is too long.", nameof(info));

            byte[] frame = new byte[RejectionHeaderLength + messageBytes.Length];
            Buffer.BlockCopy(ConnectionRejectionMagic, 0, frame, 0, ConnectionRejectionMagic.Length);
            frame[8] = ProtocolVersion;
            frame[9] = (byte)info.Reason;
            Buffer.BlockCopy(BitConverter.GetBytes(ToExpirationTicks(info.ExpiresUtc)), 0, frame, 10, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(messageBytes.Length), 0, frame, 18, 4);
            Buffer.BlockCopy(messageBytes, 0, frame, RejectionHeaderLength, messageBytes.Length);
            SendAll(socket, frame);
        }


        /// <summary>
        /// Sends a typed connection rejection through a stream transport such as TLS.
        /// </summary>
        /// <param name="stream">Writable transport stream.</param>
        /// <param name="info">Rejection information.</param>
        public static void SendConnectionRejection(Stream stream, ConnectionRejectionInfo info)
        {
            // Serialize the same rejection frame used by raw sockets before writing it through TLS.
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            byte[] messageBytes = Encoding.UTF8.GetBytes(info.Message ?? string.Empty);
            if (messageBytes.Length > MaximumMessageByteLength)
                throw new ArgumentException("The connection rejection message is too long.", nameof(info));

            byte[] frame = new byte[RejectionHeaderLength + messageBytes.Length];
            Buffer.BlockCopy(ConnectionRejectionMagic, 0, frame, 0, ConnectionRejectionMagic.Length);
            frame[8] = ProtocolVersion;
            frame[9] = (byte)info.Reason;
            Buffer.BlockCopy(BitConverter.GetBytes(ToExpirationTicks(info.ExpiresUtc)), 0, frame, 10, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(messageBytes.Length), 0, frame, 18, 4);
            Buffer.BlockCopy(messageBytes, 0, frame, RejectionHeaderLength, messageBytes.Length);
            stream.Write(frame, 0, frame.Length);
            stream.Flush();
        }
        /// <summary>
        /// Receives a complete connection rejection frame from the server.
        /// </summary>
        /// <param name="socket">Socket being validated.</param>
        /// <returns>The decoded rejection information.</returns>
        public static ConnectionRejectionInfo ReceiveConnectionRejection(Socket socket)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));

            byte[] header = ReceiveExact(socket, RejectionHeaderLength);
            if (!HasMagic(header))
                throw new InvalidOperationException("The received frame is not a NetSquare connection rejection.");
            if (header[8] != ProtocolVersion)
                throw new InvalidOperationException("Unsupported NetSquare connection feedback protocol version.");

            ConnectionRejectionReason reason = Enum.IsDefined(typeof(ConnectionRejectionReason), header[9])
                ? (ConnectionRejectionReason)header[9]
                : ConnectionRejectionReason.Unknown;
            DateTime? expiresUtc = FromExpirationTicks(BitConverter.ToInt64(header, 10));
            int messageLength = BitConverter.ToInt32(header, 18);
            if (messageLength < 0 || messageLength > MaximumMessageByteLength)
                throw new InvalidOperationException("Invalid NetSquare connection rejection message length.");

            string message = messageLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(ReceiveExact(socket, messageLength));
            return new ConnectionRejectionInfo(reason, message, expiresUtc);
        }

        /// <summary>
        /// Receives a connection rejection after its marker was already read from a stream.
        /// </summary>
        /// <param name="stream">Readable transport stream.</param>
        /// <param name="prefix">Previously read rejection marker.</param>
        /// <returns>The decoded rejection information.</returns>
        public static ConnectionRejectionInfo ReceiveConnectionRejection(Stream stream, byte[] prefix)
        {
            // Continue from the consumed marker because SslStream does not support socket-style peeking.
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (prefix == null || prefix.Length != ConnectionRejectionMagic.Length || !HasMagic(prefix))
            {
                throw new InvalidOperationException(
                    "The received frame is not a NetSquare connection rejection.");
            }

            byte[] header = new byte[RejectionHeaderLength];
            Buffer.BlockCopy(prefix, 0, header, 0, prefix.Length);
            byte[] remainingHeader = ReceiveExact(stream, RejectionHeaderLength - prefix.Length);
            Buffer.BlockCopy(remainingHeader, 0, header, prefix.Length, remainingHeader.Length);
            if (header[8] != ProtocolVersion)
                throw new InvalidOperationException("Unsupported NetSquare connection feedback protocol version.");

            ConnectionRejectionReason reason = Enum.IsDefined(typeof(ConnectionRejectionReason), header[9])
                ? (ConnectionRejectionReason)header[9]
                : ConnectionRejectionReason.Unknown;
            DateTime? expiresUtc = FromExpirationTicks(BitConverter.ToInt64(header, 10));
            int messageLength = BitConverter.ToInt32(header, 18);
            if (messageLength < 0 || messageLength > MaximumMessageByteLength)
                throw new InvalidOperationException("Invalid NetSquare connection rejection message length.");

            string message = messageLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(ReceiveExact(stream, messageLength));
            return new ConnectionRejectionInfo(reason, message, expiresUtc);
        }
        #endregion

        #region Established connection
        /// <summary>
        /// Creates the internal NetSquare message sent before closing an established connection.
        /// </summary>
        /// <param name="info">Disconnection information.</param>
        /// <param name="clientID">Client ID written into the message header.</param>
        /// <returns>The typed disconnection notice.</returns>
        public static NetworkMessage CreateDisconnectMessage(DisconnectInfo info, uint clientID = 0)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            return new NetworkMessage(NetSquareMessageID.Disconnecting, clientID)
                .Set(ProtocolVersion)
                .Set((byte)info.Reason)
                .Set(ToExpirationTicks(info.ExpiresUtc))
                .Set(info.Message ?? string.Empty);
        }

        /// <summary>
        /// Decodes typed disconnection information from an internal NetSquare message.
        /// </summary>
        /// <param name="message">Disconnection message received from the remote peer.</param>
        /// <param name="fallbackReason">Reason used when the payload is absent or invalid.</param>
        /// <returns>The decoded disconnection information.</returns>
        public static DisconnectInfo ReadDisconnectInfo(NetworkMessage message, DisconnectReason fallbackReason = DisconnectReason.Unknown)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            try
            {
                if (!message.Serializer.CanGetByte() || message.Serializer.GetByte() != ProtocolVersion)
                    return new DisconnectInfo(fallbackReason);
                if (!message.Serializer.CanGetByte())
                    return new DisconnectInfo(fallbackReason);

                byte rawReason = message.Serializer.GetByte();
                DisconnectReason reason = Enum.IsDefined(typeof(DisconnectReason), rawReason)
                    ? (DisconnectReason)rawReason
                    : fallbackReason;
                if (!message.Serializer.CanGetLong())
                    return new DisconnectInfo(reason);

                DateTime? expiresUtc = FromExpirationTicks(message.Serializer.GetLong());
                string details = message.Serializer.CanGetString()
                    ? message.Serializer.GetString()
                    : string.Empty;
                return new DisconnectInfo(reason, details, expiresUtc);
            }
            catch
            {
                return new DisconnectInfo(fallbackReason);
            }
        }
        #endregion

        #region Transport helpers
        /// <summary>
        /// Returns whether a byte buffer starts with the connection rejection marker.
        /// </summary>
        /// <param name="buffer">Buffer to inspect.</param>
        /// <returns>True when the marker matches.</returns>
        private static bool HasMagic(byte[] buffer)
        {
            if (buffer == null || buffer.Length < ConnectionRejectionMagic.Length)
                return false;

            for (int index = 0; index < ConnectionRejectionMagic.Length; index++)
            {
                if (buffer[index] != ConnectionRejectionMagic[index])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Sends every byte in a buffer through a blocking socket.
        /// </summary>
        /// <param name="socket">Destination socket.</param>
        /// <param name="buffer">Bytes to send.</param>
        private static void SendAll(Socket socket, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int sent = socket.Send(buffer, offset, buffer.Length - offset, SocketFlags.None);
                if (sent <= 0)
                    throw new SocketException((int)SocketError.ConnectionReset);
                offset += sent;
            }
        }

        /// <summary>
        /// Receives an exact number of bytes from a blocking socket.
        /// </summary>
        /// <param name="socket">Source socket.</param>
        /// <param name="length">Required byte count.</param>
        /// <returns>The filled buffer.</returns>
        private static byte[] ReceiveExact(Socket socket, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int received = socket.Receive(buffer, offset, length - offset, SocketFlags.None);
                if (received <= 0)
                    throw new SocketException((int)SocketError.ConnectionReset);
                offset += received;
            }
            return buffer;
        }

        /// <summary>
        /// Receives an exact number of bytes from a blocking stream.
        /// </summary>
        /// <param name="stream">Source stream.</param>
        /// <param name="length">Required byte count.</param>
        /// <returns>The filled buffer.</returns>
        private static byte[] ReceiveExact(Stream stream, int length)
        {
            // Rejection frames are small and the enclosing connection timeout closes stalled transports.
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int received = stream.Read(buffer, offset, length - offset);
                if (received <= 0)
                    throw new IOException("The remote peer closed the NetSquare rejection stream.");
                offset += received;
            }
            return buffer;
        }

        /// <summary>
        /// Converts an optional UTC timestamp into its wire representation.
        /// </summary>
        /// <param name="expiresUtc">Optional expiration.</param>
        /// <returns>UTC ticks, or zero when absent.</returns>
        private static long ToExpirationTicks(DateTime? expiresUtc)
        {
            if (!expiresUtc.HasValue)
                return 0;

            DateTime value = expiresUtc.Value.Kind == DateTimeKind.Utc
                ? expiresUtc.Value
                : expiresUtc.Value.ToUniversalTime();
            return value.Ticks;
        }

        /// <summary>
        /// Converts wire ticks into an optional UTC timestamp.
        /// </summary>
        /// <param name="ticks">UTC ticks, or zero when absent.</param>
        /// <returns>The decoded UTC expiration.</returns>
        private static DateTime? FromExpirationTicks(long ticks)
        {
            if (ticks == 0)
                return null;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                throw new InvalidOperationException("Invalid NetSquare connection feedback expiration timestamp.");

            return new DateTime(ticks, DateTimeKind.Utc);
        }
        #endregion
    }
}
