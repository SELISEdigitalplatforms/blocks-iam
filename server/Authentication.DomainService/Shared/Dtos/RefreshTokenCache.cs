namespace Authentication.DomainService.Dtos
{
    public class RefreshTokenCache
    {
        public string? RefreshToken { get; set; }
        public string? TenantId { get; set; }
        public string? OrganizationId { get; set; }
        public string? ClientId { get; set; }
        public string? SessionId { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime AbsoluteExpiresUtc { get; set; }
        public string? UserId { get; set; }
        public string? IpAddresses { get; set; }
        public string AuthMode { get; set; } = "root";
        public string? OriginalTenantId { get; set; }
        public string? TargetTenantId { get; set; }
        public string? ImpersonatorUserId { get; set; }
        public bool RememberMe { get; set; }
        public int TokenVersion { get; set; }
        public DateTime? RememberMeIssuedUtc { get; set; }
        public DateTime? RememberMeExpiresUtc { get; set; }
    }
}
