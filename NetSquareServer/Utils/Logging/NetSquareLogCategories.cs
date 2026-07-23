namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Exposes the log categories used by NetSquare server components.
    /// </summary>
    public static class NetSquareLogCategories
    {
        public static readonly WriterCategory General = Writer.DefineCategory("NetSquare");
        public static readonly WriterCategory Database = Writer.DefineCategory("NetSquare.Database");
        public static readonly WriterCategory PhysicalPersistence = Writer.DefineCategory("NetSquare.PhysicalPersistence");
        public static readonly WriterCategory Spells = Writer.DefineCategory("NetSquare.Spells");
        public static readonly WriterCategory Monsters = Writer.DefineCategory("NetSquare.Monsters");
        public static readonly WriterCategory Fight = Writer.DefineCategory("NetSquare.Fight");
        public static readonly WriterCategory Server = Writer.DefineCategory("NetSquare.Server");
        public static readonly WriterCategory Pnj = Writer.DefineCategory("NetSquare.PNJ");
        public static readonly WriterCategory Logging = Writer.DefineCategory("NetSquare.Logging");
    }
}
