namespace Authentication.DomainService.Security.Models
{
    public sealed class SecurityOverviewDto
    {
        public string? CurrentSessionId { get; set; }
        public IReadOnlyList<SessionGroupDto> SessionGroups { get; set; } = [];
        public IdpSessionSummaryDto? IdpSession { get; set; }
    }
}