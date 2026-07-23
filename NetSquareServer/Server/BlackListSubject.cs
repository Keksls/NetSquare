using System;

namespace NetSquare.Server
{
    /// <summary>
    /// Identifies a generic subject that can receive hits and bans.
    /// </summary>
    public sealed class BlackListSubject : IEquatable<BlackListSubject>
    {
        public const string IPAddressType = "ip";

        public string Type { get; private set; }
        public string Identifier { get; private set; }

        /// <summary>
        /// Creates a subject from a project-defined type and stable identifier.
        /// </summary>
        /// <param name="type">Identity namespace, such as account, device or ip.</param>
        /// <param name="identifier">Stable identifier inside the namespace.</param>
        public BlackListSubject(string type, string identifier)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("A blacklist subject type is required.", nameof(type));
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("A blacklist subject identifier is required.", nameof(identifier));

            // Subject types are case-insensitive while identifiers remain project-defined and case-sensitive.
            Type = type.Trim().ToLowerInvariant();
            Identifier = identifier.Trim();
        }

        /// <summary>
        /// Creates an IP subject using the canonical IPv4 or IPv6 representation.
        /// </summary>
        /// <param name="ipAddress">IPv4 or IPv6 address.</param>
        /// <returns>The canonical IP subject.</returns>
        public static BlackListSubject ForIPAddress(string ipAddress)
        {
            string normalizedAddress;
            if (!IPAddressUtilities.TryNormalize(ipAddress, out normalizedAddress))
                throw new ArgumentException("A valid IPv4 or IPv6 address is required.", nameof(ipAddress));

            return new BlackListSubject(IPAddressType, normalizedAddress);
        }

        /// <summary>
        /// Returns whether another subject identifies the same target.
        /// </summary>
        /// <param name="other">Subject to compare.</param>
        /// <returns>True when type and identifier match.</returns>
        public bool Equals(BlackListSubject other)
        {
            return other != null &&
                   string.Equals(Type, other.Type, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Identifier, other.Identifier, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns whether an object identifies the same target.
        /// </summary>
        /// <param name="obj">Object to compare.</param>
        /// <returns>True when the object is an equal subject.</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as BlackListSubject);
        }

        /// <summary>
        /// Returns a hash code matching the subject equality rules.
        /// </summary>
        /// <returns>The subject hash code.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int typeHash = StringComparer.OrdinalIgnoreCase.GetHashCode(Type);
                int identifierHash = StringComparer.Ordinal.GetHashCode(Identifier);
                return (typeHash * 397) ^ identifierHash;
            }
        }

        /// <summary>
        /// Formats the subject as a readable namespaced identifier.
        /// </summary>
        /// <returns>The subject type and identifier.</returns>
        public override string ToString()
        {
            return Type + ":" + Identifier;
        }
    }
}
