using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public sealed class ImpersonationSession
    {
        [BsonId]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; }
        public string TargetTenantId { get; set; }
        public string RootTenantId { get; set; }
        public string ClientId { get; set; }
        public string? OrganizationId { get; set; } = "default";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "active"; // active, ended_by_logout, ended_by_admin_stop
        public DateTime? EndedAt { get; set; }
        public string? Reason { get; set; }
        public DateTime CreateDate { get; set; } = DateTime.UtcNow;
        public DateTime UpdateDate { get; set; } = DateTime.UtcNow;
    }
}
