namespace NetSquare.Server.Utils
{
    /// <summary>
    /// Exposes category discovery helpers for Writer configuration user interfaces.
    /// </summary>
    public static partial class Writer
    {
        /// <summary>
        /// Gets a stable snapshot of every category declared by NetSquare and consuming projects.
        /// </summary>
        public static WriterCategory[] GetCategories()
        {
            // Discovery allocates only when configuration code explicitly requests a snapshot.
            lock (configurationLock)
            {
                WriterCategory[] snapshot = new WriterCategory[categories.Count];
                categories.Values.CopyTo(snapshot, 0);
                return snapshot;
            }
        }
    }
}
