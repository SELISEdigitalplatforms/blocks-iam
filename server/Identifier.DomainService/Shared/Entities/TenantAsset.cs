using Blocks.Genesis;
using Identifier.DomainService.Projects;

namespace Identifier.DomainService.Shared.Entities
{
    public class TenantAsset : BaseEntity
    {
        public string TenantGroupId { get; set; }
        public List<Resource> Resources { get; set; }
    }
}
