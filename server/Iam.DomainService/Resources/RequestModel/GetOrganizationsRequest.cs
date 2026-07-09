using Blocks.Genesis;

namespace Iam.DomainService.Resources
{
    public class GetOrganizationsRequest : BaseGetsRequest<GetOrganizationsFilter>
    {
        /// <summary>
        /// Optional filter by organization name (partial match).
        /// </summary>
        
    }

    public class GetOrganizationsFilter
    {
        public string Search { get; set; }
        public List<string> Ids { get; set; } = [];
        public bool? IsDisabled { get; set; }
        public string? ParentOrganizationId { get; set; }
    }
}