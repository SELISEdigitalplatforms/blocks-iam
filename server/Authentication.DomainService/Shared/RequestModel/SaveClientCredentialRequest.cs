
using Blocks.Genesis;

namespace Authentication.DomainService.Shared.RequestModel
{
    public class SaveClientCredentialRequest : IProjectKey
    {
        public string? Name { get; set; }
        public List<string> Roles { get; set; } = [];
        public Dictionary<string, List<string>> PermissionsByOrg { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? ProjectKey { get ; set ; }
    }

    public class DeleteClientCredentialRequest : IProjectKey
    {
        public string? ItemId { get; set; }
        public string? ProjectKey { get; set; }
    }
}
