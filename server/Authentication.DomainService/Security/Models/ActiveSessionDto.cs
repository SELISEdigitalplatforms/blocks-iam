namespace Authentication.DomainService.Security.Models
{
    public sealed class ActiveSessionDto
    {
        public string TokenId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string? OrganizationId { get; set; }
        public string? ClientId { get; set; }
        public string? GrantType { get; set; }
        public string? IpAddresses { get; set; }
        public string? UserAgent { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceModel { get; set; }
        public string? OperatingSystem { get; set; }
        public string? Browser { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime SlidingExpiry { get; set; }
        public DateTime AbsoluteExpiry { get; set; }
        public bool IsActive { get; set; }
        public bool Impersonated { get; set; }
        public string? ImpersonationId { get; set; }
    }
}
