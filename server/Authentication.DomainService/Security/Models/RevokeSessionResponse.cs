namespace Authentication.DomainService.Security.Models
{
    public sealed class RevokeSessionResponse
    {
        public string? SessionId { get; set; }
        public bool AlreadyRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? Reason { get; set; }
        public int RevokedRefreshTokens { get; set; }
        public List<string> Warnings { get; set; } = [];
    }
}