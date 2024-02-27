namespace NetSquare.Core.Encryption
{
    public enum NetSquareEncryption
    {
        NoEncryption = 0,
        ReverseByte = 1,
        OneToZeroBit = 2,
        AES = 3,
        CaesarChipher = 4,
        Rijndael = 5,
        SimplePasswordedCipher = 6,
        CustomSBC = 7,
        XOR = 8
    }
}