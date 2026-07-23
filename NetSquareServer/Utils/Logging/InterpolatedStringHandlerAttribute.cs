#if !NET6_0_OR_GREATER
using System;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Marks a type as an interpolated string handler on legacy target frameworks.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class InterpolatedStringHandlerAttribute : Attribute
    {
        /// <summary>
        /// Initializes the compiler marker attribute.
        /// </summary>
        public InterpolatedStringHandlerAttribute()
        {
            // The attribute contains no runtime behavior.
        }
    }
}
#endif
