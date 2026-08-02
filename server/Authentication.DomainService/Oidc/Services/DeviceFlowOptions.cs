namespace Authentication.DomainService.Oidc.Services
{
    public sealed class DeviceFlowOptions
    {
        public const string SectionName = "DeviceFlow";

        public int ExpirationSeconds { get; set; } = 600;
        public int PollIntervalSeconds { get; set; } = 5;
        public int SlowDownIncrementSeconds { get; set; } = 5;
        public int CleanupSweepIntervalSeconds { get; set; } = 300;
        public int CleanupBatchLimit { get; set; } = 500;

        public TimeSpan Expiration => TimeSpan.FromSeconds(Math.Max(1, ExpirationSeconds));
        public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(1, PollIntervalSeconds));
        public TimeSpan CleanupSweepInterval => TimeSpan.FromSeconds(Math.Max(1, CleanupSweepIntervalSeconds));
        public int NormalizedSlowDownIncrementSeconds => Math.Max(1, SlowDownIncrementSeconds);
        public int NormalizedCleanupBatchLimit => Math.Max(1, CleanupBatchLimit);
    }
}
