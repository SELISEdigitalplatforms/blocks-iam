using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class ImpersonationSession
    {
        [BsonId]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [BsonElement("user_id")]
        public string UserId { get; set; }
        
        [BsonElement("target_tenant_id")]
        public string TargetTenantId { get; set; }
        
        [BsonElement("root_tenant_id")]
        public string RootTenantId { get; set; }
        
        [BsonElement("org_id")]
        public string? OrganizationId { get; set; } = "default";
        
        [BsonElement("started_at")]
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        
        [BsonElement("last_activity")]
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        
        [BsonElement("status")]
        public string Status { get; set; } = "active"; // active, ended_by_logout, ended_by_admin_stop
        
        [BsonElement("ended_at")]
        public DateTime? EndedAt { get; set; }
        
        [BsonElement("reason")]
        public string? Reason { get; set; }
        
        [BsonElement("create_date")]
        public DateTime CreateDate { get; set; } = DateTime.UtcNow;
        
        [BsonElement("update_date")]
        public DateTime UpdateDate { get; set; } = DateTime.UtcNow;
    }
}
