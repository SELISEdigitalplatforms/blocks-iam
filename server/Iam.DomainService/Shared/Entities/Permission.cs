using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class Permission : BuiltInPermission
    {
        public List<string> Roles { get; set; } = new List<string>();
        public string OrganizationId { get; set; } = "default";
    }
}
