using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NetSquare.Core
{
    /// <summary>
    /// Authenticates UDP datagrams with directional HMAC-SHA256 keys and a 64-bit tag.
    /// </summary>
    internal sealed class UdpDatagramAuthenticator : IDisposable
    {
        public const int SequenceSize = 4;
        public const int TagSize = 8;
        public const int Overhead = SequenceSize + TagSize;

        private static readonly byte[] ClientToServerLabel =
            Encoding.ASCII.GetBytes("NetSquare UDP MAC client-to-server v1");
        private static readonly byte[] ServerToClientLabel =
            Encoding.ASCII.GetBytes("NetSquare UDP MAC server-to-client v1");

        private readonly object outgoingMacLock = new object();
        private readonly object incomingMacLock = new object();
        private readonly object replayWindowLock = new object();
        private readonly HMACSHA256 outgoingMac;
        private readonly HMACSHA256 incomingMac;
        private int outgoingSequence;
        private uint highestIncomingSequence;
        private bool disposed;

        /// <summary>
        /// Initializes directional MAC keys from one handshake session secret.
        /// </summary>
        /// <param name="sessionKey">Handshake session secret.</param>
        /// <param name="isServer">Whether this authenticator belongs to the server side.</param>
        public UdpDatagramAuthenticator(byte[] sessionKey, bool isServer)
        {
            if (sessionKey == null)
                throw new ArgumentNullException(nameof(sessionKey));
            if (sessionKey.Length < 16)
                throw new ArgumentException("The UDP session key must contain at least 16 bytes.", nameof(sessionKey));

            // Derive independent keys so a datagram from one direction cannot be reflected into the other.
            byte[] clientToServerKey = DeriveKey(sessionKey, ClientToServerLabel);
            byte[] serverToClientKey = DeriveKey(sessionKey, ServerToClientLabel);
            try
            {
                outgoingMac = new HMACSHA256(isServer ? serverToClientKey : clientToServerKey);
                incomingMac = new HMACSHA256(isServer ? clientToServerKey : serverToClientKey);
            }
            finally
            {
                ClearBytes(clientToServerKey);
                ClearBytes(serverToClientKey);
            }
        }

        /// <summary>
        /// Appends a datagram sequence and MAC64 tag to one serialized NetworkMessage.
        /// </summary>
        /// <param name="payload">Serialized NetworkMessage bytes.</param>
        /// <returns>The authenticated UDP datagram.</returns>
        public byte[] Protect(byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            ThrowIfDisposed();

            // The original message remains at offset zero so receivers can validate its declared length without copying.
            byte[] datagram = new byte[checked(payload.Length + Overhead)];
            Buffer.BlockCopy(payload, 0, datagram, 0, payload.Length);
            uint sequence = GetNextOutgoingSequence();
            WriteUInt32(datagram, payload.Length, sequence);
            ulong tag = ComputeTag(outgoingMac, outgoingMacLock, datagram, 0, datagram.Length - TagSize);
            WriteUInt64(datagram, datagram.Length - TagSize, tag);
            return datagram;
        }

        /// <summary>
        /// Authenticates one datagram and commits its sequence to the replay window.
        /// </summary>
        /// <param name="datagram">Received UDP bytes.</param>
        /// <param name="minimumPayloadLength">Minimum valid NetworkMessage length.</param>
        /// <param name="payloadLength">Authenticated NetworkMessage length.</param>
        /// <returns>True when the tag, length and sequence are valid.</returns>
        public bool TryAuthenticate(byte[] datagram, int minimumPayloadLength, out int payloadLength)
        {
            payloadLength = 0;
            if (disposed ||
                !TryGetPayloadLength(datagram, minimumPayloadLength, true, out payloadLength))
                return false;

            uint sequence = ReadUInt32(datagram, payloadLength);
            if (sequence == 0)
                return false;

            ulong receivedTag = ReadUInt64(datagram, datagram.Length - TagSize);
            ulong expectedTag = ComputeTag(
                incomingMac,
                incomingMacLock,
                datagram,
                0,
                datagram.Length - TagSize);
            if ((receivedTag ^ expectedTag) != 0)
                return false;

            // Update replay state only after authentication so forged packets cannot advance the window.
            return TryAcceptIncomingSequence(sequence);
        }

        /// <summary>
        /// Reads the unauthenticated ClientID lookup hint without parsing the message body.
        /// </summary>
        /// <param name="datagram">Received UDP bytes.</param>
        /// <param name="minimumPayloadLength">Minimum valid NetworkMessage length.</param>
        /// <param name="clientID">Client ID lookup hint.</param>
        /// <returns>True when the envelope and ClientID field are present.</returns>
        public static bool TryReadClientID(
            byte[] datagram,
            int minimumPayloadLength,
            out uint clientID)
        {
            clientID = 0;
            int payloadLength;
            if ((!TryGetPayloadLength(datagram, minimumPayloadLength, true, out payloadLength) &&
                 !TryGetPayloadLength(datagram, minimumPayloadLength, false, out payloadLength)) ||
                payloadLength < 8)
                return false;

            // This value only selects the TCP-associated connection; the server overwrites it after validation.
            clientID = ReadUInt32(datagram, 4);
            return clientID != 0;
        }

        /// <summary>
        /// Releases directional HMAC instances.
        /// </summary>
        public void Dispose()
        {
            lock (outgoingMacLock)
            {
                lock (incomingMacLock)
                {
                    if (disposed)
                        return;

                    disposed = true;
                    outgoingMac.Dispose();
                    incomingMac.Dispose();
                }
            }
        }

        /// <summary>
        /// Validates the NetworkMessage length prefix against the configured UDP envelope.
        /// </summary>
        /// <param name="datagram">Received UDP bytes.</param>
        /// <param name="minimumPayloadLength">Minimum valid NetworkMessage length.</param>
        /// <param name="hasAuthenticationTrailer">Whether sequence and MAC64 bytes must be present.</param>
        /// <param name="payloadLength">Validated NetworkMessage length.</param>
        /// <returns>True when the complete datagram has the expected envelope length.</returns>
        internal static bool TryGetPayloadLength(
            byte[] datagram,
            int minimumPayloadLength,
            bool hasAuthenticationTrailer,
            out int payloadLength)
        {
            payloadLength = 0;
            int trailerLength = hasAuthenticationTrailer ? Overhead : 0;
            if (datagram == null ||
                minimumPayloadLength < 8 ||
                datagram.Length < minimumPayloadLength + trailerLength)
                return false;

            uint declaredLength = ReadUInt32(datagram, 0);
            if (declaredLength > int.MaxValue)
                return false;

            payloadLength = (int)declaredLength;
            return payloadLength >= minimumPayloadLength &&
                payloadLength == datagram.Length - trailerLength;
        }

        /// <summary>
        /// Returns the next non-zero outgoing datagram sequence.
        /// </summary>
        private uint GetNextOutgoingSequence()
        {
            // Interlocked keeps sequences unique when multiple application threads send concurrently.
            uint sequence;
            do
            {
                sequence = unchecked((uint)Interlocked.Increment(ref outgoingSequence));
            }
            while (sequence == 0);
            return sequence;
        }

        /// <summary>
        /// Accepts only a sequence strictly newer than the latest authenticated datagram.
        /// </summary>
        private bool TryAcceptIncomingSequence(uint sequence)
        {
            lock (replayWindowLock)
            {
                if (highestIncomingSequence == 0)
                {
                    highestIncomingSequence = sequence;
                    return true;
                }

                // Signed modular distance preserves ordering across the uint wrap boundary.
                int distance = unchecked((int)(sequence - highestIncomingSequence));
                if (distance <= 0)
                    return false;

                highestIncomingSequence = sequence;
                return true;
            }
        }

        /// <summary>
        /// Derives one directional HMAC key from the handshake secret.
        /// </summary>
        private static byte[] DeriveKey(byte[] sessionKey, byte[] label)
        {
            // HMAC-based labeled derivation isolates each transport direction.
            using (HMACSHA256 derivationMac = new HMACSHA256(sessionKey))
                return derivationMac.ComputeHash(label);
        }

        /// <summary>
        /// Computes the first 64 bits of HMAC-SHA256 over one datagram prefix.
        /// </summary>
        private static ulong ComputeTag(
            HMACSHA256 mac,
            object syncRoot,
            byte[] buffer,
            int offset,
            int count)
        {
            lock (syncRoot)
            {
#if NET8_0_OR_GREATER
                Span<byte> hash = stackalloc byte[32];
                int bytesWritten;
                if (!mac.TryComputeHash(
                    new ReadOnlySpan<byte>(buffer, offset, count),
                    hash,
                    out bytesWritten) ||
                    bytesWritten < TagSize)
                    throw new CryptographicException("Unable to compute the UDP authentication tag.");
                return ReadUInt64(hash);
#else
                byte[] hash = mac.ComputeHash(buffer, offset, count);
                try
                {
                    return ReadUInt64(hash, 0);
                }
                finally
                {
                    ClearBytes(hash);
                }
#endif
            }
        }

        /// <summary>
        /// Throws when this authenticator was already disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(UdpDatagramAuthenticator));
        }

        /// <summary>
        /// Clears secret bytes without data-dependent early exits.
        /// </summary>
        private static void ClearBytes(byte[] bytes)
        {
            if (bytes == null)
                return;
            for (int index = 0; index < bytes.Length; index++)
                bytes[index] = 0;
        }

        /// <summary>
        /// Reads an unsigned 32-bit integer encoded in little-endian order.
        /// </summary>
        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] |
                (buffer[offset + 1] << 8) |
                (buffer[offset + 2] << 16) |
                (buffer[offset + 3] << 24));
        }

        /// <summary>
        /// Reads an unsigned 64-bit integer encoded in little-endian order.
        /// </summary>
        private static ulong ReadUInt64(byte[] buffer, int offset)
        {
            return buffer[offset] |
                ((ulong)buffer[offset + 1] << 8) |
                ((ulong)buffer[offset + 2] << 16) |
                ((ulong)buffer[offset + 3] << 24) |
                ((ulong)buffer[offset + 4] << 32) |
                ((ulong)buffer[offset + 5] << 40) |
                ((ulong)buffer[offset + 6] << 48) |
                ((ulong)buffer[offset + 7] << 56);
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// Reads an unsigned 64-bit integer encoded in little-endian order.
        /// </summary>
        private static ulong ReadUInt64(ReadOnlySpan<byte> buffer)
        {
            return buffer[0] |
                ((ulong)buffer[1] << 8) |
                ((ulong)buffer[2] << 16) |
                ((ulong)buffer[3] << 24) |
                ((ulong)buffer[4] << 32) |
                ((ulong)buffer[5] << 40) |
                ((ulong)buffer[6] << 48) |
                ((ulong)buffer[7] << 56);
        }
#endif

        /// <summary>
        /// Writes an unsigned 32-bit integer in little-endian order.
        /// </summary>
        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        /// <summary>
        /// Writes an unsigned 64-bit integer in little-endian order.
        /// </summary>
        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
            buffer[offset + 4] = (byte)(value >> 32);
            buffer[offset + 5] = (byte)(value >> 40);
            buffer[offset + 6] = (byte)(value >> 48);
            buffer[offset + 7] = (byte)(value >> 56);
        }
    }
}
