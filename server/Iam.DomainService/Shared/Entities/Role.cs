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

        /// <summary>
        /// Soft delete. Archived roles are hidden from the roles list and the assignable-roles
        /// picker, but the document is never removed, so audit history survives.
        /// </summary>
        /// <remarks>
        /// This field is newer than the role documents themselves, so queries that hide archived
        /// roles must use <c>Ne(x =&gt; x.IsArchived, true)</c>. A document written before this field
        /// existed has no <c>IsArchived</c> at all, and MongoDB does not match a missing field
        /// against <c>false</c> — <c>Eq(x =&gt; x.IsArchived, false)</c> would match no pre-existing
        /// role and empty the roles list for every tenant. Deserialisation is the opposite case and
        /// is safe: an absent field reads back as <c>false</c> from this initialiser.
        /// </remarks>
        public bool IsArchived { get; set; } = false;
    }
}
