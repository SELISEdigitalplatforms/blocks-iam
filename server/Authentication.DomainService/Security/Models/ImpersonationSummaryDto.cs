namespace Authentication.DomainService.Security.Models
{
    public sealed class ImpersonationSummaryDto
    {
        public string? Id { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string? RootTenantId { get; set; }
        public string? TargetTenantId { get; set; }
        public string? Status { get; set; }
        public string? Reason { get; set; }
    }
}