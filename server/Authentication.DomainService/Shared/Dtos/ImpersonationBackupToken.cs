namespace Authentication.DomainService.Dtos
{
    public class ImpersonationBackupToken
    {
        public string RefreshToken { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastRotated { get; set; }
    }
}
