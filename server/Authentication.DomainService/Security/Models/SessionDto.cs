using Iam.DomainService.Dtos;

namespace Authentication.DomainService.Security.Models
{
    public sealed class SessionDto
    {
        public string? SessionId { get; set; }
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? OrganizationId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientName { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceType { get; set; }
        public string? OperatingSystem { get; set; }
        public string? Browser { get; set; }
        public string? IpAddresses { get; set; }
        public string? GrantType { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public bool IsActive { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsImpersonated { get; set; }
        public int RotationCount { get; set; }
        public DateTime? LastRotatedAt { get; set; }
    }
}