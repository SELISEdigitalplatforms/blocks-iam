using Blocks.Genesis;

namespace Identifier.DomainService.Dtos
{
    public class GetProjectResponse
    {
        public GetProjectResponseData? Data { get; set; }
        public Dictionary<string, string> Errors { get; set; } = new();
    }

    public class GetProjectResponseData
    {
        public string? Name { get; set; }
        public List<Applications> Applications { get; set; } = new List<Applications>();
        public string? ItemId { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public string? LastUpdatedBy { get; set; }
        public List<string>? OrganizationIds { get; set; }
        public string? CreatedBy { get; set; }
        public List<string>? Tags { get; set; }
        public string? TenantId { get; set; }
        public bool IsDisabled { get; set; }
        public string? Environment { get; set; }
        public string? TenantGroupId { get; set; }
        public string? TenantSlug { get; set; }
    }
}
