namespace Authentication.DomainService.Security.Models
{
    public sealed class SecuritySummaryDto
    {
        public string? CurrentSessionId { get; set; }
        public int TotalSessions { get; set; }
        public int ActiveSessions { get; set; }
        public int ExpiredSessions { get; set; }
        public int RevokedSessions { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }
}
