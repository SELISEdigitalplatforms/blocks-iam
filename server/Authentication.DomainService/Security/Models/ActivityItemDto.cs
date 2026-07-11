using Iam.DomainService.Dtos;

namespace Authentication.DomainService.Security.Models
{
    public sealed class ActivityItemDto
    {
        public string ItemId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string ActorUserId { get; set; } = "";
        public UserActivityCategory Category { get; set; }
        public string Event { get; set; } = "";
        public string? Outcome { get; set; }
        public string? ReasonCode { get; set; }
        public string? Severity { get; set; }
        public string? Source { get; set; }
        public string? CorrelationId { get; set; }
        public string? SessionId { get; set; }
        public string? ClientId { get; set; }
        public string? TenantId { get; set; }
        public string? Entity { get; set; }
        public string? EntityId { get; set; }
        public ActivityContext? Context { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
