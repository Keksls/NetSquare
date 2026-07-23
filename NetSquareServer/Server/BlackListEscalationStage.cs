namespace NetSquare.Server
{
    /// <summary>
    /// Defines one hit threshold and the ban applied when it is reached.
    /// </summary>
    public sealed class BlackListEscalationStage
    {
        public int HitThreshold { get; set; }
        public BlackListBanType BanType { get; set; }
        public int BanDurationSeconds { get; set; }
    }
}
