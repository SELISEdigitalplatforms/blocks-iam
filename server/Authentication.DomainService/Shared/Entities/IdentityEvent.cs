using Iam.DomainService.Dtos;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class IdentityEvent
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        public string ItemId { get; set; } = Guid.NewGuid().ToString();
        public string TenantId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string? OrganizationId { get; set; }
        public string Event { get; set; } = string.Empty;
        public string? IpAddresses { get; set; }
        public DeviceInformation? DeviceInformation { get; set; }
        public string? ActionBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
