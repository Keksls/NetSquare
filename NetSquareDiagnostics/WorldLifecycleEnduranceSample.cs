namespace NetSquareDiagnostics
{
    /// <summary>
    /// Stores one world lifecycle endurance measurement.
    /// </summary>
    internal sealed class WorldLifecycleEnduranceSample
    {
        public int Cycle;
        public int WorldCount;
        public int SessionCount;
        public int MembershipCount;
        public int RetainedWorldCount;
        public long ManagedMemoryBytes;
        public long PrivateMemoryBytes;
        public long WorkingSetBytes;
    }
}
