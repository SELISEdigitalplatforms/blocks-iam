using Blocks.Genesis;
using Iam.DomainService.Dtos;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class UserAuthenticationTimeline : BaseEntity
    {
        public required string UserId { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceType { get; set; }
        public DeviceInformation? DeviceInformation { get; set; }
        public string? IpAddresses { get; set; }
        public string? Event { get; set; }
        public string? ActionBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }
    }
}
