namespace Authentication.DomainService.Security.Models
{
    public sealed class RefreshTokenStatus
    {
        public string? TokenId { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? IssuedAt { get; set; }
        public DateTime? AbsoluteExpiry { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokeReason { get; set; }
    }

    public sealed class RevokedAccessTokenDto
    {
        public string? Jti { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? Reason { get; set; }
    }
}