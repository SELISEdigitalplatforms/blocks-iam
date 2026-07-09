namespace Authentication.DomainService.Security.Models
{
    public sealed class IdpSessionAccountDto
    {
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? DisplayName { get; set; }
        public DateTime LoginAt { get; set; }
    }

    public sealed class IdpSessionSummaryDto
    {
        public string? SessionId { get; set; }
        public string? TenantId { get; set; }
        public List<IdpSessionAccountDto> Accounts { get; set; } = [];
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public DateTime IdleExpiry { get; set; }
        public DateTime AbsoluteExpiry { get; set; }
        public bool IsRevoked { get; set; }
    }
}