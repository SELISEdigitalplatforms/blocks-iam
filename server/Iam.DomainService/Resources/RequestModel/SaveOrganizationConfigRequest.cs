
using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class SaveOrganizationConfigRequest
    {
        public string? OrganizationId { get; set; }
        public string? ItemId { get; set; }
        public bool AllowCreationFromCloud { get; set; }
        public bool AllowCreationFromConstruct { get; set; }
        public List<string> DefaultRoleSlugsForNewMembers { get; set; } = [];
        public bool IsMultiOrgEnabled { get; set; }
    }

    public class GetOrganizationConfigRequest
    {
        public string? OrganizationId { get; set; }
    }
}
}
