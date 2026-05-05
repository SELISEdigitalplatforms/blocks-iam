using Blocks.Genesis;

namespace Authentication.DomainService.Entities
{
    public class ClientCredential : BaseEntity
    {
        public string? Name { get; set; }
        public string? ClientSecret { get; set; }
        public List<string> Roles { get; set; } = [];
        public Dictionary<string, List<string>> PermissionsByOrg { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsActive { get; set; }
        public List<string> Audiences { get; set; } = [];
    }
}
