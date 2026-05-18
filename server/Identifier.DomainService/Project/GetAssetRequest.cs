using Blocks.Genesis;

namespace Identifier.DomainService.Projects
{
    public class GetAssetRequest : BaseGetsRequest<GetAssetFilter>
    {
        public string TenantGroupId { get; set; }
    }

    public class GetAssetFilter
    {
        public string Name { get; set; }
        public string Link { get; set; }
    }

    public class GetAssetResponse : BaseResponse
    {
        public long TotalCount { get; set; }
        public bool IsSuccess { get; set; }
    }
}


