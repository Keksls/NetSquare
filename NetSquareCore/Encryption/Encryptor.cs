#region Source
namespace NetSquare.Core.Encryption
{
    /// <summary>
    /// Represents the encryptor component.
    /// </summary>
    public abstract class Encryptor
    {
        /// <summary>
        /// Gets or sets the key iv value.
        /// </summary>
        public KeyIV KeyIV { get; internal set; }
        /// <summary>
        /// Gets or sets the password value.
        /// </summary>
        public string password { get; internal set; }

        /// <summary>
        /// Initializes a new instance of the encryptor class.
        /// </summary>
        public Encryptor()
        {
            KeyIV = new KeyIV();
        }

        /// <summary>
        /// Executes the set key operation.
        /// </summary>
        public void SetKey(string _password)
        {
            PreSetKey();
            password = _password;
            PostSetKey();
        }

        /// <summary>
        /// Executes the set key operation.
        /// </summary>
        public void SetKey(byte[] _key, byte[] _IV)
        {
            PreSetKey();
            KeyIV.Key = _key;
            KeyIV.IV = _IV;
            PostSetKey();
        }

        /// <summary>
        /// Executes the set key operation.
        /// </summary>
        public void SetKey(KeyIV _keyIV)
        {
            PreSetKey();
            KeyIV = _keyIV;
            PostSetKey();
        }

        /// <summary>
        /// Executes the pre set key operation.
        /// </summary>
        internal virtual void PreSetKey() { }
        /// <summary>
        /// Executes the post set key operation.
        /// </summary>
        internal virtual void PostSetKey() { }

        /// <summary>
        /// Executes the encrypt operation.
        /// </summary>
        public abstract byte[] Encrypt(byte[] data);

        /// <summary>
        /// Executes the decrypt operation.
        /// </summary>
        public abstract byte[] Decrypt(byte[] data);
    }
}
#endregion
