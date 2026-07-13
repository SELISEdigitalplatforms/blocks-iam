namespace Iam.DomainService.Dtos
{
    public sealed record UserActivityEvent
    {
        public string UserId { get; init; } = "";
        public string? ActorUserId { get; init; }
        public UserActivityCategory Category { get; init; }
        public string Event { get; init; } = "";
        public string? Outcome { get; init; }
        public string? ReasonCode { get; init; }
        public string? Severity { get; init; }
        public string? Source { get; init; }
        public string? MessageId { get; init; }
        public string? CorrelationId { get; init; }
        public string? SessionId { get; init; }
        public string? ClientId { get; init; }
        public string? TenantId { get; init; }
        public ActivityContext? Context { get; init; }
        public string? Entity { get; init; }
        public string? EntityId { get; init; }
        public Dictionary<string, string>? Metadata { get; init; }
    }
}