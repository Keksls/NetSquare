namespace NetSquare.Core.Encryption
{
    public abstract class Encryptor
    {
        public KeyIV KeyIV { get; internal set; }
        public string password { get; internal set; }

        public Encryptor()
        {
            KeyIV = new KeyIV();
        }

        public void SetKey(string _password)
        {
            PreSetKey();
            password = _password;
            PostSetKey();
        }

        public void SetKey(byte[] _key, byte[] _IV)
        {
            PreSetKey();
            KeyIV.Key = _key;
            KeyIV.IV = _IV;
            PostSetKey();
        }

        public void SetKey(KeyIV _keyIV)
        {
            PreSetKey();
            KeyIV = _keyIV;
            PostSetKey();
        }

        internal virtual void PreSetKey() { }
        internal virtual void PostSetKey() { }

        public abstract byte[] Encrypt(byte[] data);

        public abstract byte[] Decrypt(byte[] data);
    }
}