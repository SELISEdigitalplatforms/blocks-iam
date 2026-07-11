namespace Authentication.DomainService.Security.Models
{
    public sealed class UserSessionDto
    {
        public string SessionId { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string? UserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public DateTime AbsoluteExpiry { get; set; }
        public DateTime IdleExpiry { get; set; }
        public bool IsCurrent { get; set; }
        public SessionStatus Status { get; set; }
        public string? PrimaryDeviceName { get; set; }
        public string? PrimaryOperatingSystem { get; set; }
        public string? PrimaryBrowser { get; set; }
        public string? PrimaryIpAddress { get; set; }
        public int ApplicationCount { get; set; }
        public List<string> ClientIds { get; set; } = new();
    }
}
