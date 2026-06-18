using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class Role : BaseEntity
    {
       // public string OrganizationId { get; set; } = "default";  // Role is org-scoped
        public string Name { get; set; }
        public string Slug { get; set; }
        public List<string> AncestorRoleSlugs { get; set; } = new(); // Backend only property to maintain the hierarchy of roles for efficient querying. It contains the slugs of all ancestor roles up to the root.
        // Parent role slug is the source-of-truth for hierarchy.
        public string? ParentRoleSlug { get; set; }
        public bool CanCreateOwn { get; set; } = true; // Indicates if users with this role can create their own roles under it
        public string Description { get; set; }
        public long Count { get; set; }
        public bool CreatedFromDefault { get; set; } = false; // Indicates if the role is created from default roles on org creation
    }
}
