using System;

namespace NetSquare.Core.Encryption
{
    [Serializable]
    public class KeyIV
    {
        public byte[] Key { get; set; }
        public byte[] IV { get; set; }
    }
}
