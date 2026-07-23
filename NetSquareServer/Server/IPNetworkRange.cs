using System;
using System.Net;

namespace NetSquare.Server
{
    /// <summary>
    /// Represents one IPv4 or IPv6 CIDR network.
    /// </summary>
    internal sealed class IPNetworkRange
    {
        private readonly byte[] networkBytes;
        private readonly int prefixLength;

        /// <summary>
        /// Initializes a parsed CIDR network.
        /// </summary>
        /// <param name="networkBytes">Masked network bytes.</param>
        /// <param name="prefixLength">CIDR prefix length.</param>
        private IPNetworkRange(byte[] networkBytes, int prefixLength)
        {
            this.networkBytes = networkBytes;
            this.prefixLength = prefixLength;
        }

        /// <summary>
        /// Parses an IPv4 or IPv6 CIDR network.
        /// </summary>
        /// <param name="value">CIDR text.</param>
        /// <param name="network">Parsed network.</param>
        /// <returns>True when the CIDR value is valid.</returns>
        public static bool TryParse(string value, out IPNetworkRange network)
        {
            network = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Trim().Split('/');
            IPAddress address;
            int parsedPrefixLength;
            if (parts.Length != 2 ||
                !IPAddress.TryParse(parts[0], out address) ||
                !int.TryParse(parts[1], out parsedPrefixLength))
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            byte[] bytes = address.GetAddressBytes();
            int bitLength = bytes.Length * 8;
            if (parsedPrefixLength < 0 || parsedPrefixLength > bitLength)
                return false;

            ApplyMask(bytes, parsedPrefixLength);
            network = new IPNetworkRange(bytes, parsedPrefixLength);
            return true;
        }

        /// <summary>
        /// Returns whether an IP address belongs to this network.
        /// </summary>
        /// <param name="address">IP address to test.</param>
        /// <returns>True when the address is contained by this network.</returns>
        public bool Contains(IPAddress address)
        {
            if (address == null)
                return false;

            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            byte[] addressBytes = address.GetAddressBytes();
            if (addressBytes.Length != networkBytes.Length)
                return false;

            ApplyMask(addressBytes, prefixLength);
            for (int index = 0; index < networkBytes.Length; index++)
            {
                if (addressBytes[index] != networkBytes[index])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Applies a CIDR mask directly to an address byte array.
        /// </summary>
        /// <param name="bytes">Address bytes.</param>
        /// <param name="maskLength">CIDR prefix length.</param>
        private static void ApplyMask(byte[] bytes, int maskLength)
        {
            // Preserve complete prefix bytes, mask the partial byte, then clear the host bytes.
            int completeBytes = maskLength / 8;
            int remainingBits = maskLength % 8;
            if (remainingBits > 0 && completeBytes < bytes.Length)
            {
                int mask = 0xFF << (8 - remainingBits);
                bytes[completeBytes] = (byte)(bytes[completeBytes] & mask);
                completeBytes++;
            }

            for (int index = completeBytes; index < bytes.Length; index++)
                bytes[index] = 0;
        }
    }
}
