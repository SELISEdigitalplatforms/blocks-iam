using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public class GetPermissionFilter
    {
        // Both of these are nullable for the same reason as GetRolesFilter.Search: a
        // non-nullable string on a bound model is implicitly [Required], which would reject a
        // caller that simply does not filter on them. Both are already blank-guarded downstream.
        public string? Search { get; set; }
        public ResourceType Type { get; set; }
        public PermissionSeverity PermissionSeverity { get; set; }
        public string? IsBuiltIn { get; set; } // "yes"/"no"
        public List<string> Tags { get; set; } = [];
        public List<string> Resources { get; set; } = [];
        public bool IsArchived { get; set; }
        public string? ResourceGroup { get; set; }
    }
}
