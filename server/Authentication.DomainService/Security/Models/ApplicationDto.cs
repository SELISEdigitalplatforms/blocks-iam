namespace Authentication.DomainService.Security.Models
{
    public sealed class ApplicationDto
    {
        public string ClientId { get; set; } = "";
        public string? ClientName { get; set; }
        public string? OrganizationId { get; set; }
        public string? GrantType { get; set; }
        public SessionStatus Status { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? LastRotationAt { get; set; }
        public int RotationCount { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokeReason { get; set; }
    }
}
