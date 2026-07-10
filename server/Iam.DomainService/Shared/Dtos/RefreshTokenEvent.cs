namespace Iam.DomainService.Dtos
{
    public class RefreshTokenEvent
    {
        public string RefreshToken { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string? OrganizationId { get; set; }
        public string? ClientId { get; set; }
        public string? SessionId { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string IpAddresses { get; set; } = string.Empty;
        public DeviceInformation? DeviceInformation { get; set; }
        public bool IsLogin { get; set; }
        public bool IsRevoke { get; set; }
        public string? GrantType { get; set; }
        public bool Impersonated { get; set; }
        public string? ImpersonationId { get; set; }
        public string? Outcome { get; set; }
        public string? ReasonCode { get; set; }
        public string? RiskLevel { get; set; }
        public string? CorrelationId { get; set; }
    }
}