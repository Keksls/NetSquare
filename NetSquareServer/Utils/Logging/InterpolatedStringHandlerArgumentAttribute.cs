#if !NET6_0_OR_GREATER
using System;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Identifies arguments forwarded to an interpolated string handler on legacy target frameworks.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class InterpolatedStringHandlerArgumentAttribute : Attribute
    {
        public string[] Arguments { get; }

        /// <summary>
        /// Initializes the compiler argument-forwarding attribute.
        /// </summary>
        public InterpolatedStringHandlerArgumentAttribute(params string[] arguments)
        {
            // Argument names are stored exactly as emitted by the consuming compiler.
            Arguments = arguments;
        }
    }
}
#endif
