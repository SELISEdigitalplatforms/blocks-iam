using Iam.DomainService.Enums;

namespace Iam.DomainService.Resources.TenantPropagation
{
    public class PropagationSummary
    {
        public string PermissionItemId { get; set; } = string.Empty;
        public MutationEventType Action { get; set; }
        public int TenantsAttempted { get; set; }
        public int TenantsSucceeded { get; set; }
        public int TenantsFailed { get; set; }
        public List<TenantPropagationResult> Results { get; set; } = new();
    }
}
