namespace Iam.DomainService.Resources.TenantPropagation
{
    /// <summary>
    /// One propagation target. Internal contract between <see cref="ITenantEnumeration"/>
    /// and <see cref="TenantPermissionPropagator"/> — never crosses the queue boundary.
    /// </summary>
    public class PermissionMutationTarget
    {
        public string TenantId { get; set; } = string.Empty;
        public string TenantName { get; set; } = string.Empty;
        public string DbConnectionString { get; set; } = string.Empty;
        public string DBName { get; set; } = string.Empty;
    }
}
