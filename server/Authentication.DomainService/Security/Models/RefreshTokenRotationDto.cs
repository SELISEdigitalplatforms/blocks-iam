namespace Authentication.DomainService.Security.Models
{
    public sealed class RefreshTokenRotationDto
    {
        public string? Fingerprint { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime AbsoluteExpiry { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokeReason { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public bool IsCurrent { get; set; }
    }
}
