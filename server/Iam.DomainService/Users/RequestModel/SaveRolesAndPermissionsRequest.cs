using Blocks.Genesis;

namespace Iam.DomainService.Users
{
    public class SaveRolesAndPermissionsRequest : IProjectKey
    {
        public required string UserId { get; set; }
        public Dictionary<string, List<string>> Roles { get; set; } = new();
        public Dictionary<string, List<string>> Permissions { get; set; } = new();
        public string? ProjectKey { get; set; }

    }

}
