using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class SaveOrganizationRequest
    {
        public string Name { get; set; }
        public string? ItemId { get; set; }
        public bool IsEnable { get; set; }
    }
}
