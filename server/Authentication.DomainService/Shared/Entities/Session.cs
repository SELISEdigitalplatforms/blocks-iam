using Iam.DomainService.Dtos;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class Session : RefreshTokenEvent
    {
        [BsonId]
        public ObjectId ItemId { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public bool IsActive { get; set; }
        
        [BsonElement("auth_mode")]
        public string AuthMode { get; set; } = "root"; // root or impersonation
        
        [BsonElement("original_tenant_id")]
        public string? OriginalTenantId { get; set; }
        
        [BsonElement("impersonation_session_id")]
        public string? ImpersonationSessionId { get; set; }
        
        [BsonElement("is_revoked")]
        public bool IsRevoked { get; set; }
        
        [BsonElement("revoked_at")]
        public DateTime? RevokedAt { get; set; }
        
        [BsonElement("revocation_reason")]
        public string? RevocationReason { get; set; }
    }
}
