using System;
using System.Net;
using System.Net.Sockets;

namespace NetSquare.Server
{
    /// <summary>
    /// Provides canonical IP parsing and address classification helpers.
    /// </summary>
    internal static class IPAddressUtilities
    {
        /// <summary>
        /// Parses and canonicalizes an IP address.
        /// </summary>
        /// <param name="value">IP address text.</param>
        /// <param name="canonicalAddress">Canonical address text.</param>
        /// <returns>True when the value is a valid IP address.</returns>
        public static bool TryNormalize(string value, out string canonicalAddress)
        {
            IPAddress address;
            if (!IPAddress.TryParse(value, out address))
            {
                canonicalAddress = null;
                return false;
            }

            // Store IPv4-mapped IPv6 addresses in the same form as native IPv4 peers.
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            canonicalAddress = address.ToString();
            return true;
        }

        /// <summary>
        /// Returns whether an address should stay outside public reputation services.
        /// </summary>
        /// <param name="value">Canonical or parseable IP address text.</param>
        /// <returns>True for loopback, private, link-local, multicast or unspecified addresses.</returns>
        public static bool IsNonPublic(string value)
        {
            IPAddress address;
            if (!IPAddress.TryParse(value, out address))
                return true;

            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                return true;

            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                // Cover RFC1918, link-local, carrier-grade NAT, multicast and reserved IPv4 ranges.
                return bytes[0] == 0 ||
                       bytes[0] == 10 ||
                       bytes[0] == 127 ||
                       (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                       (bytes[0] == 169 && bytes[1] == 254) ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       bytes[0] >= 224;
            }

            // Cover IPv6 unique-local, link-local, multicast and unspecified ranges.
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6Multicast ||
                   address.IsIPv6SiteLocal ||
                   (bytes[0] & 0xFE) == 0xFC;
        }

        /// <summary>
        /// Gets the canonical remote IP address from a socket.
        /// </summary>
        /// <param name="socket">Connected socket.</param>
        /// <returns>Canonical remote IP address.</returns>
        public static string GetRemoteAddress(Socket socket)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));

            IPEndPoint remoteEndPoint = socket.RemoteEndPoint as IPEndPoint;
            if (remoteEndPoint == null)
                throw new InvalidOperationException("The socket does not expose an IP remote endpoint.");

            string canonicalAddress;
            if (!TryNormalize(remoteEndPoint.Address.ToString(), out canonicalAddress))
                throw new InvalidOperationException("The socket remote endpoint is not a valid IP address.");

            return canonicalAddress;
        }
    }
}
