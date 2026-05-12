using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Shared.Entities
{
    [BsonIgnoreExtraElements]
    public class Organization : BaseEntity
    {
        public string Name { get; set; }
        public bool IsEnable { get; set; } = true;
        public List<string> DefaultRoleForMembers { get; set; } = new List<string>();
    }
}
