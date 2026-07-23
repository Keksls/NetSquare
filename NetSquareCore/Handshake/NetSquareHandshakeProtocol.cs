using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NetSquare.Core
{
    /// <summary>
    /// Serializes and validates the strict NetSquare handshake V2 protocol.
    /// </summary>
    public static class NetSquareHandshakeProtocol
    {
        #region Protocol constants
        public const byte HandshakeVersion = 2;
        public const int FrameMarkerLength = 8;
        public const ushort WireProtocolVersion = 2;
        public const int NonceLength = 16;
        public const int HashLength = 32;
        public const int ClientHelloLength = 42;
        public const int ServerChallengeLength = 65;
        public const int ClientProofLength = 49;
        public const int ServerAcceptLength = 64;
        public const int ClientReadyLength = 41;
        public const int ServerConnectedLength = 45;
        public const byte MaximumProofOfWorkDifficulty = 24;

        public const HandshakeCapabilities SupportedCapabilities =
            HandshakeCapabilities.Heartbeat |
            HandshakeCapabilities.HighPrecisionTimeSynchronization |
            HandshakeCapabilities.AuthenticatedUdpDatagrams;

        private static readonly byte[] ClientHelloMagic = Encoding.ASCII.GetBytes("NSQHCL02");
        private static readonly byte[] ServerChallengeMagic = Encoding.ASCII.GetBytes("NSQHSC02");
        private static readonly byte[] ClientProofMagic = Encoding.ASCII.GetBytes("NSQHCP02");
        private static readonly byte[] ServerAcceptMagic = Encoding.ASCII.GetBytes("NSQHSA02");
        private static readonly byte[] ClientReadyMagic = Encoding.ASCII.GetBytes("NSQHRD02");
        private static readonly byte[] ServerConnectedMagic = Encoding.ASCII.GetBytes("NSQHCN02");
        #endregion

        #region Frame creation
        /// <summary>
        /// Creates a client hello for the requested transport using the current Core assembly version.
        /// </summary>
        /// <param name="requestedTransport">Transport requested by the client.</param>
        /// <returns>The serialized fixed-size client hello frame.</returns>
        public static byte[] CreateClientHello(NetSquareProtocoleType requestedTransport)
        {
            return CreateClientHello(requestedTransport, SupportedCapabilities);
        }

        /// <summary>
        /// Creates a client hello advertising an explicit subset of supported capabilities.
        /// </summary>
        /// <param name="requestedTransport">Transport requested by the client.</param>
        /// <param name="capabilities">Capabilities enabled by this client configuration.</param>
        /// <returns>The serialized fixed-size client hello frame.</returns>
        public static byte[] CreateClientHello(
            NetSquareProtocoleType requestedTransport,
            HandshakeCapabilities capabilities)
        {
            if ((capabilities & ~SupportedCapabilities) != 0)
                throw new ArgumentOutOfRangeException(nameof(capabilities));

            // Bind compatibility to the centrally generated NetSquare.Core assembly version.
            Version libraryVersion = typeof(NetSquareHandshakeProtocol).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            byte[] nonce = CreateRandomBytes(NonceLength);
            return SerializeClientHello(new HandshakeClientHello(
                WireProtocolVersion,
                WireProtocolVersion,
                requestedTransport,
                capabilities,
                libraryVersion,
                nonce));
        }

        /// <summary>
        /// Serializes a client hello into its canonical fixed-size wire representation.
        /// </summary>
        /// <param name="hello">Client hello to serialize.</param>
        /// <returns>The serialized frame.</returns>
        public static byte[] SerializeClientHello(HandshakeClientHello hello)
        {
            if (hello == null)
                throw new ArgumentNullException(nameof(hello));
            ValidateFixedBytes(hello.ClientNonce, NonceLength, nameof(hello.ClientNonce));

            // Every integer is explicitly little-endian so the protocol is platform-independent.
            byte[] frame = new byte[ClientHelloLength];
            CopyMagic(ClientHelloMagic, frame);
            frame[8] = HandshakeVersion;
            WriteUInt16(frame, 9, hello.MinimumWireProtocolVersion);
            WriteUInt16(frame, 11, hello.MaximumWireProtocolVersion);
            frame[13] = (byte)hello.RequestedTransport;
            WriteUInt32(frame, 14, (uint)hello.Capabilities);
            WriteVersion(frame, 18, hello.LibraryVersion);
            Buffer.BlockCopy(hello.ClientNonce, 0, frame, 26, NonceLength);
            return frame;
        }

        /// <summary>
        /// Creates the server challenge bound to the received client hello.
        /// </summary>
        /// <param name="clientHelloFrame">Canonical client hello bytes.</param>
        /// <param name="transport">Negotiated transport.</param>
        /// <param name="capabilities">Negotiated capabilities.</param>
        /// <param name="proofOfWorkDifficulty">Required number of leading zero hash bits.</param>
        /// <returns>The serialized server challenge.</returns>
        public static byte[] CreateServerChallenge(
            byte[] clientHelloFrame,
            NetSquareProtocoleType transport,
            HandshakeCapabilities capabilities,
            byte proofOfWorkDifficulty)
        {
            if (proofOfWorkDifficulty > MaximumProofOfWorkDifficulty)
                throw new ArgumentOutOfRangeException(nameof(proofOfWorkDifficulty));

            // Echoing the hello hash prevents a challenge from being replayed across different attempts.
            byte[] frame = new byte[ServerChallengeLength];
            CopyMagic(ServerChallengeMagic, frame);
            frame[8] = HandshakeVersion;
            WriteUInt16(frame, 9, WireProtocolVersion);
            frame[11] = (byte)transport;
            WriteUInt32(frame, 12, (uint)capabilities);
            frame[16] = proofOfWorkDifficulty;
            Buffer.BlockCopy(CreateRandomBytes(NonceLength), 0, frame, 17, NonceLength);
            Buffer.BlockCopy(ComputeHash(clientHelloFrame), 0, frame, 33, HashLength);
            return frame;
        }

        /// <summary>
        /// Solves the server proof and returns the serialized client proof frame.
        /// </summary>
        /// <param name="clientHelloFrame">Serialized client hello.</param>
        /// <param name="serverChallengeFrame">Serialized server challenge.</param>
        /// <param name="cancellationToken">Cancellation token checked while solving.</param>
        /// <returns>The serialized proof frame.</returns>
        public static byte[] CreateClientProof(
            byte[] clientHelloFrame,
            byte[] serverChallengeFrame,
            CancellationToken cancellationToken)
        {
            HandshakeServerChallenge challenge = DeserializeServerChallenge(serverChallengeFrame);
            ulong proofNonce = 0;
            byte[] proofHash = null;
            byte[] proofInput = new byte[clientHelloFrame.Length + serverChallengeFrame.Length + 8];
            Buffer.BlockCopy(clientHelloFrame, 0, proofInput, 0, clientHelloFrame.Length);
            Buffer.BlockCopy(
                serverChallengeFrame,
                0,
                proofInput,
                clientHelloFrame.Length,
                serverChallengeFrame.Length);
            int nonceOffset = proofInput.Length - 8;

            // The server can validate each candidate with one SHA-256 while clients bear the search cost.
            using (SHA256 sha256 = SHA256.Create())
            {
                do
                {
                    if ((proofNonce & 4095UL) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    WriteUInt64(proofInput, nonceOffset, proofNonce);
                    proofHash = sha256.ComputeHash(proofInput);
                    if (HasLeadingZeroBits(proofHash, challenge.ProofOfWorkDifficulty))
                        break;

                    proofNonce++;
                }
                while (proofNonce != 0);
            }

            if (!HasLeadingZeroBits(proofHash, challenge.ProofOfWorkDifficulty))
                throw new InvalidOperationException("Unable to solve the NetSquare handshake proof.");

            byte[] frame = new byte[ClientProofLength];
            CopyMagic(ClientProofMagic, frame);
            frame[8] = HandshakeVersion;
            WriteUInt64(frame, 9, proofNonce);
            Buffer.BlockCopy(proofHash, 0, frame, 17, HashLength);
            return frame;
        }

        /// <summary>
        /// Creates the server acceptance frame for a validated proof.
        /// </summary>
        /// <param name="clientHelloFrame">Serialized client hello.</param>
        /// <param name="serverChallengeFrame">Serialized server challenge.</param>
        /// <param name="clientProofFrame">Serialized client proof.</param>
        /// <param name="transport">Negotiated transport.</param>
        /// <param name="capabilities">Negotiated capabilities.</param>
        /// <param name="sessionToken">Random session key used to authenticate UDP datagrams.</param>
        /// <returns>The serialized acceptance frame.</returns>
        public static byte[] CreateServerAccept(
            byte[] clientHelloFrame,
            byte[] serverChallengeFrame,
            byte[] clientProofFrame,
            NetSquareProtocoleType transport,
            HandshakeCapabilities capabilities,
            byte[] sessionToken)
        {
            ValidateFixedBytes(sessionToken, NonceLength, nameof(sessionToken));

            // The transcript hash makes both peers agree on the exact negotiated bytes.
            byte[] frame = new byte[ServerAcceptLength];
            CopyMagic(ServerAcceptMagic, frame);
            frame[8] = HandshakeVersion;
            WriteUInt16(frame, 9, WireProtocolVersion);
            frame[11] = (byte)transport;
            WriteUInt32(frame, 12, (uint)capabilities);
            Buffer.BlockCopy(sessionToken, 0, frame, 16, NonceLength);
            Buffer.BlockCopy(ComputeHash(clientHelloFrame, serverChallengeFrame, clientProofFrame), 0, frame, 32, HashLength);
            return frame;
        }

        /// <summary>
        /// Creates the client acknowledgement for the complete negotiated transcript.
        /// </summary>
        /// <param name="clientHelloFrame">Serialized client hello.</param>
        /// <param name="serverChallengeFrame">Serialized server challenge.</param>
        /// <param name="clientProofFrame">Serialized client proof.</param>
        /// <param name="serverAcceptFrame">Serialized server acceptance.</param>
        /// <returns>The serialized ready frame.</returns>
        public static byte[] CreateClientReady(
            byte[] clientHelloFrame,
            byte[] serverChallengeFrame,
            byte[] clientProofFrame,
            byte[] serverAcceptFrame)
        {
            // A final acknowledgement prevents the server from exposing half-negotiated clients.
            byte[] frame = new byte[ClientReadyLength];
            CopyMagic(ClientReadyMagic, frame);
            frame[8] = HandshakeVersion;
            Buffer.BlockCopy(
                ComputeHash(clientHelloFrame, serverChallengeFrame, clientProofFrame, serverAcceptFrame),
                0,
                frame,
                9,
                HashLength);
            return frame;
        }

        /// <summary>
        /// Creates the definitive server confirmation after the client ready acknowledgement.
        /// </summary>
        /// <param name="clientID">Allocated client ID.</param>
        /// <param name="clientReadyFrame">Validated client ready frame.</param>
        /// <returns>The serialized connected frame.</returns>
        public static byte[] CreateServerConnected(uint clientID, byte[] clientReadyFrame)
        {
            // Bind the allocated ID to the ready frame accepted by the server.
            byte[] frame = new byte[ServerConnectedLength];
            CopyMagic(ServerConnectedMagic, frame);
            frame[8] = HandshakeVersion;
            WriteUInt32(frame, 9, clientID);
            Buffer.BlockCopy(ComputeHash(clientReadyFrame), 0, frame, 13, HashLength);
            return frame;
        }
        #endregion

        #region Frame decoding
        /// <summary>
        /// Decodes and validates a client hello frame.
        /// </summary>
        public static HandshakeClientHello DeserializeClientHello(byte[] frame)
        {
            EnsureFrame(frame, ClientHelloLength, ClientHelloMagic, "client hello");
            NetSquareProtocoleType transport = ReadTransport(frame[13]);
            return new HandshakeClientHello(
                ReadUInt16(frame, 9),
                ReadUInt16(frame, 11),
                transport,
                (HandshakeCapabilities)ReadUInt32(frame, 14),
                ReadVersion(frame, 18),
                CopyBytes(frame, 26, NonceLength));
        }

        /// <summary>
        /// Decodes and validates a server challenge frame.
        /// </summary>
        public static HandshakeServerChallenge DeserializeServerChallenge(byte[] frame)
        {
            EnsureFrame(frame, ServerChallengeLength, ServerChallengeMagic, "server challenge");
            byte difficulty = frame[16];
            if (difficulty > MaximumProofOfWorkDifficulty)
                throw new InvalidOperationException("Unsupported NetSquare proof-of-work difficulty.");

            return new HandshakeServerChallenge(
                ReadUInt16(frame, 9),
                ReadTransport(frame[11]),
                (HandshakeCapabilities)ReadUInt32(frame, 12),
                difficulty,
                CopyBytes(frame, 17, NonceLength),
                CopyBytes(frame, 33, HashLength));
        }

        /// <summary>
        /// Decodes and validates a client proof frame.
        /// </summary>
        public static HandshakeClientProof DeserializeClientProof(byte[] frame)
        {
            EnsureFrame(frame, ClientProofLength, ClientProofMagic, "client proof");
            return new HandshakeClientProof(ReadUInt64(frame, 9), CopyBytes(frame, 17, HashLength));
        }

        /// <summary>
        /// Decodes and validates a server acceptance frame.
        /// </summary>
        public static HandshakeServerAccept DeserializeServerAccept(byte[] frame)
        {
            EnsureFrame(frame, ServerAcceptLength, ServerAcceptMagic, "server acceptance");
            return new HandshakeServerAccept(
                ReadUInt16(frame, 9),
                ReadTransport(frame[11]),
                (HandshakeCapabilities)ReadUInt32(frame, 12),
                CopyBytes(frame, 16, NonceLength),
                CopyBytes(frame, 32, HashLength));
        }

        /// <summary>
        /// Decodes and validates a definitive server connected frame.
        /// </summary>
        public static HandshakeServerConnected DeserializeServerConnected(byte[] frame)
        {
            EnsureFrame(frame, ServerConnectedLength, ServerConnectedMagic, "server connected");
            return new HandshakeServerConnected(ReadUInt32(frame, 9), CopyBytes(frame, 13, HashLength));
        }
        #endregion

        #region Transcript validation
        /// <summary>
        /// Verifies that a challenge is bound to the supplied client hello.
        /// </summary>
        public static bool ValidateChallenge(byte[] clientHelloFrame, HandshakeServerChallenge challenge)
        {
            // Compare hashes in constant time to avoid data-dependent early exits.
            return challenge != null && FixedTimeEquals(ComputeHash(clientHelloFrame), challenge.ClientHelloHash);
        }

        /// <summary>
        /// Verifies the proof nonce, hash, and requested difficulty.
        /// </summary>
        public static bool ValidateClientProof(
            byte[] clientHelloFrame,
            byte[] serverChallengeFrame,
            byte[] clientProofFrame)
        {
            HandshakeServerChallenge challenge = DeserializeServerChallenge(serverChallengeFrame);
            HandshakeClientProof proof = DeserializeClientProof(clientProofFrame);
            byte[] expectedHash = ComputeProofHash(clientHelloFrame, serverChallengeFrame, proof.ProofNonce);
            return HasLeadingZeroBits(expectedHash, challenge.ProofOfWorkDifficulty) &&
                   FixedTimeEquals(expectedHash, proof.ProofHash);
        }

        /// <summary>
        /// Verifies the server acceptance transcript hash.
        /// </summary>
        public static bool ValidateServerAccept(
            byte[] clientHelloFrame,
            byte[] serverChallengeFrame,
            byte[] clientProofFrame,
            HandshakeServerAccept accept)
        {
            // Reject any mutation of the negotiated frames before sending ReadyAck.
            byte[] expectedHash = ComputeHash(clientHelloFrame, serverChallengeFrame, clientProofFrame);
            return accept != null && FixedTimeEquals(expectedHash, accept.TranscriptHash);
        }

        /// <summary>
        /// Verifies the client ready acknowledgement against the complete transcript.
        /// </summary>
        public static bool ValidateClientReady(
            byte[] clientHelloFrame,
            byte[] serverChallengeFrame,
            byte[] clientProofFrame,
            byte[] serverAcceptFrame,
            byte[] clientReadyFrame)
        {
            EnsureFrame(clientReadyFrame, ClientReadyLength, ClientReadyMagic, "client ready");
            byte[] expectedHash = ComputeHash(clientHelloFrame, serverChallengeFrame, clientProofFrame, serverAcceptFrame);
            return FixedTimeEquals(expectedHash, CopyBytes(clientReadyFrame, 9, HashLength));
        }

        /// <summary>
        /// Verifies that the final connected frame is bound to the sent ready acknowledgement.
        /// </summary>
        public static bool ValidateServerConnected(byte[] clientReadyFrame, HandshakeServerConnected connected)
        {
            // The client ID is trusted only when the server confirmation matches this attempt.
            return connected != null && FixedTimeEquals(ComputeHash(clientReadyFrame), connected.ReadyHash);
        }
        #endregion

        #region Transport helpers
        /// <summary>
        /// Creates cryptographically strong random bytes.
        /// </summary>
        public static byte[] CreateRandomBytes(int length)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            // RandomNumberGenerator is safe across concurrent handshake workers.
            byte[] bytes = new byte[length];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
                generator.GetBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// Sends every byte in a handshake frame.
        /// </summary>
        public static void SendAll(Socket socket, byte[] frame)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            // Socket.Send may complete partially even for small handshake frames.
            int offset = 0;
            while (offset < frame.Length)
            {
                int sent = socket.Send(frame, offset, frame.Length - offset, SocketFlags.None);
                if (sent <= 0)
                    throw new SocketException((int)SocketError.ConnectionReset);
                offset += sent;
            }
        }

        /// <summary>
        /// Sends a complete handshake frame through a stream transport such as TLS.
        /// </summary>
        /// <param name="stream">Writable transport stream.</param>
        /// <param name="frame">Complete frame to send.</param>
        public static void SendAll(Stream stream, byte[] frame)
        {
            // Stream.Write guarantees that the requested buffer is consumed or an exception is raised.
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            stream.Write(frame, 0, frame.Length);
            stream.Flush();
        }

        /// <summary>
        /// Receives exactly the requested number of handshake bytes from a stream before a UTC deadline.
        /// </summary>
        /// <param name="stream">Readable transport stream.</param>
        /// <param name="length">Required frame length.</param>
        /// <param name="deadlineUtc">Absolute UTC deadline.</param>
        /// <returns>The complete frame bytes.</returns>
        public static byte[] ReceiveExact(Stream stream, int length, DateTime deadlineUtc)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                // APM waiting provides one deadline implementation shared by NetworkStream and SslStream.
                int remainingMilliseconds = (int)Math.Min(
                    int.MaxValue,
                    Math.Max(0, (deadlineUtc - DateTime.UtcNow).TotalMilliseconds));
                if (remainingMilliseconds <= 0)
                    throw new TimeoutException("The NetSquare handshake timed out.");

                IAsyncResult pendingRead = stream.BeginRead(buffer, offset, length - offset, null, null);
                if (!pendingRead.AsyncWaitHandle.WaitOne(remainingMilliseconds))
                {
                    pendingRead.AsyncWaitHandle.Close();
                    throw new TimeoutException("The NetSquare handshake timed out.");
                }

                int received;
                try { received = stream.EndRead(pendingRead); }
                finally { pendingRead.AsyncWaitHandle.Close(); }
                if (received <= 0)
                    throw new IOException("The remote peer closed the NetSquare handshake stream.");
                offset += received;
            }
            return buffer;
        }

        /// <summary>
        /// Receives exactly the requested number of handshake bytes before a UTC deadline.
        /// </summary>
        public static byte[] ReceiveExact(Socket socket, int length, DateTime deadlineUtc)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            // Poll with a short sleep so timeout checks remain independent from Socket.ReceiveTimeout behavior.
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                if (DateTime.UtcNow >= deadlineUtc)
                    throw new TimeoutException("The NetSquare handshake timed out.");

                if (socket.Poll(1000, SelectMode.SelectRead))
                {
                    int received = socket.Receive(buffer, offset, length - offset, SocketFlags.None);
                    if (received <= 0)
                        throw new SocketException((int)SocketError.ConnectionReset);
                    offset += received;
                    continue;
                }

                Thread.Sleep(1);
            }

            return buffer;
        }
        #endregion

        #region Binary helpers
        /// <summary>
        /// Computes SHA-256 over one or more buffers without ambiguous separators.
        /// </summary>
        private static byte[] ComputeHash(params byte[][] buffers)
        {
            // Handshake frames are fixed length, so direct concatenation is unambiguous.
            int totalLength = 0;
            for (int index = 0; index < buffers.Length; index++)
            {
                if (buffers[index] == null)
                    throw new ArgumentNullException(nameof(buffers));
                totalLength = checked(totalLength + buffers[index].Length);
            }

            byte[] data = new byte[totalLength];
            int offset = 0;
            for (int index = 0; index < buffers.Length; index++)
            {
                Buffer.BlockCopy(buffers[index], 0, data, offset, buffers[index].Length);
                offset += buffers[index].Length;
            }

            using (SHA256 sha256 = SHA256.Create())
                return sha256.ComputeHash(data);
        }

        /// <summary>
        /// Computes the proof hash for one nonce.
        /// </summary>
        private static byte[] ComputeProofHash(byte[] hello, byte[] challenge, ulong proofNonce)
        {
            // The nonce is appended in canonical little-endian form.
            byte[] nonceBytes = new byte[8];
            WriteUInt64(nonceBytes, 0, proofNonce);
            return ComputeHash(hello, challenge, nonceBytes);
        }

        /// <summary>
        /// Returns whether a hash begins with the requested number of zero bits.
        /// </summary>
        private static bool HasLeadingZeroBits(byte[] hash, byte difficulty)
        {
            // Whole zero bytes are checked before the remaining high-order bits.
            int completeBytes = difficulty / 8;
            int remainingBits = difficulty % 8;
            for (int index = 0; index < completeBytes; index++)
            {
                if (hash[index] != 0)
                    return false;
            }

            if (remainingBits == 0)
                return true;

            int mask = 0xFF << (8 - remainingBits);
            return (hash[completeBytes] & mask) == 0;
        }

        /// <summary>
        /// Compares same-length byte arrays without returning early on different data.
        /// </summary>
        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            // Accumulate all differences so comparison duration does not reveal a matching prefix.
            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        /// <summary>
        /// Validates a fixed frame marker and handshake version.
        /// </summary>
        private static void EnsureFrame(byte[] frame, int expectedLength, byte[] magic, string frameName)
        {
            // Strict lengths avoid parser ambiguity and oversized pre-authentication allocations.
            if (frame == null || frame.Length != expectedLength)
                throw new InvalidOperationException("Invalid NetSquare " + frameName + " length.");
            for (int index = 0; index < magic.Length; index++)
            {
                if (frame[index] != magic[index])
                    throw new InvalidOperationException("Invalid NetSquare " + frameName + " marker.");
            }
            if (frame[8] != HandshakeVersion)
                throw new InvalidOperationException("Unsupported NetSquare handshake version.");
        }

        /// <summary>
        /// Copies a frame marker into a destination buffer.
        /// </summary>
        private static void CopyMagic(byte[] magic, byte[] destination)
        {
            // All protocol markers occupy the first eight bytes.
            Buffer.BlockCopy(magic, 0, destination, 0, magic.Length);
        }

        /// <summary>
        /// Validates an exact-size byte field.
        /// </summary>
        private static void ValidateFixedBytes(byte[] bytes, int expectedLength, string parameterName)
        {
            // Reject variable token lengths before serializing a fixed frame.
            if (bytes == null || bytes.Length != expectedLength)
                throw new ArgumentException("Expected " + expectedLength + " bytes.", parameterName);
        }

        /// <summary>
        /// Reads and validates a transport enum byte.
        /// </summary>
        private static NetSquareProtocoleType ReadTransport(byte value)
        {
            // Only transports implemented by both NetSquare peers are accepted.
            if (!Enum.IsDefined(typeof(NetSquareProtocoleType), (int)value))
                throw new InvalidOperationException("Unsupported NetSquare transport.");
            return (NetSquareProtocoleType)value;
        }

        /// <summary>
        /// Copies one field from a frame.
        /// </summary>
        private static byte[] CopyBytes(byte[] source, int offset, int length)
        {
            // Return owned arrays so decoded frame objects remain immutable.
            byte[] copy = new byte[length];
            Buffer.BlockCopy(source, offset, copy, 0, length);
            return copy;
        }

        /// <summary>
        /// Writes a four-component assembly version.
        /// </summary>
        private static void WriteVersion(byte[] buffer, int offset, Version version)
        {
            // The centralized version is expected to fit four unsigned 16-bit components.
            WriteUInt16(buffer, offset, ToVersionComponent(version.Major));
            WriteUInt16(buffer, offset + 2, ToVersionComponent(version.Minor));
            WriteUInt16(buffer, offset + 4, ToVersionComponent(version.Build));
            WriteUInt16(buffer, offset + 6, ToVersionComponent(version.Revision));
        }

        /// <summary>
        /// Reads a four-component assembly version.
        /// </summary>
        private static Version ReadVersion(byte[] buffer, int offset)
        {
            // All four components are present on the wire, including the CLR revision.
            return new Version(
                ReadUInt16(buffer, offset),
                ReadUInt16(buffer, offset + 2),
                ReadUInt16(buffer, offset + 4),
                ReadUInt16(buffer, offset + 6));
        }

        /// <summary>
        /// Converts one assembly version component to its wire range.
        /// </summary>
        private static ushort ToVersionComponent(int value)
        {
            // Missing components are normalized to zero; oversized versions are rejected.
            if (value < 0)
                return 0;
            if (value > ushort.MaxValue)
                throw new InvalidOperationException("NetSquare assembly version component exceeds UInt16.");
            return (ushort)value;
        }

        /// <summary>
        /// Writes a little-endian unsigned 16-bit integer.
        /// </summary>
        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        /// <summary>
        /// Reads a little-endian unsigned 16-bit integer.
        /// </summary>
        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        /// <summary>
        /// Writes a little-endian unsigned 32-bit integer.
        /// </summary>
        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            for (int index = 0; index < 4; index++)
                buffer[offset + index] = (byte)(value >> (index * 8));
        }

        /// <summary>
        /// Reads a little-endian unsigned 32-bit integer.
        /// </summary>
        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            uint value = 0;
            for (int index = 0; index < 4; index++)
                value |= (uint)buffer[offset + index] << (index * 8);
            return value;
        }

        /// <summary>
        /// Writes a little-endian unsigned 64-bit integer.
        /// </summary>
        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            for (int index = 0; index < 8; index++)
                buffer[offset + index] = (byte)(value >> (index * 8));
        }

        /// <summary>
        /// Reads a little-endian unsigned 64-bit integer.
        /// </summary>
        private static ulong ReadUInt64(byte[] buffer, int offset)
        {
            ulong value = 0;
            for (int index = 0; index < 8; index++)
                value |= (ulong)buffer[offset + index] << (index * 8);
            return value;
        }
        #endregion
    }
}
