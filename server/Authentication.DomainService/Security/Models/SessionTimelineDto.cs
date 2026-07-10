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
}
