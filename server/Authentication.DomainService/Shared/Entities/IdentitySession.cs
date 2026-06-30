using Iam.DomainService.Dtos;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public sealed class IdentitySession
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string ItemId { get; set; } = Guid.NewGuid().ToString();
        public string TenantId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string? OrganizationId { get; set; }
        public string? ClientId { get; set; }
        public string? SessionId { get; set; }
        public string RefreshToken { get; set; } = string.Empty;
        public string? IpAddresses { get; set; }
        public DeviceInformation? DeviceInformation { get; set; }
        public string? GrantType { get; set; }
        public bool IsLogin { get; set; }
        public bool IsActive { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
