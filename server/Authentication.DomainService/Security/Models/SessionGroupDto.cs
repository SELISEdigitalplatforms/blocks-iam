namespace Authentication.DomainService.Security.Models
{
    public sealed class SessionGroupDto
    {
        public string SessionId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public bool IsCurrent { get; set; }
        public List<ActiveSessionDto> Apps { get; set; } = new();
    }
}
