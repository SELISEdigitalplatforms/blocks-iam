using Iam.DomainService.Dtos;

namespace Iam.DomainService.Activity.RequestModel
{
    public class GetActivitiesFilter
    {
        public string? ActorUserId { get; set; }
        public List<UserActivityCategory>? Categories { get; set; }
        public List<string>? Events { get; set; }
        public List<string>? Outcomes { get; set; }
        public List<string>? Severities { get; set; }
        public string? Source { get; set; }
        public string? SessionId { get; set; }
        public string? ClientId { get; set; }
        public string? TenantId { get; set; }
        public string? OrganizationId { get; set; }
        public string? CorrelationId { get; set; }
        public string? Entity { get; set; }
        public string? EntityId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? Search { get; set; }
    }
}