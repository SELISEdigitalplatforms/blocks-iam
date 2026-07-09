using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class ClientCredential : BaseEntity
    {
        public string? Name { get; set; }
        public string? ClientSecret { get; set; }
        public int AccessTokenValidForNumberMinutes { get; set; } = 5;
        public List<string> Roles { get; set; } = new List<string>();
        public List<string> Permissions { get; set; } = new List<string>();
        public bool IsActive { get; set; }
    }
}
