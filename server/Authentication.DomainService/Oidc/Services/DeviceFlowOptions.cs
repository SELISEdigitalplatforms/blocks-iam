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

        /// <summary>
        /// Externally-visible base URL (scheme + host, optional path) used to build
        /// the device-flow verification URIs returned to clients (verification_uri /
        /// verification_uri_complete) and the SPA's device returnUrl. When set, this
        /// overrides whatever <c>HttpRequest.Scheme/Host</c> reports — important when
        /// the device_authorization endpoint is invoked over an internal transport
        /// (e.g. gRPC) whose scheme does not reflect the public-facing protocol.
        /// Example: <c>https://iam.seliseblocks.com</c>.
        /// </summary>
        public string? PublicBaseUrl { get; set; }

        public TimeSpan Expiration => TimeSpan.FromSeconds(Math.Max(1, ExpirationSeconds));
        public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(1, PollIntervalSeconds));
        public TimeSpan CleanupSweepInterval => TimeSpan.FromSeconds(Math.Max(1, CleanupSweepIntervalSeconds));
        public int NormalizedSlowDownIncrementSeconds => Math.Max(1, SlowDownIncrementSeconds);
        public int NormalizedCleanupBatchLimit => Math.Max(1, CleanupBatchLimit);
    }
}
