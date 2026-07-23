using System;

namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Represents a named structured value with unboxed storage for common value types.
    /// </summary>
    public readonly struct NetSquareLogProperty
    {
        private readonly object referenceValue;
        private readonly long signedValue;
        private readonly ulong unsignedValue;
        private readonly double floatingValue;
        private readonly decimal decimalValue;
        private readonly Guid guidValue;
        private readonly NetSquareLogValueKind valueKind;

        public string Name { get; }
        public object Value => GetBoxedValue();
        internal NetSquareLogValueKind ValueKind => valueKind;
        internal object ReferenceValue => referenceValue;
        internal long SignedValue => signedValue;
        internal ulong UnsignedValue => unsignedValue;
        internal double FloatingValue => floatingValue;
        internal decimal DecimalValue => decimalValue;
        internal Guid GuidValue => guidValue;

        /// <summary>
        /// Initializes a string property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, string value)
            : this(name, value == null ? NetSquareLogValueKind.Null : NetSquareLogValueKind.String, value, 0, 0, 0, 0, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes a 32-bit signed integer property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, int value)
            : this(name, NetSquareLogValueKind.SignedInteger, null, value, 0, 0, 0, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes a 32-bit unsigned integer property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, uint value)
            : this(name, NetSquareLogValueKind.UnsignedInteger, null, 0, value, 0, 0, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }
        /// <summary>
        /// Initializes a signed integer property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, long value)
            : this(name, NetSquareLogValueKind.SignedInteger, null, value, 0, 0, 0, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes an unsigned integer property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, ulong value)
            : this(name, NetSquareLogValueKind.UnsignedInteger, null, 0, value, 0, 0, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes a floating-point property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, double value)
            : this(name, NetSquareLogValueKind.FloatingPoint, null, 0, 0, value, 0, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes a decimal property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, decimal value)
            : this(name, NetSquareLogValueKind.Decimal, null, 0, 0, 0, value, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes a Boolean property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, bool value)
            : this(name, NetSquareLogValueKind.Boolean, null, 0, value ? 1UL : 0UL, 0, 0, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes a Guid property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, Guid value)
            : this(name, NetSquareLogValueKind.Guid, null, 0, 0, 0, 0, value)
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes a DateTime property without boxing.
        /// </summary>
        public NetSquareLogProperty(string name, DateTime value)
            : this(name, NetSquareLogValueKind.DateTime, null, value.Ticks, (ulong)value.Kind, 0, 0, default(Guid))
        {
            // The shared constructor owns validation and field initialization.
        }

        /// <summary>
        /// Initializes an arbitrary property through the allocation-tolerant fallback storage.
        /// </summary>
        public NetSquareLogProperty(string name, object value)
            : this(name, value == null ? NetSquareLogValueKind.Null : NetSquareLogValueKind.Object, value, 0, 0, 0, 0, default(Guid))
        {
            // Value types passed through this overload are boxed by the caller and remain a slow path.
        }

        /// <summary>
        /// Initializes every storage field through one validated constructor.
        /// </summary>
        private NetSquareLogProperty(
            string name,
            NetSquareLogValueKind valueKind,
            object referenceValue,
            long signedValue,
            ulong unsignedValue,
            double floatingValue,
            decimal decimalValue,
            Guid guidValue)
        {
            // Property names are validated once when consuming projects create the value.
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A log property name is required.", nameof(name));

            Name = name;
            this.valueKind = valueKind;
            this.referenceValue = referenceValue;
            this.signedValue = signedValue;
            this.unsignedValue = unsignedValue;
            this.floatingValue = floatingValue;
            this.decimalValue = decimalValue;
            this.guidValue = guidValue;
        }

        /// <summary>
        /// Boxes the stored value only when a consumer explicitly requests the compatibility property.
        /// </summary>
        private object GetBoxedValue()
        {
            // Writer formatting uses typed internal fields and never calls this compatibility accessor.
            switch (valueKind)
            {
                case NetSquareLogValueKind.String:
                case NetSquareLogValueKind.Object:
                    return referenceValue;
                case NetSquareLogValueKind.SignedInteger:
                    return signedValue;
                case NetSquareLogValueKind.UnsignedInteger:
                    return unsignedValue;
                case NetSquareLogValueKind.FloatingPoint:
                    return floatingValue;
                case NetSquareLogValueKind.Decimal:
                    return decimalValue;
                case NetSquareLogValueKind.Boolean:
                    return unsignedValue != 0;
                case NetSquareLogValueKind.Guid:
                    return guidValue;
                case NetSquareLogValueKind.DateTime:
                    return new DateTime(signedValue, (DateTimeKind)unsignedValue);
                default:
                    return null;
            }
        }
    }
}
