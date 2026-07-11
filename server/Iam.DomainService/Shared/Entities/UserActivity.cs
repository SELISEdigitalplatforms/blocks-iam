using Blocks.Genesis;
using Iam.DomainService.Dtos;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class UserActivity : BaseEntity
    {
        [BsonRepresentation(MongoDB.Bson.BsonType.String)]
        public required UserActivityCategory Category { get; set; }
        public required string Event { get; set; }
        public required string UserId { get; set; }
        public required string ActorUserId { get; set; }
        public string? TenantId { get; set; }
        public string? Outcome { get; set; }
        public string? ReasonCode { get; set; }
        public string? Severity { get; set; }
        public string? Source { get; set; }
        public string? MessageId { get; set; }
        public string? CorrelationId { get; set; }
        public string? SessionId { get; set; }
        public string? ClientId { get; set; }
        public ActivityContext? Context { get; set; }
        public string? Entity { get; set; }
        public string? EntityId { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }
}