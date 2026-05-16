using Identifier.DomainService.Shared.Entities;

namespace Identifier.DomainService.Dtos
{
    public class GetAssetResponse
    {
        public TenantAsset? Assets { get; set; }
        public long TotalCount { get; set; }
        public bool IsSuccess { get; set; }
    }
}
