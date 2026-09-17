using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Shared.Entities
{
    [BsonIgnoreExtraElements]
    public class TenantConfiguration : BaseEntity
    {
        public bool IsEmailPasswordSignUpEnabled { get; set; }
        public bool IsSSoSignUpEnabled { get; set; }
        public List<string> DefaultRolesForNewUserOnSignUp { get; set; } = new List<string>();
        public List<string> DefaultPermissionsForNewUserOnSignUp { get; set; } = new List<string>();
        public bool AllowOrgCreationFromCloud { get; set; }
        public bool AllowOrgCreationFromConstruct { get; set; }
        public bool AllowOrgCreationFromSignup { get; set; }
        public bool AllowOrgCreationFromPortal { get; set; }
        public bool IsMultiOrgEnabled { get; set; }
        public bool ConsentForMultiOrgEnable { get; set; }
        public DateTime ConsentTimeForMultiOrgEnable { get; set; }

        /// <summary>
        /// Whether organization names must be unique within the tenant, case-insensitively.
        /// <para>
        /// Off by default, including for every configuration document written before this field
        /// existed -- a missing value deserializes as false. A tenant that wants the check turns
        /// it on through <c>POST iam/organizations/config</c>. Nothing in the system keys on an
        /// organization's name (memberships, tokens and scoping all carry ItemId), so duplicates
        /// are a presentation concern rather than an integrity one.
        /// </para>
        /// </summary>
        public bool IsOrgNameUniquenessEnabled { get; set; }
    }
}
