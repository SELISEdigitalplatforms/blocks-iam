namespace Iam.DomainService.Resources.TenantPropagation
{
    public class TenantPropagationResult
    {
        public string TenantId { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public long? DocumentsAffected { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorType { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
