using System;

#region Source
namespace NetSquare.Core.Encryption
{
    [Serializable]
    /// <summary>
    /// Represents the key iv component.
    /// </summary>
    public class KeyIV
    {
        /// <summary>
        /// Gets or sets the key value.
        /// </summary>
        public byte[] Key { get; set; }
        /// <summary>
        /// Gets or sets the iv value.
        /// </summary>
        public byte[] IV { get; set; }
    }
}
#endregion
