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
        public List<string> DefaultRoleOnOrgCreation { get; set; } = new List<string>();
        public List<string> DefaultPermissionOnOrgCreation { get; set; } = new List<string>();
    }
}
