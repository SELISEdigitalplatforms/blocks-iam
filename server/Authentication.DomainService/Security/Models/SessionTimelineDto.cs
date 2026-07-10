namespace Authentication.DomainService.Security.Models
{
    public sealed class SessionTimelineDto
    {
        public string? SessionId { get; set; }
        public SessionDto? Session { get; set; }
        public RefreshTokenStatus? RefreshTokenStatus { get; set; }
        public List<RevokedAccessTokenDto> RevokedAccessTokens { get; set; } = [];
        public List<AuthHistoryDto> Lifecycle { get; set; } = [];
        public List<RefreshTokenRotationDto> Rotations { get; set; } = [];
    }

    public sealed class RefreshTokenRotationDto
    {
        public string? TokenId { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime AbsoluteExpiry { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokeReason { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }
}