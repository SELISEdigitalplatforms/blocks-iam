using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class Permission : BuiltInPermission
    {
        public Dictionary<string, List<string>> Roles { get; set; } = [];
    }
}
